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
    bool Tracking,
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
/// Слежение за направлением головы веб-камерой (OpenCV).
/// У обычной камеры на расстоянии вытянутой руки глаза занимают несколько пикселей,
/// поэтому зрачки и направление взгляда не отследить — следим за поворотом и наклоном головы.
///
/// • Лицо находит каскад Хаара (рядом с прошлым положением — в разы быстрее).
/// • В центре лица (глаза, нос, рот) выбираются 20–40 опорных точек, и они отслеживаются
///   оптическим потоком Лукаса–Канаде: это субпиксельно точно и плавно, в отличие от рамки лица.
///   Когда голову опускают к клавиатуре, каскад часто теряет лицо, а точки ещё видны.
/// • Признаки: центр точек в кадре, центр и ширина рамки лица и смещение точек
///   относительно рамки (при повороте головы черты лица уходят к краю рамки).
/// • После калибровки (игрок «указывает носом» на движущуюся точку) модель <see cref="GazeModel"/>
///   переводит признаки в точку на экране, куда направлена голова.
/// • Голова «не на экран»: точка ушла за границы окна (обычно вниз — на клавиатуру),
///   голова заметно опущена или лицо и точки потеряны.
/// Кадры обрабатываются только в памяти и никуда не сохраняются.
/// </summary>
public sealed class GazeTracker : IDisposable
{
    const int FrameWidth = 320;
    /// <summary>За сколько секунд непрерывных признаков «не на экран» состояние переключается.</summary>
    const double AwayRiseSeconds = 0.2;
    /// <summary>За сколько секунд «на экран» состояние возвращается.</summary>
    const double AwayFallSeconds = 0.25;
    /// <summary>Лицо и точки потеряны дольше этого — считаем, что голова опущена к клавиатуре.</summary>
    const double LostAwaySeconds = 0.25;
    /// <summary>Лицо пропало дольше этого — игрок отошёл.</summary>
    const double NoFaceSeconds = 5;
    /// <summary>Сколько секунд после потери лица ещё искать его рядом с прошлым местом.</summary>
    const double FaceTrackSeconds = 0.5;
    /// <summary>Без рамки лица точки ведём не дольше этого — дальше им нельзя доверять.</summary>
    const double PointsWithoutFaceSeconds = 1.5;
    const int MaxPoints = 40;
    const int MinPoints = 8;

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

    /// <summary>Готовить ли картинку предпросмотра (это лишняя работа, когда её никто не видит).</summary>
    public bool PreviewEnabled
    {
        get => _previewEnabled;
        set => _previewEnabled = value;
    }

    /// <summary>Вызывается в UI-потоке при смене состояния.</summary>
    public event Action<GazeState>? StateChanged;
    /// <summary>Кадр предпросмотра (зеркальный, с рамкой лица и опорными точками), в UI-потоке.</summary>
    public event Action<BitmapSource>? Preview;
    /// <summary>Признаки и оценка направления головы по каждому кадру, в UI-потоке.</summary>
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
            using var previousGray = new Mat();
            var head = new HeadPoints();
            var filter = new OneEuroFilter();
            var clock = Stopwatch.StartNew();
            double lastFaceSeen = 0, lastTracked = 0, lastFrameTime = 0, lastPreview = -1, awayScore = 0;
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
                // Для оптического потока нужна исходная яркость; выравнивание — только для каскада
                using var equalized = new Mat();
                Cv2.EqualizeHist(gray, equalized);

                var face = FindFace(faceCascade, equalized, now - lastFaceSeen < FaceTrackSeconds ? lastFace : null);
                if (face is Rect found)
                {
                    lastFaceSeen = now;
                    lastFace = found;
                }

                bool faceRecent = lastFace != null && now - lastFaceSeen < PointsWithoutFaceSeconds;
                bool tracking = faceRecent && head.Update(previousGray, gray, face ?? lastFace!.Value, face != null);
                gray.CopyTo(previousGray);
                if (tracking)
                    lastTracked = now;
                else
                    head.Reset();

                bool drawPreview = PreviewEnabled && now - lastPreview >= 0.1;
                if (drawPreview)
                {
                    if (face is Rect f)
                        Cv2.Rectangle(small, f, Scalar.FromRgb(0x5C, 0xE1, 0xFF), 2);
                    head.Draw(small);
                }

                double[]? features = tracking ? head.Features(lastFace!.Value, gray.Cols, gray.Rows) : null;
                double[]? smoothed = null;
                if (features != null)
                    smoothed = filter.Filter(features, dt);
                else
                    filter.Reset();

                var calibration = Volatile.Read(ref _calibration);
                double faceY = lastFace is Rect r1 ? (r1.Y + r1.Height / 2.0) / gray.Rows : 0;
                double faceH = lastFace is Rect r2 ? r2.Height / (double)gray.Rows : 0;
                double drop = face != null && calibration != null
                    ? (faceY - calibration.FaceY) / Math.Max(0.01, calibration.FaceHeight)
                    : 0;
                double gazeX = 0, gazeY = 0;
                bool hasGaze = calibration != null && smoothed != null;
                if (hasGaze)
                    (gazeX, gazeY) = GazeModel.Predict(calibration!, smoothed!);

                // Решение по времени: «очки» отведённой головы растут за AwayRiseSeconds и падают за AwayFallSeconds
                if (calibration == null)
                {
                    awayScore = 0;
                    SetState(GazeState.NeedsCalibration);
                }
                else if (!tracking && now - Math.Max(lastFaceSeen, lastTracked) > NoFaceSeconds)
                {
                    awayScore = 0;
                    SetState(GazeState.NoFace);
                }
                else
                {
                    bool away = !tracking
                        ? now - lastTracked > LostAwaySeconds
                        : drop > HeadDropThreshold || (hasGaze && IsOffScreen(gazeX, gazeY));
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

                var report = new GazeFrame(face != null, tracking, faceY, faceH, smoothed,
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

    /// <summary>Точка за пределами окна (с запасом на неточность модели).</summary>
    public static bool IsOffScreen(double x, double y) => y > 1.12 || y < -0.35 || x < -0.3 || x > 1.3;

    /// <summary>
    /// Опорные точки на лице, отслеживаемые оптическим потоком.
    /// При пересеве точек положение их центра сохраняется непрерывным (без скачка указателя).
    /// </summary>
    sealed class HeadPoints
    {
        Point2f[] _points = Array.Empty<Point2f>();
        Point2d _shift;
        Point2d _center;
        bool _hasCenter;
        int _framesSinceSeed;

        public void Reset()
        {
            _points = Array.Empty<Point2f>();
            _hasCenter = false;
            _shift = default;
        }

        /// <summary>Сдвигает точки на новый кадр; при необходимости выбирает новые. false — следить нечем.</summary>
        public bool Update(Mat previous, Mat current, Rect face, bool faceDetected)
        {
            if (_points.Length > 0 && !previous.Empty())
            {
                var next = Array.Empty<Point2f>();
                Cv2.CalcOpticalFlowPyrLK(previous, current, _points, ref next, out byte[] status, out float[] error,
                    new Size(15, 15), 2);
                var area = Inflate(face, face.Width / 3);
                var kept = new List<Point2f>(_points.Length);
                for (int i = 0; i < next.Length; i++)
                    if (status[i] == 1 && error[i] < 25 && area.Contains((int)next[i].X, (int)next[i].Y))
                        kept.Add(next[i]);
                _points = kept.ToArray();
            }

            _framesSinceSeed++;
            // Точек мало или они давно не обновлялись — выбираем новые, пока лицо видно
            if (faceDetected && (_points.Length < MinPoints || _framesSinceSeed > 150))
                Seed(current, face);

            if (_points.Length < MinPoints / 2)
                return false;

            // Поправка на непрерывность медленно (≈10 с) уходит к нулю: накопленный дрейф
            // оптического потока не копится, а центр точек снова привязывается к лицу
            _shift = new Point2d(_shift.X * 0.997, _shift.Y * 0.997);

            var raw = Mean(_points);
            _center = new Point2d(raw.X + _shift.X, raw.Y + _shift.Y);
            _hasCenter = true;
            return true;
        }

        void Seed(Mat gray, Rect face)
        {
            // Центр лица: глаза, нос, рот — там самые надёжные для слежения уголки
            var region = new Rect(face.X + face.Width / 4, face.Y + face.Height / 4, face.Width / 2, face.Height * 55 / 100)
                         & new Rect(0, 0, gray.Cols, gray.Rows);
            if (region.Width < 8 || region.Height < 8)
                return;
            using var mask = new Mat(gray.Size(), MatType.CV_8UC1, Scalar.All(0));
            Cv2.Rectangle(mask, region, Scalar.All(255), -1);
            var fresh = Cv2.GoodFeaturesToTrack(gray, MaxPoints, 0.01, Math.Max(3, face.Width / 20), mask, 3, false, 0.04);
            if (fresh.Length < MinPoints)
                return;

            // Указатель не должен прыгать: новый центр «пришиваем» к прежнему
            var freshMean = Mean(fresh);
            _shift = _hasCenter ? new Point2d(_center.X - freshMean.X, _center.Y - freshMean.Y) : default;
            _points = fresh;
            _framesSinceSeed = 0;
        }

        /// <summary>[центр точек X, Y, центр лица X, Y, ширина лица, смещение точек от центра лица X, Y].</summary>
        public double[] Features(Rect face, int width, int height)
        {
            double faceCx = face.X + face.Width / 2.0, faceCy = face.Y + face.Height / 2.0;
            return new[]
            {
                _center.X / width,
                _center.Y / height,
                faceCx / width,
                faceCy / height,
                face.Width / (double)width,
                (_center.X - faceCx) / face.Width,
                (_center.Y - faceCy) / face.Height,
            };
        }

        public void Draw(Mat preview)
        {
            foreach (var p in _points)
                Cv2.Circle(preview, new Point((int)p.X, (int)p.Y), 1, Scalar.FromRgb(0xFF, 0x9F, 0x1C), -1);
            if (_hasCenter && _points.Length > 0)
            {
                var c = Mean(_points);
                Cv2.DrawMarker(preview, new Point((int)c.X, (int)c.Y), Scalar.FromRgb(0x6B, 0xFF, 0xB0), MarkerTypes.Cross, 12, 2);
            }
        }

        static Point2d Mean(Point2f[] points) =>
            new(points.Average(p => p.X), points.Average(p => p.Y));

        static Rect Inflate(Rect r, int by) => new(r.X - by, r.Y - by, r.Width + 2 * by, r.Height + 2 * by);
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
