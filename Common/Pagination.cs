namespace Gladi_Bersih.Common;

public sealed record Pagination(int Page, int PageSize, int Offset)
{
    public static Pagination Create(int page, int pageSize)
    {
        if (page < 1)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Page minimal 1.");
        }

        if (pageSize is < 1 or > 100)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Page size harus antara 1 dan 100.");
        }

        return new Pagination(page, pageSize, (page - 1) * pageSize);
    }
}
