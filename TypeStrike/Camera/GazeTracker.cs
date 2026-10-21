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
/// В каждом кадре ищутся лицо, глаза и зрачки (самая тёмная область внутри глаза).
/// Признаки — положение зрачков относительно лица и положение лица в кадре — после
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
    /// <summary>Кадр предпросмотра (зеркальный, с рамками лица, глаз и зрачками), в UI-потоке.</summary>
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

    /// <summary>
    /// Ищет глаза и зрачки. Возвращает признаки
    /// [зрачок X и Y относительно лица, центр лица X и Y в кадре, ширина лица] или null.
    /// </summary>
    static double[]? Analyze(Mat gray, Mat preview, Rect face, CascadeClassifier eyeCascade, out bool eyesFound)
    {
        // Глаза ищем только в верхней половине лица
        var upper = new Rect(face.X, face.Y, face.Width, face.Height * 55 / 100);
        Rect[] eyes;
        using (var roi = new Mat(gray, upper))
            eyes = eyeCascade.DetectMultiScale(roi, 1.1, 4, HaarDetectionTypes.ScaleImage, new Size(face.Width / 8, face.Width / 8));
        eyesFound = eyes.Length > 0;

        double sumX = 0, sumY = 0;
        int pupils = 0;
        foreach (var e in eyes.OrderByDescending(e => e.Width * e.Height).Take(2))
        {
            var eye = new Rect(face.X + e.X, face.Y + e.Y, e.Width, e.Height);
            Cv2.Rectangle(preview, eye, Scalar.FromRgb(0x6B, 0xFF, 0xB0), 1);
            if (FindPupil(gray, eye) is Point2d pupil)
            {
                Cv2.Circle(preview, new Point((int)pupil.X, (int)pupil.Y), 2, Scalar.FromRgb(0xFF, 0x9F, 0x1C), -1);
                sumX += pupil.X;
                sumY += pupil.Y;
                pupils++;
            }
        }
        if (pupils == 0)
            return null;

        return new[]
        {
            (sumX / pupils - face.X) / face.Width,
            (sumY / pupils - face.Y) / face.Height,
            (face.X + face.Width / 2.0) / gray.Cols,
            (face.Y + face.Height / 2.0) / gray.Rows,
            face.Width / (double)gray.Cols,
        };
    }

    /// <summary>Зрачок — центр самых тёмных пикселей в средней части глаза (без брови и уголков).</summary>
    static Point2d? FindPupil(Mat gray, Rect eye)
    {
        var inner = new Rect(eye.X + eye.Width * 15 / 100, eye.Y + eye.Height * 30 / 100,
            eye.Width * 70 / 100, eye.Height * 50 / 100);
        inner &= new Rect(0, 0, gray.Cols, gray.Rows);
        if (inner.Width < 4 || inner.Height < 3)
            return null;

        using var roi = new Mat(gray, inner);
        using var blurred = new Mat();
        using var mask = new Mat();
        Cv2.GaussianBlur(roi, blurred, new Size(5, 5), 0);
        Cv2.MinMaxLoc(blurred, out double min, out double max);
        if (max - min < 10)
            return null;
        Cv2.Threshold(blurred, mask, min + (max - min) * 0.25, 255, ThresholdTypes.BinaryInv);

        var m = Cv2.Moments(mask, true);
        if (m.M00 < 3)
            return null;
        return new Point2d(inner.X + m.M10 / m.M00, inner.Y + m.M01 / m.M00);
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
