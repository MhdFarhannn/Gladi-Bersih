namespace Gladi_Bersih.DTOs;

public sealed record LogAktivitasResponse(
    uint Id,
    uint? IdUser,
    string? Username,
    string? NamaUser,
    string Aktivitas,
    DateTime Waktu);
