using Gladi_Bersih.Common;
using MySqlConnector;

namespace Gladi_Bersih.Middleware;

public sealed class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException exception)
        {
            await WriteErrorAsync(context, exception.StatusCode, exception.Message);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await WriteErrorAsync(context, StatusCodes.Status409Conflict, "Data duplikat tidak dapat disimpan.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Terjadi kesalahan yang tidak ditangani.");
            await WriteErrorAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Terjadi kesalahan pada server.");
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string message)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            ApiResponseFactory.Error(message),
            context.RequestAborted);
    }
}
