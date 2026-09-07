using EnergyBalance.Domain.Enums;

namespace EnergyBalance.Domain.Entities;

/// <summary>
/// Контрольный (мгновенный) срез показаний прибора учёта — накопительные счётчики энергии
/// по тарифным зонам плюс текущие параметры качества сети на момент опроса.
/// </summary>
public class Reading
{
    public long Id { get; set; }

    public int MeterId { get; set; }
    public Meter? Meter { get; set; }

    /// <summary>Момент времени снятия показания (UTC).</summary>
    public DateTime TimestampUtc { get; set; }

    /// <summary>Активная энергия «приём» (А+), накопительный счёт по тарифу Т1, кВт·ч (в первичных величинах, до умножения на Ктт/Ктн).</summary>
    public decimal ActiveEnergyTariff1 { get; set; }
    public decimal ActiveEnergyTariff2 { get; set; }
    public decimal ActiveEnergyTariff3 { get; set; }
    public decimal ActiveEnergyTariff4 { get; set; }

    /// <summary>
    /// Активная энергия «выдача» (А-), накопительный счёт по тарифу Т1, кВт·ч — обязательна для
    /// точек с возможным двусторонним перетоком (границы с другой сетевой организацией, точки
    /// с распределённой генерацией). Подтверждено интерфейсом ПО «Пирамида 2.0» (параметр
    /// «Энергия A-» наравне с «Энергия A+» в разделах «Электроэнергия»/«Профиль»/«Аналитика»).
    /// Для точек с однонаправленным потреблением равна нулю.
    /// </summary>
    public decimal ActiveEnergyExportTariff1 { get; set; }
    public decimal ActiveEnergyExportTariff2 { get; set; }
    public decimal ActiveEnergyExportTariff3 { get; set; }
    public decimal ActiveEnergyExportTariff4 { get; set; }

    /// <summary>Реактивная энергия «приём» (Р+), накопительный счёт, кВАр·ч.</summary>
    public decimal ReactiveEnergyImport { get; set; }

    /// <summary>Реактивная энергия «выдача» (Р-), накопительный счёт, кВАр·ч.</summary>
    public decimal ReactiveEnergyExport { get; set; }

    /// <summary>Суммарная активная энергия «приём» по всем тарифам.</summary>
    public decimal ActiveEnergyTotal =>
        ActiveEnergyTariff1 + ActiveEnergyTariff2 + ActiveEnergyTariff3 + ActiveEnergyTariff4;

    /// <summary>Суммарная активная энергия «выдача» по всем тарифам.</summary>
    public decimal ActiveEnergyExportTotal =>
        ActiveEnergyExportTariff1 + ActiveEnergyExportTariff2 + ActiveEnergyExportTariff3 + ActiveEnergyExportTariff4;

    /// <summary>Мгновенные значения напряжения по фазам, В.</summary>
    public decimal? VoltageA { get; set; }
    public decimal? VoltageB { get; set; }
    public decimal? VoltageC { get; set; }

    /// <summary>Мгновенные значения тока по фазам, А.</summary>
    public decimal? CurrentA { get; set; }
    public decimal? CurrentB { get; set; }
    public decimal? CurrentC { get; set; }

    /// <summary>Активная мощность, кВт.</summary>
    public decimal? ActivePower { get; set; }

    /// <summary>Коэффициент мощности cos φ.</summary>
    public decimal? PowerFactor { get; set; }

    public ReadingQuality Quality { get; set; } = ReadingQuality.Good;

    /// <summary>Источник данных, откуда получено показание (для сверки каналов).</summary>
    public MeterProtocol Source { get; set; }

    /// <summary>Признак вмешательства/вскрытия корпуса или клеммной крышки, если прибор его выдаёт.</summary>
    public bool TamperFlag { get; set; }
}
