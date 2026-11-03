using Gladi_Bersih.Common;
using Gladi_Bersih.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gladi_Bersih.Controllers;

[ApiController]
[Route("api/log-aktivitas")]
[Authorize(Roles = "Admin")]
public sealed class LogAktivitasController : ControllerBase
{
    private readonly ILogAktivitasService _service;

    public LogAktivitasController(ILogAktivitasService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetAllAsync(page, pageSize, search, from, to, cancellationToken);
        return Ok(ApiResponseFactory.Success("Log aktivitas berhasil diambil.", result));
    }
}
