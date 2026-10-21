using System.Windows;
using System.Windows.Media;

namespace TypeStrike.Game;

public enum Difficulty { Easy, Normal, Hard }

public enum Phase
{
    /// <summary>Меню: летят только звёзды.</summary>
    Idle,
    /// <summary>Пауза между волнами.</summary>
    Intermission,
    Fighting,
    /// <summary>Корабль взорван, ждём экран итогов.</summary>
    Dying,
    Over,
}

public enum Sfx { Shoot, Hit, Explode, BigExplode, Miss, Emp, WaveClear, ShipHit, GameOver }

public sealed record GameResult(int Score, int Wave, double Wpm, double Accuracy, int MaxCombo, int Words, string TroubleLetters);

/// <summary>
/// Вся игровая логика: волны, враги, прицеливание по первой букве, пули, ЭМИ, очки.
/// Ничего не знает о WPF-контролах — только о координатах игрового поля.
/// </summary>
public sealed class GameEngine
{
    public const double ShipRadius = 18;
    public const double EmpRadius = 330;
    const double BulletSpeed = 1500;
    const int MaxParticles = 900;
    const int MaxEnemiesOnScreen = 14;

    static readonly Color CyanColor = Color.FromRgb(0x5C, 0xE1, 0xFF);
    static readonly Color GreenColor = Color.FromRgb(0x6B, 0xFF, 0xB0);
    static readonly Color OrangeColor = Color.FromRgb(0xFF, 0x9F, 0x1C);
    static readonly Color PinkColor = Color.FromRgb(0xFF, 0x3D, 0x81);

    readonly Random _rng = new();
    readonly Queue<(EnemyKind Kind, double Delay)> _spawnQueue = new();
    readonly Dictionary<char, int> _missedLetters = new();
    double _spawnTimer;
    double _phaseTimer;
    int _waveMisses;
    Enemy? _aim;
    WordBank _words = WordBank.English;

    public GameEngine()
    {
        Stars = new Star[170];
        for (int i = 0; i < Stars.Length; i++)
        {
            double depth = _rng.NextDouble();
            Stars[i] = new Star
            {
                X = _rng.NextDouble(),
                Y = _rng.NextDouble(),
                Speed = 0.008 + depth * depth * 0.07,
                Size = 1 + depth * 1.8,
                Brightness = 0.25 + depth * 0.75,
            };
        }
    }

    public List<Enemy> Enemies { get; } = new();
    public List<Bullet> Bullets { get; } = new();
    public List<Particle> Particles { get; } = new();
    public List<Ring> Rings { get; } = new();
    public Star[] Stars { get; }

    public Size Field { get; private set; } = new(1200, 800);
    public Point Ship => new(Field.Width / 2, Field.Height - 64);
    public double ShipAngle { get; private set; }
    public Enemy? Target { get; private set; }
    public Phase Phase { get; private set; } = Phase.Idle;
    public Difficulty Difficulty { get; private set; } = Difficulty.Normal;
    public WordBank Words => _words;

    public int Score { get; private set; }
    public int Wave { get; private set; }
    public int Combo { get; private set; }
    public int MaxCombo { get; private set; }
    public int Shields { get; private set; }
    public int MaxShields { get; private set; }
    public int Emps { get; private set; }
    public int Keystrokes { get; private set; }
    public int Correct { get; private set; }
    public int Misses { get; private set; }
    public int WordsDestroyed { get; private set; }
    public double ActiveTime { get; private set; }
    public double Time { get; private set; }
    public double Shake { get; private set; }
    public double MissFlash { get; private set; }
    public double Invulnerable { get; private set; }
    public Banner? Banner { get; private set; }
    public string? Hint { get; private set; }
    public double HintTime { get; private set; }

    public event Action<GameResult>? GameOver;
    public Action<Sfx>? PlaySound { get; set; }

    public int Multiplier => 1 + Math.Min(Combo / 20, 4);
    public double Wpm => ActiveTime < 1 ? 0 : Correct / 5.0 / (ActiveTime / 60.0);
    public double Accuracy => Keystrokes == 0 ? 100 : 100.0 * Correct / Keystrokes;
    public bool IsRunning => Phase is Phase.Intermission or Phase.Fighting or Phase.Dying;

    double SpeedMultiplier => Difficulty switch { Difficulty.Easy => 0.7, Difficulty.Hard => 1.3, _ => 1.0 };
    double IntervalMultiplier => Difficulty switch { Difficulty.Easy => 1.35, Difficulty.Hard => 0.8, _ => 1.0 };
    int EmpsPerWave => Difficulty == Difficulty.Easy ? 2 : 1;
    int StartShields => Difficulty switch { Difficulty.Easy => 3, Difficulty.Normal => 2, _ => 1 };
    double BaseSpeed => Math.Min(26 + Wave * 3.0, 85) * SpeedMultiplier;

    public void Resize(Size size)
    {
        if (size.Width > 0 && size.Height > 0)
            Field = size;
    }

    public void Reset()
    {
        Enemies.Clear();
        Bullets.Clear();
        Particles.Clear();
        Rings.Clear();
        _spawnQueue.Clear();
        _missedLetters.Clear();
        Target = null;
        _aim = null;
        Phase = Phase.Idle;
        Banner = null;
        Hint = null;
        Score = Wave = Combo = MaxCombo = Emps = Keystrokes = Correct = Misses = WordsDestroyed = 0;
        ActiveTime = Shake = MissFlash = Invulnerable = 0;
        ShipAngle = 0;
    }

    public void Start(WordBank words, Difficulty difficulty)
    {
        Reset();
        _words = words;
        Difficulty = difficulty;
        Shields = MaxShields = StartShields;
        Phase = Phase.Intermission;
        _phaseTimer = 1.8;
        ShowBanner("ПРИГОТОВЬТЕСЬ", "первая буква слова захватывает цель", 1.8, CyanColor);
    }

    // ───────────────────────────── Ввод ─────────────────────────────

    public void OnChar(char raw)
    {
        if (Phase != Phase.Fighting)
            return;

        char c = TextUtil.Norm(raw);
        if (!_words.Alphabet.Contains(c))
        {
            // Скорее всего, не та раскладка — не наказываем, а подсказываем
            ShowHint("Похоже, не та раскладка клавиатуры");
            return;
        }

        Keystrokes++;

        if (Target is { CanBeTargeted: false })
            Target = null;

        if (Target == null)
        {
            var ship = Ship;
            Target = Enemies
                .Where(e => e.CanBeTargeted && e.Pos.Y > -e.Radius * 0.5 && TextUtil.Norm(e.Word[e.Typed]) == c)
                .OrderBy(e => (e.Pos - ship).LengthSquared)
                .FirstOrDefault();

            if (Target == null)
            {
                RegisterMiss(null);
                return;
            }
        }

        char expected = TextUtil.Norm(Target.Word[Target.Typed]);
        if (expected != c)
        {
            RegisterMiss(expected);
            return;
        }

        Correct++;
        Combo++;
        MaxCombo = Math.Max(MaxCombo, Combo);
        Score += 10 * Multiplier;
        Fire(Target);
        Target.Typed++;

        if (Target.Typed >= Target.Word.Length)
            Target = null;
    }

    /// <summary>Backspace: отпустить текущую цель и выбрать другую.</summary>
    public void ReleaseTarget()
    {
        if (Phase == Phase.Fighting)
            Target = null;
    }

    /// <summary>Enter: ЭМИ-импульс отбрасывает врагов и уничтожает мини-корабли рядом.</summary>
    public bool TriggerEmp()
    {
        if (Phase != Phase.Fighting || Emps <= 0)
            return false;

        Emps--;
        var ship = Ship;
        Rings.Add(new Ring { Center = ship, StartRadius = 10, EndRadius = EmpRadius, Life = 0.6, MaxLife = 0.6, Thickness = 6, Color = CyanColor });
        Rings.Add(new Ring { Center = ship, StartRadius = 5, EndRadius = EmpRadius * 0.7, Life = 0.45, MaxLife = 0.45, Thickness = 3, Color = Colors.White });

        foreach (var e in Enemies)
        {
            if (!e.IsAlive || (e.Pos - ship).Length >= EmpRadius)
                continue;
            if (e.Kind == EnemyKind.Mite)
                Destroy(e, byPlayer: true);
        }
        Repel(ship, EmpRadius, 650);

        Shake = Math.Max(Shake, 8);
        PlaySound?.Invoke(Sfx.Emp);
        return true;
    }

    void RegisterMiss(char? expected)
    {
        Misses++;
        _waveMisses++;
        Combo = 0;
        MissFlash = 0.35;
        if (expected is char ch)
            _missedLetters[ch] = _missedLetters.GetValueOrDefault(ch) + 1;
        PlaySound?.Invoke(Sfx.Miss);
    }

    void Fire(Enemy target)
    {
        _aim = target;
        var ship = Ship;
        var dir = target.Pos - ship;
        if (dir.LengthSquared < 0.01)
            dir = new Vector(0, -1);
        dir.Normalize();
        var muzzle = ship + dir * 24;
        Bullets.Add(new Bullet { Pos = muzzle, Dir = dir, Target = target });

        for (int i = 0; i < 3; i++)
            AddParticle(muzzle, Rotate(dir, (_rng.NextDouble() - 0.5) * 0.9) * (120 + _rng.NextDouble() * 120), 0.15, 2, OrangeColor);

        PlaySound?.Invoke(Sfx.Shoot);
    }

    // ───────────────────────────── Цикл ─────────────────────────────

    public void Update(double dt)
    {
        Time += dt;
        UpdateStars(dt);
        Shake = Math.Max(0, Shake - dt * 40);
        MissFlash = Math.Max(0, MissFlash - dt * 2.5);
        Invulnerable = Math.Max(0, Invulnerable - dt);

        if (Banner != null)
        {
            Banner.Time += dt;
            if (Banner.Time >= Banner.Duration)
                Banner = null;
        }
        if (Hint != null)
        {
            HintTime -= dt;
            if (HintTime <= 0)
                Hint = null;
        }

        switch (Phase)
        {
            case Phase.Intermission:
                _phaseTimer -= dt;
                if (_phaseTimer <= 0)
                    StartWave();
                break;
            case Phase.Fighting:
                ActiveTime += dt;
                UpdateSpawning(dt);
                break;
            case Phase.Dying:
                _phaseTimer -= dt;
                if (_phaseTimer <= 0)
                {
                    Phase = Phase.Over;
                    GameOver?.Invoke(BuildResult());
                }
                break;
        }

        UpdateEnemies(dt);
        UpdateBullets(dt);
        UpdateParticles(dt);
        UpdateRings(dt);

        Enemies.RemoveAll(e => !e.IsAlive);
        if (Target is { CanBeTargeted: false })
            Target = null;

        if (Phase == Phase.Fighting && _spawnQueue.Count == 0 && Enemies.Count == 0)
            WaveCleared();

        UpdateShipAngle(dt);
    }

    void UpdateStars(double dt)
    {
        double boost = Phase == Phase.Intermission ? 2.5 : 1;
        foreach (var s in Stars)
        {
            s.Y += s.Speed * dt * boost;
            if (s.Y > 1)
            {
                s.Y -= 1;
                s.X = _rng.NextDouble();
            }
        }
    }

    void UpdateShipAngle(double dt)
    {
        double desired = 0;
        var aim = Target ?? (_aim is { IsAlive: true } ? _aim : null);
        if (aim != null)
        {
            var d = aim.Pos - Ship;
            desired = Math.Atan2(d.X, -d.Y) * 180 / Math.PI;
        }
        ShipAngle += (desired - ShipAngle) * Math.Min(1, dt * 14);
    }

    void UpdateSpawning(double dt)
    {
        if (_spawnQueue.Count == 0)
            return;

        _spawnTimer -= dt;
        if (_spawnTimer > 0)
            return;

        if (Enemies.Count >= MaxEnemiesOnScreen)
        {
            _spawnTimer = 0.3;
            return;
        }

        var (kind, _) = _spawnQueue.Dequeue();
        Spawn(kind);
        if (_spawnQueue.Count > 0)
            _spawnTimer = _spawnQueue.Peek().Delay;
    }

    void UpdateEnemies(double dt)
    {
        var ship = Ship;
        for (int i = 0; i < Enemies.Count; i++)
        {
            var e = Enemies[i];
            if (!e.IsAlive)
                continue;

            e.Age += dt;
            e.Flash = Math.Max(0, e.Flash - dt);

            var to = ship - e.Pos;
            double dist = to.Length;
            if (dist > 0.001)
                to /= dist;

            var v = to * e.Speed;
            if (e.Kind == EnemyKind.Dart)
                v += new Vector(-to.Y, to.X) * Math.Sin(e.Age * 2.6 + e.WobblePhase) * e.Speed * 0.55;
            else if (e.Kind is EnemyKind.Carrier or EnemyKind.Boss)
                v += new Vector(Math.Sin(e.Age * 0.7 + e.WobblePhase) * 25, 0);

            e.Pos += (v + e.Knock) * dt;
            e.Pos = new Point(Math.Clamp(e.Pos.X, e.Radius, Math.Max(e.Radius, Field.Width - e.Radius)), e.Pos.Y);
            e.Knock *= Math.Exp(-3.2 * dt);
            e.Heading = Math.Atan2(v.Y, v.X) * 180 / Math.PI;

            if (Phase != Phase.Fighting)
                continue;

            if (e.Kind is EnemyKind.Carrier or EnemyKind.Boss && e.Pos.Y > 20 && e.Pos.Y < Field.Height * 0.62)
            {
                e.SpawnCooldown -= dt;
                if (e.SpawnCooldown <= 0 && Enemies.Count(x => x.IsAlive && x.Kind == EnemyKind.Mite) < 8)
                {
                    int count = e.Kind == EnemyKind.Boss ? 2 : 1;
                    for (int k = 0; k < count; k++)
                        SpawnMite(e);
                    e.SpawnCooldown = e.Kind == EnemyKind.Boss ? 2.4 : Math.Max(2.0, 5.0 - Wave * 0.15);
                }
            }

            if (dist < ShipRadius + e.Radius * 0.75)
                HitShip(e);
        }
    }

    void UpdateBullets(double dt)
    {
        double step = BulletSpeed * dt;
        foreach (var b in Bullets)
        {
            if (!b.Target.IsAlive)
            {
                b.Done = true;
                continue;
            }
            var to = b.Target.Pos - b.Pos;
            double d = to.Length;
            if (d <= step + b.Target.Radius * 0.6)
            {
                b.Done = true;
                OnBulletHit(b.Target, d > 0.01 ? to / d : b.Dir);
            }
            else
            {
                b.Dir = to / d;
                b.Pos += b.Dir * step;
            }
        }
        Bullets.RemoveAll(b => b.Done);
    }

    void UpdateParticles(double dt)
    {
        double drag = Math.Exp(-2.2 * dt);
        foreach (var p in Particles)
        {
            p.Pos += p.Vel * dt;
            p.Vel *= drag;
            p.Life -= dt;
        }
        Particles.RemoveAll(p => p.Life <= 0);
    }

    void UpdateRings(double dt)
    {
        foreach (var r in Rings)
            r.Life -= dt;
        Rings.RemoveAll(r => r.Life <= 0);
    }

    // ───────────────────────────── Волны ─────────────────────────────

    void StartWave()
    {
        Wave++;
        _waveMisses = 0;
        Phase = Phase.Fighting;
        Emps = EmpsPerWave;
        Target = null;
        BuildWave();
        ShowBanner($"ВОЛНА {Wave}", Wave % 5 == 0 ? "приближается флагман!" : null, 1.8,
            Wave % 5 == 0 ? PinkColor : CyanColor);
    }

    void BuildWave()
    {
        _spawnQueue.Clear();
        var kinds = new List<EnemyKind>();
        int drones = Math.Min(3 + Wave * 2, 24);
        int darts = Wave >= 2 ? Math.Min(Wave - 1, 10) : 0;
        int carriers = Wave >= 3 ? Math.Min(1 + (Wave - 3) / 3, 4) : 0;
        kinds.AddRange(Enumerable.Repeat(EnemyKind.Drone, drones));
        kinds.AddRange(Enumerable.Repeat(EnemyKind.Dart, darts));
        kinds.AddRange(Enumerable.Repeat(EnemyKind.Carrier, carriers));

        for (int i = kinds.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
        }
        if (Wave % 5 == 0)
            kinds.Insert(kinds.Count / 2, EnemyKind.Boss);

        double interval = Math.Max(0.55, 2.1 - Wave * 0.1) * IntervalMultiplier;
        for (int i = 0; i < kinds.Count; i++)
        {
            double delay = i == 0 ? 0.4 : interval * (0.5 + _rng.NextDouble());
            if (kinds[i] == EnemyKind.Boss)
                delay += 2;
            _spawnQueue.Enqueue((kinds[i], delay));
        }
        _spawnTimer = _spawnQueue.Peek().Delay;
    }

    void WaveCleared()
    {
        Phase = Phase.Intermission;
        _phaseTimer = 3.0;

        string subtitle;
        if (_waveMisses == 0)
        {
            int bonus = 250 * Wave;
            Score += bonus;
            subtitle = $"без единой ошибки!  бонус +{bonus}";
        }
        else
        {
            subtitle = $"скорость {Wpm:0} WPM  ·  точность {Accuracy:0}%";
        }

        ShowBanner($"ВОЛНА {Wave} ПРОЙДЕНА", subtitle, 2.8, GreenColor);
        PlaySound?.Invoke(Sfx.WaveClear);
    }

    void Spawn(EnemyKind kind)
    {
        var (min, max, speedK) = kind switch
        {
            EnemyKind.Drone => (3 + Wave / 6, Math.Min(5 + Wave / 3, 11), 1.0),
            EnemyKind.Dart => (2, Math.Min(4 + Wave / 6, 6), 1.7),
            EnemyKind.Carrier => (7, 12, 0.5),
            EnemyKind.Boss => (11, 20, 0.3),
            _ => (2, 3, 2.0),
        };
        double radius = EnemyPalette.RadiusOf(kind);
        double margin = radius + 30;
        double x = margin + _rng.NextDouble() * Math.Max(1, Field.Width - 2 * margin);
        AddEnemy(kind, PickWord(min, max), new Point(x, -radius - 8), BaseSpeed * speedK, default);
    }

    void SpawnMite(Enemy parent)
    {
        double angle = (20 + _rng.NextDouble() * 140) * Math.PI / 180;
        var push = new Vector(Math.Cos(angle), Math.Sin(angle)) * 150;
        AddEnemy(EnemyKind.Mite, PickWord(2, 3), parent.Pos, BaseSpeed * 2.0, push);
    }

    void AddEnemy(EnemyKind kind, string word, Point pos, double speed, Vector knock)
    {
        Enemies.Add(new Enemy
        {
            Word = word,
            Kind = kind,
            Pos = pos,
            Knock = knock,
            Speed = speed * (0.9 + _rng.NextDouble() * 0.2),
            Radius = EnemyPalette.RadiusOf(kind),
            Color = EnemyPalette.ColorOf(kind),
            WobblePhase = _rng.NextDouble() * Math.PI * 2,
            SpawnCooldown = 1.5,
        });
    }

    string PickWord(int min, int max)
    {
        var letters = new HashSet<char>();
        var words = new HashSet<string>();
        foreach (var e in Enemies)
        {
            if (!e.CanBeTargeted)
                continue;
            letters.Add(TextUtil.Norm(e.Word[e.Typed]));
            words.Add(e.Word);
        }
        return _words.Pick(min, max, letters, words, _rng);
    }

    // ───────────────────────────── Попадания ─────────────────────────────

    void OnBulletHit(Enemy e, Vector dir)
    {
        e.Hits++;
        e.Flash = 0.08;
        double knockback = e.Kind switch
        {
            EnemyKind.Drone => 22,
            EnemyKind.Dart => 26,
            EnemyKind.Carrier => 9,
            EnemyKind.Mite => 30,
            EnemyKind.Boss => 3.5,
            _ => 20,
        };
        e.Knock += dir * knockback;

        var at = e.Pos - dir * e.Radius * 0.6;
        for (int i = 0; i < 5; i++)
            AddParticle(at, Rotate(dir, (_rng.NextDouble() - 0.5) * 1.6) * (80 + _rng.NextDouble() * 160),
                0.25 + _rng.NextDouble() * 0.2, 2, i % 2 == 0 ? e.Color : OrangeColor);

        if (e.Hits >= e.Word.Length)
            Destroy(e, byPlayer: true);
        else
            PlaySound?.Invoke(Sfx.Hit);
    }

    void Destroy(Enemy e, bool byPlayer)
    {
        if (!e.IsAlive)
            return;
        e.IsAlive = false;
        if (Target == e)
            Target = null;

        var (count, power) = e.Kind switch
        {
            EnemyKind.Boss => (140, 2.0),
            EnemyKind.Carrier => (70, 1.4),
            EnemyKind.Drone => (36, 1.0),
            EnemyKind.Dart => (28, 0.9),
            _ => (16, 0.7),
        };
        Explosion(e.Pos, e.Color, count, power);
        Rings.Add(new Ring { Center = e.Pos, StartRadius = e.Radius * 0.5, EndRadius = e.Radius * 3.5, Life = 0.45, MaxLife = 0.45, Thickness = 3, Color = e.Color });

        if (byPlayer)
        {
            WordsDestroyed++;
            Score += e.Word.Length * 5 * Multiplier + e.Kind switch
            {
                EnemyKind.Boss => 1000,
                EnemyKind.Carrier => 150,
                _ => 0,
            };
        }

        Shake = Math.Max(Shake, e.Kind switch { EnemyKind.Boss => 18, EnemyKind.Carrier => 9, _ => 4 });
        PlaySound?.Invoke(e.Kind is EnemyKind.Boss or EnemyKind.Carrier ? Sfx.BigExplode : Sfx.Explode);
    }

    void HitShip(Enemy e)
    {
        Destroy(e, byPlayer: false);
        if (Invulnerable > 0 || Phase != Phase.Fighting)
            return;

        Shields--;
        Combo = 0;
        Shake = 22;
        MissFlash = 0.9;
        var ship = Ship;

        if (Shields <= 0)
        {
            Phase = Phase.Dying;
            _phaseTimer = 2.4;
            Target = null;
            Explosion(ship, CyanColor, 120, 1.8);
            Explosion(ship, OrangeColor, 80, 1.4);
            Rings.Add(new Ring { Center = ship, StartRadius = 10, EndRadius = 260, Life = 0.9, MaxLife = 0.9, Thickness = 8, Color = OrangeColor });
            PlaySound?.Invoke(Sfx.GameOver);
        }
        else
        {
            Invulnerable = 2.0;
            Repel(ship, 280, 520);
            Rings.Add(new Ring { Center = ship, StartRadius = 10, EndRadius = 280, Life = 0.6, MaxLife = 0.6, Thickness = 5, Color = GreenColor });
            ShowHint(Shields == 1 ? "Щиты исчерпаны — следующее столкновение последнее!" : "Щит потерян!");
            PlaySound?.Invoke(Sfx.ShipHit);
        }
    }

    void Repel(Point center, double radius, double force)
    {
        foreach (var e in Enemies)
        {
            if (!e.IsAlive)
                continue;
            var v = e.Pos - center;
            double d = v.Length;
            if (d >= radius || d < 0.001)
                continue;
            double weight = e.Kind switch { EnemyKind.Boss => 0.25, EnemyKind.Carrier => 0.5, _ => 1.0 };
            e.Knock += v / d * force * (1 - d / radius) * weight;
        }
    }

    // ───────────────────────────── Эффекты ─────────────────────────────

    void Explosion(Point at, Color color, int count, double power)
    {
        for (int i = 0; i < count; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2;
            double speed = 40 + _rng.NextDouble() * 220 * power;
            var c = _rng.NextDouble() switch
            {
                < 0.15 => Colors.White,
                < 0.35 => OrangeColor,
                _ => color,
            };
            AddParticle(at, new Vector(Math.Cos(a), Math.Sin(a)) * speed,
                0.4 + _rng.NextDouble() * 0.7, 1.5 + _rng.NextDouble() * 2.5, c);
        }
    }

    void AddParticle(Point pos, Vector vel, double life, double size, Color color)
    {
        if (Particles.Count >= MaxParticles)
            Particles.RemoveAt(0);
        Particles.Add(new Particle { Pos = pos, Vel = vel, Life = life, MaxLife = life, Size = size, Color = color });
    }

    void ShowBanner(string title, string? subtitle, double duration, Color color) =>
        Banner = new Banner { Title = title, Subtitle = subtitle, Duration = duration, Color = color };

    void ShowHint(string text)
    {
        Hint = text;
        HintTime = 2.0;
    }

    static Vector Rotate(Vector v, double radians)
    {
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        return new Vector(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    GameResult BuildResult()
    {
        var trouble = string.Join("   ", _missedLetters
            .OrderByDescending(kv => kv.Value)
            .Take(6)
            .Select(kv => $"{kv.Key} ×{kv.Value}"));
        return new GameResult(Score, Wave, Wpm, Accuracy, MaxCombo, WordsDestroyed, trouble);
    }
}
