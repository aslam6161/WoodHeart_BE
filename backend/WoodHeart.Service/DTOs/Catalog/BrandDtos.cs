using System.ComponentModel.DataAnnotations;

namespace WoodHeart.Service.DTOs.Catalog;

public class BrandDto
{
    public long Id { get; set; }

    public string NameEn { get; set; } = string.Empty;

    public string? NameBn { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string? DescriptionEn { get; set; }

    public string? DescriptionBn { get; set; }

    public string? LogoPath { get; set; }

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    public int ProductCount { get; set; }
}

public class CreateBrandDto
{
    [Required(ErrorMessage = "An English name is required.")]
    [StringLength(200, MinimumLength = 1)]
    public string NameEn { get; set; } = string.Empty;

    [StringLength(200)]
    public string? NameBn { get; set; }

    /// <summary>Leave empty to derive from the name.</summary>
    [StringLength(160)]
    public string? Slug { get; set; }

    [StringLength(2000)]
    public string? DescriptionEn { get; set; }

    [StringLength(2000)]
    public string? DescriptionBn { get; set; }

    [StringLength(512)]
    public string? LogoPath { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
}

public class UpdateBrandDto : CreateBrandDto;
