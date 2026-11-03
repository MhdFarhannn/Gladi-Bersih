using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;
using Gladi_Bersih.Extensions;
using Gladi_Bersih.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gladi_Bersih.Controllers;

[ApiController]
[Route("api/peminjaman")]
[Authorize]
public sealed class PeminjamanController : ControllerBase
{
    private readonly IPeminjamanService _service;

    public PeminjamanController(IPeminjamanService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetAllAsync(
            User.GetUserId(),
            User.GetRole(),
            page,
            pageSize,
            search,
            status,
            cancellationToken);
        return Ok(ApiResponseFactory.Success("Daftar peminjaman berhasil diambil.", result));
    }

    [HttpGet("{id:long:min(1)}")]
    public async Task<IActionResult> GetById(uint id, CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(
            User.GetUserId(),
            User.GetRole(),
            id,
            cancellationToken);
        return Ok(ApiResponseFactory.Success("Detail peminjaman berhasil diambil.", result));
    }

    [Authorize(Roles = "Peminjam,Admin")]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] PeminjamanRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(
            User.GetUserId(),
            User.GetRole(),
            request,
            cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            ApiResponseFactory.Success("Peminjaman berhasil diajukan.", result));
    }

    [Authorize(Roles = "Peminjam,Admin")]
    [HttpPut("{id:long:min(1)}")]
    public async Task<IActionResult> Update(
        uint id,
        [FromBody] PeminjamanRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(
            User.GetUserId(),
            User.GetRole(),
            id,
            request,
            cancellationToken);
        return Ok(ApiResponseFactory.Success("Peminjaman berhasil diperbarui.", result));
    }

    [Authorize(Roles = "Petugas")]
    [HttpPost("{id:long:min(1)}/setujui")]
    public async Task<IActionResult> Approve(uint id, CancellationToken cancellationToken)
    {
        var result = await _service.ApproveAsync(User.GetUserId(), id, cancellationToken);
        return Ok(ApiResponseFactory.Success("Peminjaman berhasil disetujui.", result));
    }

    [Authorize(Roles = "Petugas")]
    [HttpPost("{id:long:min(1)}/tolak")]
    public async Task<IActionResult> Reject(
        uint id,
        [FromBody] PenolakanPeminjamanRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.RejectAsync(
            User.GetUserId(),
            id,
            request.Alasan,
            cancellationToken);
        return Ok(ApiResponseFactory.Success("Peminjaman berhasil ditolak.", result));
    }

    [Authorize(Roles = "Petugas")]
    [HttpPost("{id:long:min(1)}/serahkan")]
    public async Task<IActionResult> HandOver(uint id, CancellationToken cancellationToken)
    {
        var result = await _service.HandOverAsync(User.GetUserId(), id, cancellationToken);
        return Ok(ApiResponseFactory.Success("Unit alat berhasil diserahkan.", result));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:long:min(1)}")]
    public async Task<IActionResult> Delete(uint id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(User.GetUserId(), id, cancellationToken);
        return Ok(ApiResponseFactory.Success("Peminjaman berhasil dihapus."));
    }
}
