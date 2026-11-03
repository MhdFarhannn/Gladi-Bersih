using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Gladi_Bersih.DTOs;

public sealed class AlatRequest
{
    [FromForm(Name = "idKategori")]
    [Range(1, uint.MaxValue, ErrorMessage = "Kategori wajib dipilih.")]
    public uint IdKategori { get; init; }

    [FromForm(Name = "nama")]
    [Required(ErrorMessage = "Nama alat wajib diisi.")]
    [MaxLength(150, ErrorMessage = "Nama alat maksimal 150 karakter.")]
    public string Nama { get; init; } = string.Empty;

    [FromForm(Name = "deskripsi")]
    public string? Deskripsi { get; init; }

    [FromForm(Name = "hargaSewa")]
    [Range(typeof(decimal), "0", "9999999999999.99", ErrorMessage = "Harga sewa tidak valid.")]
    public decimal HargaSewa { get; init; }

    [FromForm(Name = "image")]
    public IFormFile? Image { get; init; }
}

public sealed record AlatResponse(
    uint Id,
    uint IdKategori,
    string Kategori,
    string Nama,
    string? Deskripsi,
    decimal HargaSewa,
    string? ImagePath,
    int StokTersedia,
    DateTime CreatedAt);
