using Gladi_Bersih.Common;
using Gladi_Bersih.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gladi_Bersih.Controllers;

[ApiController]
[Route("api/laporan")]
[Authorize(Roles = "Petugas,Admin")]
public sealed class LaporanController : ControllerBase
{
    private readonly ILaporanService _service;

    public LaporanController(ILaporanService service)
    {
        _service = service;
    }

    [HttpGet("peminjaman")]
    public async Task<IActionResult> GetPeminjaman(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetPeminjamanAsync(from, to, cancellationToken);
        return Ok(ApiResponseFactory.Success("Laporan peminjaman berhasil dibuat.", result));
    }

    [HttpGet("peminjaman/csv")]
    public async Task<IActionResult> DownloadPeminjaman(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        var report = await _service.GetPeminjamanAsync(from, to, cancellationToken);
        var content = _service.CreatePeminjamanCsv(report);
        return File(content, "text/csv; charset=utf-8", $"laporan-peminjaman-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }

    [HttpGet("pengembalian")]
    public async Task<IActionResult> GetPengembalian(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetPengembalianAsync(from, to, cancellationToken);
        return Ok(ApiResponseFactory.Success("Laporan pengembalian berhasil dibuat.", result));
    }

    [HttpGet("pengembalian/csv")]
    public async Task<IActionResult> DownloadPengembalian(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        var report = await _service.GetPengembalianAsync(from, to, cancellationToken);
        var content = _service.CreatePengembalianCsv(report);
        return File(content, "text/csv; charset=utf-8", $"laporan-pengembalian-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }
}
