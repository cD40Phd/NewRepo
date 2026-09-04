namespace EnergyBalance.Domain.Entities;

/// <summary>Подстанция или иной узловой объект сети.</summary>
public class Substation
{
    public int Id { get; set; }

    /// <summary>Наименование подстанции (например, «ПС Северная 110/10»).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Код подстанции по учётной системе заказчика.</summary>
    public string? Code { get; set; }

    /// <summary>Класс напряжения верхней стороны, кВ.</summary>
    public decimal? VoltageClassKv { get; set; }

    public List<Feeder> Feeders { get; set; } = new();
}
