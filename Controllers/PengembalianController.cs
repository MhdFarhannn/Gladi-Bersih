using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;
using Gladi_Bersih.Extensions;
using Gladi_Bersih.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gladi_Bersih.Controllers;

[ApiController]
[Route("api/pengembalian")]
[Authorize]
public sealed class PengembalianController : ControllerBase
{
    private readonly IPengembalianService _service;

    public PengembalianController(IPengembalianService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetAllAsync(
            User.GetUserId(),
            User.GetRole(),
            page,
            pageSize,
            search,
            cancellationToken);
        return Ok(ApiResponseFactory.Success("Daftar pengembalian berhasil diambil.", result));
    }

    [HttpGet("{id:long:min(1)}")]
    public async Task<IActionResult> GetById(uint id, CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(
            User.GetUserId(),
            User.GetRole(),
            id,
            cancellationToken);
        return Ok(ApiResponseFactory.Success("Detail pengembalian berhasil diambil.", result));
    }

    [Authorize(Roles = "Peminjam,Petugas,Admin")]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] PengembalianRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(
            User.GetUserId(),
            User.GetRole(),
            request,
            cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            ApiResponseFactory.Success("Pengembalian berhasil dicatat.", result));
    }

    [Authorize(Roles = "Petugas,Admin")]
    [HttpPut("{id:long:min(1)}/verifikasi")]
    public async Task<IActionResult> Verify(
        uint id,
        [FromBody] VerifikasiPengembalianRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.VerifyAsync(User.GetUserId(), id, request, cancellationToken);
        return Ok(ApiResponseFactory.Success("Pengembalian berhasil diverifikasi.", result));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:long:min(1)}")]
    public async Task<IActionResult> Delete(uint id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(User.GetUserId(), id, cancellationToken);
        return Ok(ApiResponseFactory.Success("Pengembalian berhasil dihapus."));
    }
}
