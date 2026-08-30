using System.Text.Json;

namespace WoodHeart.Presentation.Extensions;

/// <summary>The paging metadata, sent as a header so the body stays a clean array.</summary>
public class PaginationHeader(int currentPage, int itemsPerPage, int totalItems, int totalPages)
{
    public int CurrentPage { get; } = currentPage;

    public int ItemsPerPage { get; } = itemsPerPage;

    public int TotalItems { get; } = totalItems;

    public int TotalPages { get; } = totalPages;
}

public static class HttpExtensions
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void AddPaginationHeader(
        this HttpResponse response, int currentPage, int itemsPerPage, int totalItems, int totalPages)
    {
        var header = new PaginationHeader(currentPage, itemsPerPage, totalItems, totalPages);

        response.Headers["X-Pagination"] = JsonSerializer.Serialize(header, SerializerOptions);

        // Without this the browser hides the header from Angular on any
        // cross-origin request, and the pager silently shows one page.
        response.Headers["Access-Control-Expose-Headers"] = "X-Pagination";
    }
}
