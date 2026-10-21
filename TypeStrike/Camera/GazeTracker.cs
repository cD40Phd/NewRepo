using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using TypeStrike.Game;

namespace TypeStrike.Camera;

public enum GazeState { Starting, Calibrating, OnScreen, LookingDown, NoFace, Error }

/// <summary>
/// Следит по веб-камере, не смотрит ли игрок на клавиатуру.
/// Это не точный eye-tracking, а эвристика: после калибровки (игрок смотрит на экран)
/// взгляд вниз распознаётся по опусканию головы, по пропаже открытых глаз
/// (веки прикрываются, когда смотрят вниз) или по пропаже лица из кадра.
/// Кадры обрабатываются только в памяти и никуда не сохраняются.
/// </summary>
public sealed class GazeTracker : IDisposable
{
    const int FrameWidth = 320;
    const double CalibrationSeconds = 2.5;
    const int MinCalibrationSamples = 10;
    const int VoteWindow = 8;

    readonly Dispatcher _dispatcher;
    readonly CancellationTokenSource _cts = new();
    Task? _worker;
    volatile bool _recalibrate;
    double _headDropThreshold = 0.22;
    volatile bool _useEyes = true;
    DateTime _lastMetrics;

    public GazeTracker(Dispatcher dispatcher, int cameraIndex)
    {
        _dispatcher = dispatcher;
        CameraIndex = cameraIndex;
    }

    public int CameraIndex { get; }

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

    /// <summary>Калибровка завершена (в UI-потоке) — её можно сохранить и использовать в следующий раз.</summary>
    public event Action<GazeCalibration>? Calibrated;
    /// <summary>Текущие показатели для экрана настроек: наклон головы, лицо найдено, глаза найдены.</summary>
    public event Action<double, bool, bool>? Metrics;

    public GazeState State { get; private set; } = GazeState.Starting;
    public string? ErrorMessage { get; private set; }

    /// <summary>Вызывается в UI-потоке при смене состояния.</summary>
    public event Action<GazeState>? StateChanged;
    /// <summary>Кадр предпросмотра (зеркальный, с рамками лица и глаз), в UI-потоке.</summary>
    public event Action<BitmapSource>? Preview;

    /// <summary>Запуск. Если передана сохранённая калибровка — сразу начинаем следить, без калибровки.</summary>
    public void Start(GazeCalibration? calibration = null) => _worker ??= Task.Run(() => Run(calibration, _cts.Token));

    /// <summary>Заново запомнить положение «смотрю на экран».</summary>
    public void Recalibrate() => _recalibrate = true;

    sealed class Baseline
    {
        public int Samples;
        public int EyeHits;
        double _faceY, _faceH;

        public double FaceY => _faceY / Math.Max(1, Samples);
        public double FaceH => _faceH / Math.Max(1, Samples);
        public double EyeRate => Samples == 0 ? 0 : (double)EyeHits / Samples;

        public static Baseline From(GazeCalibration c) => new()
        {
            Samples = MinCalibrationSamples,
            EyeHits = (int)Math.Round(c.EyeRate * MinCalibrationSamples),
            _faceY = c.FaceY * MinCalibrationSamples,
            _faceH = c.FaceHeight * MinCalibrationSamples,
        };

        public GazeCalibration ToCalibration() => new() { FaceY = FaceY, FaceHeight = FaceH, EyeRate = EyeRate };

        public void Add(double faceY, double faceH, bool eyes)
        {
            Samples++;
            _faceY += faceY;
            _faceH += faceH;
            if (eyes)
                EyeHits++;
        }
    }

    void Run(GazeCalibration? initial, CancellationToken ct)
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

            var baseline = initial != null ? Baseline.From(initial) : new Baseline();
            var votes = new Queue<bool>();
            bool calibrated = initial != null;
            var calibrationStart = DateTime.UtcNow;
            var lastFaceSeen = DateTime.UtcNow;
            var lastPreview = DateTime.MinValue;
            SetState(calibrated ? GazeState.OnScreen : GazeState.Calibrating);

            while (!ct.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                if (_recalibrate)
                {
                    _recalibrate = false;
                    baseline = new Baseline();
                    votes.Clear();
                    calibrated = false;
                    calibrationStart = now;
                    SetState(GazeState.Calibrating);
                }

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

                if (face is Rect f)
                {
                    lastFaceSeen = now;
                    // Глаза ищем только в верхней половине лица
                    var upper = new Rect(f.X, f.Y, f.Width, f.Height * 55 / 100);
                    using var roi = new Mat(gray, upper);
                    var eyes = eyeCascade.DetectMultiScale(roi, 1.1, 4, HaarDetectionTypes.ScaleImage,
                        new Size(f.Width / 8, f.Width / 8));
                    eyesFound = eyes.Length > 0;

                    Cv2.Rectangle(small, f, Scalar.FromRgb(0x5C, 0xE1, 0xFF), 2);
                    foreach (var e in eyes)
                        Cv2.Rectangle(small, new Rect(f.X + e.X, f.Y + e.Y, e.Width, e.Height), Scalar.FromRgb(0x6B, 0xFF, 0xB0), 1);
                }

                double faceY = face is Rect r1 ? (r1.Y + r1.Height / 2.0) / gray.Rows : 0;
                double faceH = face is Rect r2 ? r2.Height / (double)gray.Rows : 0;

                if (!calibrated)
                {
                    if (face != null)
                        baseline.Add(faceY, faceH, eyesFound);
                    if ((now - calibrationStart).TotalSeconds >= CalibrationSeconds && baseline.Samples >= MinCalibrationSamples)
                    {
                        calibrated = true;
                        var result = baseline.ToCalibration();
                        _dispatcher.BeginInvoke(() => Calibrated?.Invoke(result));
                        SetState(GazeState.OnScreen);
                    }
                }
                else if (face == null && (now - lastFaceSeen).TotalSeconds > 5)
                {
                    // Лица давно нет — скорее всего, игрок отошёл; не считаем это подглядыванием
                    votes.Clear();
                    SetState(GazeState.NoFace);
                }
                else
                {
                    bool down;
                    double drop = face != null ? (faceY - baseline.FaceY) / Math.Max(0.01, baseline.FaceH) : 0;
                    if ((now - _lastMetrics).TotalMilliseconds >= 100)
                    {
                        _lastMetrics = now;
                        bool hasFace = face != null, hasEyes = eyesFound;
                        _dispatcher.BeginInvoke(() => Metrics?.Invoke(drop, hasFace, hasEyes));
                    }

                    if (face == null)
                    {
                        // Сильно опущенную голову детектор лица уже не находит
                        down = (now - lastFaceSeen).TotalSeconds > 0.4;
                    }
                    else
                    {
                        bool headDown = drop > HeadDropThreshold;
                        bool eyesHidden = UseEyes && baseline.EyeRate > 0.6 && !eyesFound;
                        down = headDown || eyesHidden;
                    }

                    votes.Enqueue(down);
                    if (votes.Count > VoteWindow)
                        votes.Dequeue();
                    int downVotes = votes.Count(v => v);

                    if (downVotes >= 6)
                        SetState(GazeState.LookingDown);
                    else if (downVotes <= 2)
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
