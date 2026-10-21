using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using TypeStrike.Game;

namespace TypeStrike.Camera;

public enum GazeState { Starting, NeedsCalibration, OnScreen, LookingAway, NoFace, Error }

/// <summary>Результат обработки кадра (для калибровки, игры и экрана настроек).</summary>
public sealed record GazeFrame(
    bool Face,
    bool Eyes,
    double FaceY,
    double FaceHeight,
    double[]? Features,
    double HeadDrop,
    double GazeX,
    double GazeY,
    bool HasGaze,
    double Fps,
    double ProcessingMs,
    double LatencyMs);

/// <summary>
/// Отслеживание взгляда веб-камерой (OpenCV).
/// В каждом кадре ищутся лицо и радужки — тёмный круг в разрезе век на белом фоне белка.
/// Признаки — положение радужки в разрезе глаза, сколько белка видно с каждой стороны,
/// насколько веко срезает радужку, положение лица в кадре — после калибровки по движущейся
/// точке переводятся моделью <see cref="GazeModel"/> в точку на экране.
///
/// Чтобы взгляд отслеживался без задержки:
/// • кадры читает отдельный поток, обработка всегда берёт самый свежий (буфер драйвера не копится);
/// • лицо ищется рядом с прошлым положением, а не по всему кадру;
/// • глаза берутся по пропорциям лица — медленный каскад глаз не нужен;
/// • признаки сглаживает фильтр One Euro (дрожание гасится, быстрые движения проходят сразу);
/// • решение «взгляд ушёл / вернулся» принимается по времени (~0,2 с), а не по числу кадров.
/// Кадры обрабатываются только в памяти и никуда не сохраняются.
/// </summary>
public sealed class GazeTracker : IDisposable
{
    const int FrameWidth = 320;
    /// <summary>За сколько секунд непрерывных признаков «не на экране» состояние переключается.</summary>
    const double AwayRiseSeconds = 0.2;
    /// <summary>За сколько секунд взгляда на экран состояние возвращается.</summary>
    const double AwayFallSeconds = 0.25;
    /// <summary>Лицо пропало дольше этого — считаем, что голова опущена к клавиатуре.</summary>
    const double FaceLostAwaySeconds = 0.25;
    /// <summary>Лицо пропало дольше этого — игрок отошёл.</summary>
    const double NoFaceSeconds = 5;
    /// <summary>Сколько секунд после потери лица ещё искать его рядом с прошлым местом.</summary>
    const double FaceTrackSeconds = 0.5;

    readonly Dispatcher _dispatcher;
    readonly CancellationTokenSource _cts = new();
    readonly object _frameLock = new();
    Thread? _grabber;
    Thread? _worker;
    Mat? _latest;
    long _latestTicks;
    bool _hasNewFrame;
    GazeCalibration? _calibration;
    double _headDropThreshold = 0.22;
    volatile bool _useEyes = true;
    volatile bool _previewEnabled = true;

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

    /// <summary>Учитывать пропажу радужки (веки прикрываются, когда смотрят вниз).</summary>
    public bool UseEyes
    {
        get => _useEyes;
        set => _useEyes = value;
    }

    /// <summary>Готовить ли картинку предпросмотра (это лишняя работа, когда её никто не видит).</summary>
    public bool PreviewEnabled
    {
        get => _previewEnabled;
        set => _previewEnabled = value;
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
        if (_worker != null)
            return;
        _worker = new Thread(() => Run(_cts.Token)) { IsBackground = true, Name = "Gaze", Priority = ThreadPriority.AboveNormal };
        _worker.Start();
    }

    /// <summary>Применить новую калибровку (null — следить нечем, нужна калибровка).</summary>
    public void SetCalibration(GazeCalibration? calibration) =>
        Volatile.Write(ref _calibration, calibration != null && calibration.HasModel(GazeModel.FeatureCount) ? calibration : null);

    static VideoCapture OpenCamera(int index)
    {
        var capture = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
        if (!capture.IsOpened())
        {
            capture.Dispose();
            throw new InvalidOperationException($"Камера {index + 1} не найдена или занята другим приложением.");
        }
        // Небольшое разрешение и MJPG — больше кадров в секунду; буфер в 1 кадр — меньше задержка
        capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
        capture.Set(VideoCaptureProperties.FrameWidth, 640);
        capture.Set(VideoCaptureProperties.FrameHeight, 480);
        capture.Set(VideoCaptureProperties.Fps, 30);
        capture.Set(VideoCaptureProperties.BufferSize, 1);
        return capture;
    }

    /// <summary>Поток захвата: читает кадры без остановки и держит только самый свежий.</summary>
    void Grab(VideoCapture capture, CancellationToken ct)
    {
        var buffer = new Mat();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!capture.Read(buffer) || buffer.Empty())
                {
                    Thread.Sleep(10);
                    continue;
                }
                lock (_frameLock)
                {
                    (_latest, buffer) = (buffer, _latest ?? new Mat());
                    _latestTicks = Stopwatch.GetTimestamp();
                    _hasNewFrame = true;
                    Monitor.Pulse(_frameLock);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            SetState(GazeState.Error);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>Ждёт свежий кадр и забирает его, отдавая взамен уже обработанный буфер.</summary>
    bool TakeLatest(ref Mat work, out long ticks, CancellationToken ct)
    {
        lock (_frameLock)
        {
            while (!_hasNewFrame)
            {
                if (ct.IsCancellationRequested)
                {
                    ticks = 0;
                    return false;
                }
                Monitor.Wait(_frameLock, 100);
            }
            (work, _latest) = (_latest!, work);
            ticks = _latestTicks;
            _hasNewFrame = false;
            return true;
        }
    }

    void Run(CancellationToken ct)
    {
        VideoCapture? capture = null;
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Camera");
            using var faceCascade = new CascadeClassifier(Path.Combine(dir, "haarcascade_frontalface_default.xml"));
            if (faceCascade.Empty())
                throw new InvalidOperationException("Не найден файл каскада лица в папке Camera.");

            capture = OpenCamera(CameraIndex);
            var captureRef = capture;
            _grabber = new Thread(() => Grab(captureRef, ct)) { IsBackground = true, Name = "GazeGrab" };
            _grabber.Start();

            var frame = new Mat();
            using var small = new Mat();
            using var gray = new Mat();
            var filter = new OneEuroFilter();
            var clock = Stopwatch.StartNew();
            double lastFaceSeen = 0, lastFrameTime = 0, lastPreview = -1, awayScore = 0;
            double fps = 0, processingMs = 0, latencyMs = 0;
            Rect? lastFace = null;

            while (!ct.IsCancellationRequested)
            {
                if (!TakeLatest(ref frame, out long grabbedAt, ct))
                    break;

                long started = Stopwatch.GetTimestamp();
                double now = clock.Elapsed.TotalSeconds;
                double dt = lastFrameTime > 0 ? now - lastFrameTime : 0;
                lastFrameTime = now;

                int height = (int)(frame.Rows * (FrameWidth / (double)frame.Cols));
                Cv2.Resize(frame, small, new Size(FrameWidth, height), 0, 0, InterpolationFlags.Area);
                Cv2.Flip(small, small, FlipMode.Y);
                Cv2.CvtColor(small, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.EqualizeHist(gray, gray);

                var face = FindFace(faceCascade, gray, now - lastFaceSeen < FaceTrackSeconds ? lastFace : null);
                bool eyesFound = false;
                double[]? features = null;
                bool drawPreview = PreviewEnabled && now - lastPreview >= 0.1;

                if (face is Rect f)
                {
                    lastFaceSeen = now;
                    lastFace = f;
                    features = Analyze(gray, drawPreview ? small : null, f, out eyesFound);
                    if (drawPreview)
                        Cv2.Rectangle(small, f, Scalar.FromRgb(0x5C, 0xE1, 0xFF), 2);
                }

                double[]? smoothed = null;
                if (features != null)
                    smoothed = filter.Filter(features, dt);
                else if (face == null)
                    filter.Reset();

                var calibration = Volatile.Read(ref _calibration);
                double faceY = face is Rect r1 ? (r1.Y + r1.Height / 2.0) / gray.Rows : 0;
                double faceH = face is Rect r2 ? r2.Height / (double)gray.Rows : 0;
                double drop = face != null && calibration != null
                    ? (faceY - calibration.FaceY) / Math.Max(0.01, calibration.FaceHeight)
                    : 0;
                double gazeX = 0, gazeY = 0;
                bool hasGaze = calibration != null && smoothed != null;
                if (hasGaze)
                    (gazeX, gazeY) = GazeModel.Predict(calibration!, smoothed!);

                // Решение по времени: «очки» отведённого взгляда растут за AwayRiseSeconds и падают за AwayFallSeconds
                if (calibration == null)
                {
                    awayScore = 0;
                    SetState(GazeState.NeedsCalibration);
                }
                else if (face == null && now - lastFaceSeen > NoFaceSeconds)
                {
                    awayScore = 0;
                    SetState(GazeState.NoFace);
                }
                else
                {
                    bool away = face == null
                        ? now - lastFaceSeen > FaceLostAwaySeconds
                        : drop > HeadDropThreshold
                          || (UseEyes && calibration.EyeRate > 0.6 && !eyesFound)
                          || (hasGaze && IsOffScreen(gazeX, gazeY));
                    awayScore = away
                        ? Math.Min(1, awayScore + dt / AwayRiseSeconds)
                        : Math.Max(0, awayScore - dt / AwayFallSeconds);

                    if (awayScore >= 1)
                        SetState(GazeState.LookingAway);
                    else if (awayScore <= 0)
                        SetState(GazeState.OnScreen);
                }

                // Диагностика: кадры в секунду, время обработки, задержка от захвата кадра до результата
                long finished = Stopwatch.GetTimestamp();
                double frameMs = (finished - started) * 1000.0 / Stopwatch.Frequency;
                double lagMs = (finished - grabbedAt) * 1000.0 / Stopwatch.Frequency;
                processingMs += (frameMs - processingMs) * 0.1;
                latencyMs += (lagMs - latencyMs) * 0.1;
                if (dt > 0)
                    fps += (1 / dt - fps) * 0.1;

                var report = new GazeFrame(face != null, eyesFound, faceY, faceH, smoothed,
                    drop, gazeX, gazeY, hasGaze, fps, processingMs, latencyMs);
                _dispatcher.BeginInvoke(DispatcherPriority.Send, () => Frame?.Invoke(report));

                if (drawPreview)
                {
                    lastPreview = now;
                    var bitmap = small.ToBitmapSource();
                    bitmap.Freeze();
                    _dispatcher.BeginInvoke(() => Preview?.Invoke(bitmap));
                }
            }
            frame.Dispose();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            SetState(GazeState.Error);
        }
        finally
        {
            _cts.Cancel();
            _grabber?.Join(1000);
            capture?.Dispose();
            lock (_frameLock)
            {
                _latest?.Dispose();
                _latest = null;
            }
        }
    }

    /// <summary>
    /// Ищет лицо. Если оно было в прошлом кадре — только в окрестности прошлого места
    /// и близкого размера (в разы быстрее); иначе — по всему кадру.
    /// </summary>
    static Rect? FindFace(CascadeClassifier cascade, Mat gray, Rect? previous)
    {
        var bounds = new Rect(0, 0, gray.Cols, gray.Rows);
        if (previous is Rect p)
        {
            int margin = p.Width / 2;
            var area = new Rect(p.X - margin, p.Y - margin, p.Width + 2 * margin, p.Height + 2 * margin) & bounds;
            if (area.Width > 20 && area.Height > 20)
            {
                Rect[] near;
                using (var roi = new Mat(gray, area))
                    near = cascade.DetectMultiScale(roi, 1.1, 3, HaarDetectionTypes.ScaleImage,
                        new Size(p.Width * 7 / 10, p.Height * 7 / 10), new Size(p.Width * 14 / 10, p.Height * 14 / 10));
                if (near.Length > 0)
                {
                    var best = near.MaxBy(r => r.Width * r.Height);
                    return new Rect(area.X + best.X, area.Y + best.Y, best.Width, best.Height);
                }
            }
        }

        var faces = cascade.DetectMultiScale(gray, 1.15, 5, HaarDetectionTypes.ScaleImage, new Size(60, 60));
        return faces.Length > 0 ? faces.MaxBy(r => r.Width * r.Height) : null;
    }

    /// <summary>Точка взгляда за пределами окна (с запасом на неточность модели).</summary>
    public static bool IsOffScreen(double x, double y) => y > 1.12 || y < -0.35 || x < -0.3 || x > 1.3;

    /// <summary>Признаки одного глаза (всё нормировано на размер разреза глаза).</summary>
    readonly record struct EyeFeatures(double IrisX, double IrisY, double Openness, double ScleraBalance);

    /// <summary>
    /// Анализ обоих глаз. Области глаз берутся по пропорциям лица (центры глаз у фронтального
    /// лица — примерно на 30 % и 70 % ширины и 38 % высоты рамки) — это быстрее и стабильнее
    /// каскада глаз. Возвращает [радужка X, радужка Y, открытость, баланс белка,
    /// центр лица X, Y, ширина лица] или null, если радужка не видна ни в одном глазу.
    /// </summary>
    static double[]? Analyze(Mat gray, Mat? preview, Rect face, out bool eyesFound)
    {
        int size = face.Width * 26 / 100;
        int centerY = face.Y + face.Height * 38 / 100;
        var found = new List<EyeFeatures>(2);
        foreach (int centerX in new[] { face.X + face.Width * 30 / 100, face.X + face.Width * 70 / 100 })
        {
            var eye = new Rect(centerX - size / 2, centerY - size / 2, size, size);
            if (preview != null)
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
    static EyeFeatures? AnalyzeEye(Mat gray, Mat? preview, Rect eye)
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

        if (preview != null)
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
        _dispatcher.BeginInvoke(DispatcherPriority.Send, () => StateChanged?.Invoke(state));
    }

    public void Dispose()
    {
        _cts.Cancel();
        lock (_frameLock)
            Monitor.PulseAll(_frameLock);
        // Токен не освобождаем: поток может завершаться чуть позже и ещё проверяет его
        _worker?.Join(1500);
    }
}
