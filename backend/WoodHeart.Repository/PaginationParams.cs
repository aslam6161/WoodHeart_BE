namespace WoodHeart.Repository;

/// <summary>
/// Base query parameters for any paged endpoint.
/// </summary>
/// <remarks>
/// <see cref="PageSize"/> is capped rather than trusted. Without the cap,
/// <c>?pageSize=100000</c> is a one-line denial of service against the product
/// catalog — and it will be found, because crawlers try it.
/// </remarks>
public class PaginationParams
{
    private const int MaxPageSize = 100;

    private int _pageSize = 20;

    private int _pageNumber = 1;

    public int PageNumber
    {
        get => _pageNumber;
        set => _pageNumber = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => 1,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }
}
