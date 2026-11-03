using System.Data;

namespace Gladi_Bersih.Services;

public interface IActivityLogService
{
    Task WriteAsync(
        uint? userId,
        string activity,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);
}
