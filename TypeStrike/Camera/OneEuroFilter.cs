namespace TypeStrike.Camera;

/// <summary>
/// Фильтр One Euro (Casiez и др., 2012): адаптивный фильтр нижних частот.
/// Когда сигнал почти не меняется (взгляд стоит), частота среза низкая — дрожание гасится.
/// Когда сигнал меняется быстро (взгляд переводят), частота среза растёт — задержки почти нет.
/// </summary>
public sealed class OneEuroFilter
{
    readonly double _minCutoff;
    readonly double _beta;
    readonly double _derivativeCutoff;
    double[]? _value;
    double[]? _derivative;

    /// <param name="minCutoff">Частота среза в покое, Гц (меньше — сильнее сглаживание).</param>
    /// <param name="beta">Насколько быстро растёт частота среза при движении (больше — меньше задержка).
    /// Значения по умолчанию подобраны на 30 к/с: скачок взгляда догоняется за ~55 мс
    /// (у прежнего сглаживания 50/50 — ~100 мс), а дрожание в покое на треть меньше.</param>
    public OneEuroFilter(double minCutoff = 0.5, double beta = 10.0, double derivativeCutoff = 1.0)
    {
        _minCutoff = minCutoff;
        _beta = beta;
        _derivativeCutoff = derivativeCutoff;
    }

    public void Reset()
    {
        _value = null;
        _derivative = null;
    }

    public double[] Filter(double[] x, double dt)
    {
        if (_value == null || _derivative == null || _value.Length != x.Length || dt <= 0)
        {
            _value = (double[])x.Clone();
            _derivative = new double[x.Length];
            return (double[])_value.Clone();
        }

        double aDerivative = Alpha(_derivativeCutoff, dt);
        for (int i = 0; i < x.Length; i++)
        {
            double rawDerivative = (x[i] - _value[i]) / dt;
            _derivative[i] += aDerivative * (rawDerivative - _derivative[i]);
            double cutoff = _minCutoff + _beta * Math.Abs(_derivative[i]);
            _value[i] += Alpha(cutoff, dt) * (x[i] - _value[i]);
        }
        return (double[])_value.Clone();
    }

    static double Alpha(double cutoff, double dt)
    {
        double tau = 1 / (2 * Math.PI * cutoff);
        return 1 / (1 + tau / dt);
    }
}
