namespace Gladi_Bersih.Models;

public sealed class UnitAlatModel
{
    public uint Id { get; init; }
    public uint IdAlat { get; init; }
    public string Alat { get; init; } = string.Empty;
    public string KodeInventaris { get; init; } = string.Empty;
    public string StatusKetersediaan { get; init; } = string.Empty;
    public string StatusBarang { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
