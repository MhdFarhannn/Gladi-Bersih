namespace Gladi_Bersih.Common;

public sealed record ApiResponse<T>(bool Success, string Message, T? Data);

public static class ApiResponseFactory
{
    public static ApiResponse<T> Success<T>(string message, T data)
    {
        return new ApiResponse<T>(true, message, data);
    }

    public static ApiResponse<object?> Success(string message)
    {
        return new ApiResponse<object?>(true, message, null);
    }

    public static ApiResponse<object?> Error(string message)
    {
        return new ApiResponse<object?>(false, message, null);
    }
}

public sealed record PagedResult<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, long Total);
