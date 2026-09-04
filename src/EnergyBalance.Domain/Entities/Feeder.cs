namespace EnergyBalance.Domain.Entities;

/// <summary>Отходящий фидер (линия) подстанции.</summary>
public class Feeder
{
    public int Id { get; set; }

    public int SubstationId { get; set; }
    public Substation? Substation { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }

    public List<MeteringPoint> MeteringPoints { get; set; } = new();
}
