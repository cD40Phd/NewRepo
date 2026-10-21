using System.IO;
using System.Windows.Media;

namespace TypeStrike.Game;

/// <summary>
/// Простые «ретро»-звуки, синтезированные в коде: WAV-файлы пишутся во временную папку
/// и проигрываются пулом MediaPlayer'ов, чтобы звуки могли накладываться друг на друга.
/// </summary>
public sealed class SoundEngine : IDisposable
{
    const int Rate = 22050;

    readonly Dictionary<Sfx, MediaPlayer[]> _players = new();
    readonly Dictionary<Sfx, int> _next = new();

    public bool Enabled { get; set; } = true;

    public SoundEngine()
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "TypeStrike", "sfx");
            Directory.CreateDirectory(dir);
            foreach (var sfx in Enum.GetValues<Sfx>())
            {
                var path = Path.Combine(dir, sfx + ".wav");
                File.WriteAllBytes(path, ToWav(Synthesize(sfx)));

                int voices = sfx is Sfx.Shoot or Sfx.Hit or Sfx.Miss ? 8 : 4;
                var pool = new MediaPlayer[voices];
                for (int i = 0; i < voices; i++)
                {
                    var player = new MediaPlayer { Volume = 0.6 };
                    player.Open(new Uri(path));
                    pool[i] = player;
                }
                _players[sfx] = pool;
            }
        }
        catch
        {
            // Нет звука — не беда, играем молча
            _players.Clear();
        }
    }

    public void Play(Sfx sfx)
    {
        if (!Enabled || !_players.TryGetValue(sfx, out var pool))
            return;

        int index = (_next.GetValueOrDefault(sfx) + 1) % pool.Length;
        _next[sfx] = index;
        try
        {
            var player = pool[index];
            player.Stop();
            player.Position = TimeSpan.Zero;
            player.Play();
        }
        catch
        {
            // игнорируем ошибки воспроизведения
        }
    }

    public void Dispose()
    {
        foreach (var player in _players.Values.SelectMany(p => p))
            player.Close();
        _players.Clear();
    }

    // ───────────────────────────── Синтез ─────────────────────────────

    enum Shape { Sine, Square, Saw, Triangle }

    static float[] Synthesize(Sfx sfx) => sfx switch
    {
        Sfx.Shoot => Tone(0.06, 1500, 600, Shape.Square, 0.16, 2),
        Sfx.Hit => Mix(Noise(0.05, 0.3, 3, 0.5), Tone(0.05, 320, 150, Shape.Triangle, 0.3, 2)),
        Sfx.Explode => Mix(Noise(0.35, 0.55, 2.5, 0.15), Tone(0.3, 150, 40, Shape.Sine, 0.5, 2)),
        Sfx.BigExplode => Mix(Noise(0.9, 0.75, 2, 0.08), Tone(0.8, 90, 25, Shape.Sine, 0.7, 1.5)),
        Sfx.Miss => Tone(0.12, 140, 110, Shape.Saw, 0.22, 1),
        Sfx.Emp => Mix(Tone(0.7, 900, 60, Shape.Sine, 0.5, 1.2), Noise(0.6, 0.25, 2, 0.05)),
        Sfx.WaveClear => Concat(
            Tone(0.09, 523, 523, Shape.Triangle, 0.4, 0.6),
            Tone(0.09, 659, 659, Shape.Triangle, 0.4, 0.6),
            Tone(0.09, 784, 784, Shape.Triangle, 0.4, 0.6),
            Tone(0.25, 1046, 1046, Shape.Triangle, 0.4, 1.2)),
        Sfx.ShipHit => Mix(Noise(0.5, 0.7, 2, 0.12), Tone(0.5, 200, 50, Shape.Square, 0.25, 2)),
        Sfx.GameOver => Mix(
            Noise(1.0, 0.7, 1.8, 0.07),
            Concat(
                Tone(0.25, 392, 392, Shape.Triangle, 0.4, 0.5),
                Tone(0.25, 330, 330, Shape.Triangle, 0.4, 0.5),
                Tone(0.6, 262, 196, Shape.Triangle, 0.4, 1))),
        _ => new float[1],
    };

    static float[] Tone(double duration, double f0, double f1, Shape shape, double volume, double decay)
    {
        int n = (int)(Rate * duration);
        var buffer = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            phase += (f0 + (f1 - f0) * t) / Rate;
            double x = phase % 1;
            double v = shape switch
            {
                Shape.Square => x < 0.5 ? 1 : -1,
                Shape.Saw => 2 * x - 1,
                Shape.Triangle => 4 * Math.Abs(x - 0.5) - 1,
                _ => Math.Sin(2 * Math.PI * phase),
            };
            double attack = Math.Min(1, i / (Rate * 0.003));
            buffer[i] = (float)(v * volume * attack * Math.Pow(1 - t, decay));
        }
        return buffer;
    }

    static float[] Noise(double duration, double volume, double decay, double smoothing)
    {
        var rng = new Random(42);
        int n = (int)(Rate * duration);
        var buffer = new float[n];
        double y = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            y += (rng.NextDouble() * 2 - 1 - y) * smoothing;
            buffer[i] = (float)(y * volume * Math.Pow(1 - t, decay));
        }
        return buffer;
    }

    static float[] Mix(params float[][] parts)
    {
        var result = new float[parts.Max(p => p.Length)];
        foreach (var part in parts)
            for (int i = 0; i < part.Length; i++)
                result[i] += part[i];
        return result;
    }

    static float[] Concat(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    static byte[] ToWav(float[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataSize = samples.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataSize);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);      // PCM
        w.Write((short)1);      // mono
        w.Write(Rate);
        w.Write(Rate * 2);      // byte rate
        w.Write((short)2);      // block align
        w.Write((short)16);     // bits per sample
        w.Write("data"u8);
        w.Write(dataSize);
        foreach (var s in samples)
            w.Write((short)(Math.Clamp(s, -1f, 1f) * short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
