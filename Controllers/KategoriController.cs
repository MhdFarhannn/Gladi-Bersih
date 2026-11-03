using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;
using Gladi_Bersih.Extensions;
using Gladi_Bersih.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gladi_Bersih.Controllers;

[ApiController]
[Route("api/kategori")]
[Authorize]
public sealed class KategoriController : ControllerBase
{
    private readonly IKategoriService _service;

    public KategoriController(IKategoriService service)
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
        var result = await _service.GetAllAsync(page, pageSize, search, cancellationToken);
        return Ok(ApiResponseFactory.Success("Daftar kategori berhasil diambil.", result));
    }

    [HttpGet("{id:long:min(1)}")]
    public async Task<IActionResult> GetById(uint id, CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success("Detail kategori berhasil diambil.", result));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] KategoriRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(User.GetUserId(), request, cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            ApiResponseFactory.Success("Kategori berhasil dibuat.", result));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:long:min(1)}")]
    public async Task<IActionResult> Update(
        uint id,
        [FromBody] KategoriRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(User.GetUserId(), id, request, cancellationToken);
        return Ok(ApiResponseFactory.Success("Kategori berhasil diperbarui.", result));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:long:min(1)}")]
    public async Task<IActionResult> Delete(uint id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(User.GetUserId(), id, cancellationToken);
        return Ok(ApiResponseFactory.Success("Kategori berhasil dihapus."));
    }
}
