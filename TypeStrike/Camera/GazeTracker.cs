using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using TypeStrike.Game;

namespace TypeStrike.Camera;

public enum GazeState { Starting, NeedsCalibration, OnScreen, LookingAway, NoFace, Error }

/// <summary>Результат обработки кадра (для калибровки и экрана настроек).</summary>
public sealed record GazeFrame(
    bool Face,
    bool Eyes,
    double FaceY,
    double FaceHeight,
    double[]? Features,
    double HeadDrop,
    double GazeX,
    double GazeY,
    bool HasGaze);

/// <summary>
/// Отслеживание взгляда веб-камерой (OpenCV, каскады Хаара).
/// В каждом кадре ищутся лицо, глаза и радужки — тёмный круг в разрезе век на белом фоне белка.
/// Признаки — положение радужки в разрезе глаза, сколько белка видно с каждой стороны,
/// насколько веко срезает радужку, положение лица в кадре — после
/// калибровки по движущейся точке переводятся моделью <see cref="GazeModel"/> в точку на экране.
/// Взгляд «не на экране»: точка ушла за границы окна (обычно вниз, на клавиатуру),
/// голова заметно опущена, открытые глаза пропали или лицо исчезло из кадра.
/// Кадры обрабатываются только в памяти и никуда не сохраняются.
/// </summary>
public sealed class GazeTracker : IDisposable
{
    const int FrameWidth = 320;
    const int VoteWindow = 8;
    /// <summary>Сглаживание признаков между кадрами (доля нового кадра).</summary>
    const double Smoothing = 0.5;

    readonly Dispatcher _dispatcher;
    readonly CancellationTokenSource _cts = new();
    Task? _worker;
    GazeCalibration? _calibration;
    double _headDropThreshold = 0.22;
    volatile bool _useEyes = true;

    public GazeTracker(Dispatcher dispatcher, int cameraIndex)
    {
        _dispatcher = dispatcher;
        CameraIndex = cameraIndex;
    }

    public int CameraIndex { get; }
    public GazeState State { get; private set; } = GazeState.Starting;
    public string? ErrorMessage { get; private set; }

    /// <summary>На сколько высот лица должен сместиться центр лица вниз, чтобы считать голову опущенной.</summary>
    public double HeadDropThreshold
    {
        get => Volatile.Read(ref _headDropThreshold);
        set => Volatile.Write(ref _headDropThreshold, value);
    }

    /// <summary>Учитывать пропажу открытых глаз.</summary>
    public bool UseEyes
    {
        get => _useEyes;
        set => _useEyes = value;
    }

    /// <summary>Вызывается в UI-потоке при смене состояния.</summary>
    public event Action<GazeState>? StateChanged;
    /// <summary>Кадр предпросмотра (зеркальный, с рамками лица, глаз и радужек), в UI-потоке.</summary>
    public event Action<BitmapSource>? Preview;
    /// <summary>Признаки и оценка взгляда по каждому кадру, в UI-потоке.</summary>
    public event Action<GazeFrame>? Frame;

    public void Start(GazeCalibration? calibration)
    {
        SetCalibration(calibration);
        _worker ??= Task.Run(() => Run(_cts.Token));
    }

    /// <summary>Применить новую калибровку (null — следить нечем, нужна калибровка).</summary>
    public void SetCalibration(GazeCalibration? calibration) =>
        Volatile.Write(ref _calibration, calibration != null && calibration.HasModel(GazeModel.FeatureCount) ? calibration : null);

    void Run(CancellationToken ct)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Camera");
            using var faceCascade = new CascadeClassifier(Path.Combine(dir, "haarcascade_frontalface_default.xml"));
            using var eyeCascade = new CascadeClassifier(Path.Combine(dir, "haarcascade_eye_tree_eyeglasses.xml"));
            if (faceCascade.Empty() || eyeCascade.Empty())
                throw new InvalidOperationException("Не найдены файлы каскадов в папке Camera.");

            using var capture = new VideoCapture(CameraIndex, VideoCaptureAPIs.DSHOW);
            if (!capture.IsOpened())
                throw new InvalidOperationException($"Камера {CameraIndex + 1} не найдена или занята другим приложением.");

            using var frame = new Mat();
            using var small = new Mat();
            using var gray = new Mat();

            var votes = new Queue<bool>();
            var lastFaceSeen = DateTime.UtcNow;
            var lastPreview = DateTime.MinValue;
            double[]? smoothed = null;

            while (!ct.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                if (!capture.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(30);
                    continue;
                }

                int height = (int)(frame.Rows * (FrameWidth / (double)frame.Cols));
                Cv2.Resize(frame, small, new Size(FrameWidth, height));
                Cv2.Flip(small, small, FlipMode.Y);
                Cv2.CvtColor(small, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.EqualizeHist(gray, gray);

                var faces = faceCascade.DetectMultiScale(gray, 1.15, 5, HaarDetectionTypes.ScaleImage, new Size(60, 60));
                Rect? face = faces.Length > 0 ? faces.MaxBy(f => f.Width * f.Height) : null;
                bool eyesFound = false;
                double[]? features = null;

                if (face is Rect f)
                {
                    lastFaceSeen = now;
                    features = Analyze(gray, small, f, eyeCascade, out eyesFound);
                    Cv2.Rectangle(small, f, Scalar.FromRgb(0x5C, 0xE1, 0xFF), 2);
                }

                if (features != null)
                {
                    if (smoothed == null)
                        smoothed = features;
                    else
                        for (int i = 0; i < features.Length; i++)
                            smoothed[i] += (features[i] - smoothed[i]) * Smoothing;
                }
                else if (face == null)
                {
                    smoothed = null;
                }

                var calibration = Volatile.Read(ref _calibration);
                double faceY = face is Rect r1 ? (r1.Y + r1.Height / 2.0) / gray.Rows : 0;
                double faceH = face is Rect r2 ? r2.Height / (double)gray.Rows : 0;
                double drop = face != null && calibration != null
                    ? (faceY - calibration.FaceY) / Math.Max(0.01, calibration.FaceHeight)
                    : 0;
                double gazeX = 0, gazeY = 0;
                bool hasGaze = calibration != null && features != null && smoothed != null;
                if (hasGaze)
                    (gazeX, gazeY) = GazeModel.Predict(calibration!, smoothed!);

                var report = new GazeFrame(face != null, eyesFound, faceY, faceH,
                    features != null && smoothed != null ? (double[])smoothed.Clone() : null,
                    drop, gazeX, gazeY, hasGaze);
                _dispatcher.BeginInvoke(() => Frame?.Invoke(report));

                if (calibration == null)
                {
                    votes.Clear();
                    SetState(GazeState.NeedsCalibration);
                }
                else if (face == null && (now - lastFaceSeen).TotalSeconds > 5)
                {
                    // Лица давно нет — скорее всего, игрок отошёл
                    votes.Clear();
                    SetState(GazeState.NoFace);
                }
                else
                {
                    bool away;
                    if (face == null)
                    {
                        // Сильно опущенную голову детектор лица уже не находит
                        away = (now - lastFaceSeen).TotalSeconds > 0.4;
                    }
                    else
                    {
                        bool headDown = drop > HeadDropThreshold;
                        bool eyesHidden = UseEyes && calibration.EyeRate > 0.6 && !eyesFound;
                        bool offScreen = hasGaze && IsOffScreen(gazeX, gazeY);
                        away = headDown || eyesHidden || offScreen;
                    }

                    votes.Enqueue(away);
                    if (votes.Count > VoteWindow)
                        votes.Dequeue();
                    int awayVotes = votes.Count(v => v);

                    if (awayVotes >= 6)
                        SetState(GazeState.LookingAway);
                    else if (awayVotes <= 2)
                        SetState(GazeState.OnScreen);
                }

                if ((now - lastPreview).TotalMilliseconds >= 100)
                {
                    lastPreview = now;
                    var bitmap = small.ToBitmapSource();
                    bitmap.Freeze();
                    _dispatcher.BeginInvoke(() => Preview?.Invoke(bitmap));
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            SetState(GazeState.Error);
        }
    }

    /// <summary>Точка взгляда за пределами окна (с запасом на неточность модели).</summary>
    public static bool IsOffScreen(double x, double y) => y > 1.12 || y < -0.35 || x < -0.3 || x > 1.3;

    /// <summary>Признаки одного глаза (всё нормировано на размер разреза глаза).</summary>
    readonly record struct EyeFeatures(double IrisX, double IrisY, double Openness, double ScleraBalance);

    /// <summary>
    /// Ищет глаза и радужки. Возвращает признаки
    /// [радужка X, радужка Y, открытость, баланс белка, центр лица X, Y, ширина лица] или null.
    /// </summary>
    static double[]? Analyze(Mat gray, Mat preview, Rect face, CascadeClassifier eyeCascade, out bool eyesFound)
    {
        // Глаза ищем только в верхней половине лица
        var upper = new Rect(face.X, face.Y, face.Width, face.Height * 55 / 100);
        Rect[] eyes;
        using (var roi = new Mat(gray, upper))
            eyes = eyeCascade.DetectMultiScale(roi, 1.1, 4, HaarDetectionTypes.ScaleImage, new Size(face.Width / 8, face.Width / 8));

        var found = new List<EyeFeatures>();
        foreach (var e in eyes.OrderByDescending(e => e.Width * e.Height).Take(2))
        {
            var eye = new Rect(face.X + e.X, face.Y + e.Y, e.Width, e.Height);
            Cv2.Rectangle(preview, eye, Scalar.FromRgb(0x6B, 0xFF, 0xB0), 1);
            if (AnalyzeEye(gray, preview, eye) is EyeFeatures features)
                found.Add(features);
        }

        // «Глаза видны» — значит, видна радужка в разрезе век; если веки прикрыты, её нет
        eyesFound = found.Count > 0;
        if (found.Count == 0)
            return null;

        return new[]
        {
            found.Average(f => f.IrisX),
            found.Average(f => f.IrisY),
            found.Average(f => f.Openness),
            found.Average(f => f.ScleraBalance),
            (face.X + face.Width / 2.0) / gray.Cols,
            (face.Y + face.Height / 2.0) / gray.Rows,
            face.Width / (double)gray.Cols,
        };
    }

    /// <summary>
    /// Радужка целиком: тёмный круг в разрезе век на фоне белка.
    /// По горизонтали — самая тёмная вертикальная полоса шириной с радужку,
    /// по вертикали — строки, где эта полоса тёмная (верхнее веко срезает радужку,
    /// когда смотрят вниз). Белок — самые светлые пиксели слева и справа от радужки.
    /// </summary>
    static EyeFeatures? AnalyzeEye(Mat gray, Mat preview, Rect eye)
    {
        // Убираем бровь сверху, щёку снизу и уголки по бокам
        var inner = new Rect(eye.X + eye.Width * 8 / 100, eye.Y + eye.Height * 25 / 100,
            eye.Width * 84 / 100, eye.Height * 60 / 100);
        inner &= new Rect(0, 0, gray.Cols, gray.Rows);
        int w = inner.Width, h = inner.Height;
        if (w < 10 || h < 6)
            return null;

        using var roi = new Mat(gray, inner);
        using var blurred = new Mat();
        using var otsuMask = new Mat();
        Cv2.GaussianBlur(roi, blurred, new Size(3, 3), 0);
        double otsu = Cv2.Threshold(blurred, otsuMask, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        blurred.GetArray(out byte[] pixels);

        // Тёмное — только радужка. Одного порога Оцу мало: когда веко опущено,
        // он делит «кожа | белок» и засчитывает кожу века тёмной. Поэтому порог
        // не выше середины между самыми тёмными пикселями и медианой.
        var sorted = (byte[])pixels.Clone();
        Array.Sort(sorted);
        double low = sorted[sorted.Length * 5 / 100];
        double median = sorted[sorted.Length / 2];
        double darkThreshold = Math.Min(otsu, low + (median - low) * 0.6);
        var mask = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
            mask[i] = pixels[i] <= darkThreshold ? (byte)1 : (byte)0;

        // Самая тёмная вертикальная полоса шириной с радужку
        int irisWidth = Math.Max(3, (int)(w * 0.35));
        var columnDark = new int[w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (mask[y * w + x] != 0)
                    columnDark[x]++;

        int window = 0, best = -1, bestX = 0;
        for (int x = 0; x < w; x++)
        {
            window += columnDark[x];
            if (x >= irisWidth)
                window -= columnDark[x - irisWidth];
            if (x >= irisWidth - 1 && window > best)
            {
                best = window;
                bestX = x - irisWidth + 1;
            }
        }
        // Радужки не видно (веки прикрыты или смотрят сильно вниз)
        if (best < irisWidth * h * 0.15)
            return null;

        // Строки, в которых видна радужка
        int top = -1, bottom = -1;
        for (int y = 0; y < h; y++)
        {
            int count = 0;
            for (int x = bestX; x < bestX + irisWidth; x++)
                if (mask[y * w + x] != 0)
                    count++;
            if (count > irisWidth * 0.35)
            {
                if (top < 0)
                    top = y;
                bottom = y;
            }
        }
        if (top < 0)
            return null;
        int visible = bottom - top + 1;

        // Белок: светлые пиксели слева и справа от радужки в разрезе век
        double brightThreshold = median + (255 - median) * 0.4;
        int left = 0, right = 0;
        for (int y = top; y <= bottom; y++)
            for (int x = 0; x < w; x++)
            {
                if (pixels[y * w + x] <= brightThreshold)
                    continue;
                if (x < bestX)
                    left++;
                else if (x >= bestX + irisWidth)
                    right++;
            }

        Cv2.Rectangle(preview, new Rect(inner.X + bestX, inner.Y + top, irisWidth, visible), Scalar.FromRgb(0xFF, 0x9F, 0x1C), 1);

        return new EyeFeatures(
            IrisX: (bestX + irisWidth / 2.0) / w,
            IrisY: (top + bottom) / 2.0 / h,
            Openness: Math.Min(1.5, visible / (double)irisWidth),
            ScleraBalance: (left - right) / (double)(left + right + 1));
    }

    void SetState(GazeState state)
    {
        if (State == state)
            return;
        State = state;
        _dispatcher.BeginInvoke(() => StateChanged?.Invoke(state));
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _worker?.Wait(1500);
        }
        catch
        {
            // поток камеры завершается сам
        }
        _cts.Dispose();
    }
}
