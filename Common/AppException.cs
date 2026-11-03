namespace Gladi_Bersih.Common;

public sealed class AppException : Exception
{
    public AppException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
