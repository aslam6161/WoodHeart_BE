using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Repository.Data;

/// <summary>
/// Sample catalog data: the category tree from PLAN.md §2, a few brands, and
/// enough products to render a storefront.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is demo data, not WoodHeart's real catalogue.</b> The category
/// structure is real — it comes straight from PLAN.md §2 and is what the
/// business actually sells. The products, prices and SKUs are plausible
/// placeholders so the storefront, the admin grid and the SSR pages have
/// something to render before anyone has typed in a real product. Every price
/// here is a guess and none of it should reach a customer.
/// </para>
/// <para>
/// <b>Gated separately from <see cref="Seed"/>.</b> Roles and settings belong
/// in every environment; demo furniture does not. This runs only when
/// <c>Seed:Catalog</c> is explicitly true, so production stays empty even
/// though production seeds roles on every start.
/// </para>
/// <para>
/// Idempotent by the same rule as the rest of the seeding: it checks for the
/// category tree and returns if anything is already there. It deliberately does
/// <b>not</b> try to merge — a half-seeded catalog someone has since edited is
/// not something a startup path should be reconciling.
/// </para>
/// </remarks>
public static class CatalogSeed
{
    public static async Task RunAsync(
        DataContext context,
        IDateTimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        if (await context.Categories.AnyAsync(cancellationToken))
        {
            return;
        }

        var categories = await SeedCategoriesAsync(context, cancellationToken);
        var brands = await SeedBrandsAsync(context, cancellationToken);

        await SeedProductsAsync(context, categories, brands, clock, cancellationToken);
        await SeedCollectionsAsync(context, cancellationToken);
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// The tree from PLAN.md §2 — six rooms, each with the items that sell in it.
    /// </summary>
    /// <remarks>
    /// Written in two passes because <c>MaterializedPath</c> contains the row's
    /// own id, and the database assigns it. Roots are saved first so their ids
    /// exist before children are built against them.
    /// </remarks>
    private static async Task<Dictionary<string, Category>> SeedCategoriesAsync(
        DataContext context, CancellationToken cancellationToken)
    {
        var tree = new (string NameEn, string NameBn, string Slug, (string En, string Bn, string Slug)[] Children)[]
        {
            ("Bedroom", "শয়নকক্ষ", "bedroom",
            [
                ("Beds", "বিছানা", "beds"),
                ("Wardrobes", "আলমারি", "wardrobes"),
                ("Dressing Tables", "ড্রেসিং টেবিল", "dressing-tables"),
                ("Side Tables", "সাইড টেবিল", "side-tables")
            ]),
            ("Dining", "খাবার ঘর", "dining",
            [
                ("Dining Tables", "ডাইনিং টেবিল", "dining-tables"),
                ("Dining Chairs", "ডাইনিং চেয়ার", "dining-chairs"),
                ("Dining Wagons", "ডাইনিং ওয়াগন", "dining-wagons")
            ]),
            ("Living", "বসার ঘর", "living",
            [
                ("Sofas", "সোফা", "sofas"),
                ("Centre Tables", "সেন্টার টেবিল", "centre-tables"),
                ("Showcases", "শোকেস", "showcases"),
                ("Mirrors", "আয়না", "mirrors")
            ]),
            ("Bath", "স্নানঘর", "bath",
            [
                ("Basin Cabinets", "বেসিন ক্যাবিনেট", "basin-cabinets"),
                ("Vanities", "ভ্যানিটি", "vanities"),
                ("Mirror Cabinets", "মিরর ক্যাবিনেট", "mirror-cabinets")
            ]),
            ("Lighting", "আলোকসজ্জা", "lighting",
            [
                ("Ceiling Lights", "সিলিং লাইট", "ceiling-lights"),
                ("Pendants", "পেন্ডেন্ট", "pendants"),
                ("Wall Lights", "ওয়াল লাইট", "wall-lights")
            ]),
            ("Decor", "সাজসজ্জা", "decor",
            [
                ("Wall Art", "ওয়াল আর্ট", "wall-art"),
                ("Handicrafts", "হস্তশিল্প", "handicrafts"),
                ("Planters", "প্ল্যান্টার", "planters")
            ])
        };

        var roots = tree.Select((node, index) => new Category
        {
            Name = LocalizedText.Create(node.NameEn, node.NameBn),
            Slug = Slug.From(node.Slug),
            SortOrder = index,
            Depth = 0,
            IsActive = true,
            IsFeatured = true
        }).ToList();

        context.Categories.AddRange(roots);
        await context.SaveChangesAsync(cancellationToken);

        foreach (var root in roots)
        {
            root.MaterializedPath = root.BuildPath(null);
        }

        var children = new List<Category>();

        for (var i = 0; i < tree.Length; i++)
        {
            var parent = roots[i];

            children.AddRange(tree[i].Children.Select((child, index) => new Category
            {
                Name = LocalizedText.Create(child.En, child.Bn),
                Slug = Slug.From(child.Slug),
                ParentId = parent.Id,
                Depth = 1,
                SortOrder = index,
                IsActive = true
            }));
        }

        context.Categories.AddRange(children);
        await context.SaveChangesAsync(cancellationToken);

        foreach (var child in children)
        {
            child.MaterializedPath = child.BuildPath(roots.First(r => r.Id == child.ParentId));
        }

        await context.SaveChangesAsync(cancellationToken);

        return roots.Concat(children).ToDictionary(c => c.Slug.Value, c => c);
    }

    private static async Task<Dictionary<string, Brand>> SeedBrandsAsync(
        DataContext context, CancellationToken cancellationToken)
    {
        var brands = new[]
        {
            new Brand
            {
                Name = LocalizedText.Create("WoodHeart", "উডহার্ট"),
                Slug = Slug.From("woodheart"),
                Description = LocalizedText.Create("Made in our own workshop in Dhaka."),
                SortOrder = 0
            },
            new Brand
            {
                Name = LocalizedText.Create("WoodHeart Studio", "উডহার্ট স্টুডিও"),
                Slug = Slug.From("woodheart-studio"),
                Description = LocalizedText.Create("Designer pieces, made to order."),
                SortOrder = 1
            }
        };

        context.Brands.AddRange(brands);
        await context.SaveChangesAsync(cancellationToken);

        return brands.ToDictionary(b => b.Slug.Value, b => b);
    }

    /// <summary>
    /// A dozen products across the tree, exercising every shape the catalog
    /// supports.
    /// </summary>
    /// <remarks>
    /// Chosen to cover the cases that behave differently rather than to fill the
    /// grid: a stocked product with wood and size variants, a made-to-order
    /// wardrobe with a lead time, a single-variant decor item, a product on
    /// offer, and one left as a draft so the "drafts are invisible to the
    /// storefront" rule has something to be tested against by hand.
    /// </remarks>
    private static async Task SeedProductsAsync(
        DataContext context,
        Dictionary<string, Category> categories,
        Dictionary<string, Brand> brands,
        IDateTimeProvider clock,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var house = brands["woodheart"];
        var studio = brands["woodheart-studio"];

        var products = new List<Product>
        {
            // Two option dimensions: the case the variant model exists for.
            NewProduct(
                "WH-BED-001", "Segun King Bed", "সেগুন কিং বেড", "segun-king-bed",
                categories["beds"], house, 68_500m, now,
                shortEn: "Solid Segun wood, hand-finished, with a slatted base.",
                material: "Segun (Teak)", finish: "Natural Matte", warranty: 24,
                lengthCm: 213, widthCm: 198, heightCm: 122, weightKg: 95,
                variants:
                [
                    ("WH-BED-001-SG-6", "Segun · 6ft", new() { ["Wood"] = "Segun", ["Size"] = "6ft" }, null, true),
                    ("WH-BED-001-SG-7", "Segun · 7ft", new() { ["Wood"] = "Segun", ["Size"] = "7ft" }, 76_000m, false),
                    ("WH-BED-001-MG-6", "Mehogoni · 6ft", new() { ["Wood"] = "Mehogoni", ["Size"] = "6ft" }, 54_000m, false),
                    ("WH-BED-001-MG-7", "Mehogoni · 7ft", new() { ["Wood"] = "Mehogoni", ["Size"] = "7ft" }, 61_000m, false)
                ]),

            // Made to order: no stock to reserve, a lead time instead.
            NewProduct(
                "WH-WRD-001", "Four-Door Wardrobe", "চার দরজার আলমারি", "four-door-wardrobe",
                categories["wardrobes"], studio, 92_000m, now,
                shortEn: "Built to your measurements. Soft-close hinges throughout.",
                material: "Engineered wood with Segun veneer", finish: "Walnut Stain",
                productType: ProductType.MadeToOrder, leadTimeDays: 21, assemblyRequired: true,
                lengthCm: 200, widthCm: 60, heightCm: 210, weightKg: 140,
                variants:
                [
                    ("WH-WRD-001-WAL", "Walnut", new() { ["Finish"] = "Walnut" }, null, true),
                    ("WH-WRD-001-NAT", "Natural", new() { ["Finish"] = "Natural" }, null, false)
                ]),

            // On offer: exercises the compare-at price and the discount badge.
            NewProduct(
                "WH-SOF-001", "Three-Seater Sofa", "থ্রি-সিটার সোফা", "three-seater-sofa",
                categories["sofas"], house, 48_000m, now,
                compareAt: 62_000m,
                shortEn: "Velvet upholstery over a seasoned Segun frame.",
                material: "Segun frame, velvet", finish: "Fabric", warranty: 12,
                lengthCm: 210, widthCm: 88, heightCm: 82, weightKg: 62,
                deliveryInsideDhaka: 1_500m, deliveryOutsideDhaka: 3_500m,
                variants:
                [
                    ("WH-SOF-001-CHA", "Charcoal", new() { ["Fabric"] = "Charcoal" }, null, true),
                    ("WH-SOF-001-OLV", "Olive", new() { ["Fabric"] = "Olive" }, null, false),
                    ("WH-SOF-001-RST", "Rust", new() { ["Fabric"] = "Rust" }, null, false)
                ]),

            NewProduct(
                "WH-DIN-001", "Six-Seat Dining Table", "ছয় আসনের ডাইনিং টেবিল", "six-seat-dining-table",
                categories["dining-tables"], house, 56_000m, now,
                shortEn: "Solid top, tapered legs, seats six comfortably.",
                material: "Segun (Teak)", finish: "Natural Matte", warranty: 24,
                lengthCm: 180, widthCm: 90, heightCm: 76, weightKg: 58,
                deliveryInsideDhaka: 1_200m, deliveryOutsideDhaka: 2_800m,
                variants:
                [
                    ("WH-DIN-001-SG", "Segun", new() { ["Wood"] = "Segun" }, null, true),
                    ("WH-DIN-001-MG", "Mehogoni", new() { ["Wood"] = "Mehogoni" }, 44_000m, false)
                ]),

            NewProduct(
                "WH-CHR-001", "Dining Chair", "ডাইনিং চেয়ার", "dining-chair",
                categories["dining-chairs"], house, 6_800m, now,
                shortEn: "Sold singly. Cushioned seat, solid wood frame.",
                material: "Segun (Teak)", finish: "Natural Matte",
                lengthCm: 45, widthCm: 50, heightCm: 96, weightKg: 7,
                variants:
                [
                    ("WH-CHR-001-SG", "Segun", new() { ["Wood"] = "Segun" }, null, true),
                    ("WH-CHR-001-MG", "Mehogoni", new() { ["Wood"] = "Mehogoni" }, 5_400m, false)
                ]),

            NewProduct(
                "WH-CTB-001", "Centre Table", "সেন্টার টেবিল", "centre-table",
                categories["centre-tables"], house, 14_500m, now,
                shortEn: "Low profile with an open shelf beneath.",
                material: "Segun (Teak)", finish: "Natural Matte",
                lengthCm: 110, widthCm: 60, heightCm: 42, weightKg: 22,
                variants: [("WH-CTB-001", "Standard", new(), null, true)]),

            NewProduct(
                "WH-DRS-001", "Dressing Table with Mirror", "আয়নাসহ ড্রেসিং টেবিল",
                "dressing-table-with-mirror",
                categories["dressing-tables"], house, 24_000m, now,
                shortEn: "Three drawers and a bevelled mirror.",
                material: "Segun (Teak)", finish: "Natural Matte", warranty: 12,
                lengthCm: 100, widthCm: 45, heightCm: 165, weightKg: 34,
                variants: [("WH-DRS-001", "Standard", new(), null, true)]),

            NewProduct(
                "WH-BSN-001", "Basin Cabinet", "বেসিন ক্যাবিনেট", "basin-cabinet",
                categories["basin-cabinets"], studio, 18_500m, now,
                shortEn: "Moisture-resistant board, wall mounted.",
                material: "Marine-grade ply", finish: "Gloss White", warranty: 12,
                lengthCm: 80, widthCm: 45, heightCm: 55, weightKg: 24,
                assemblyRequired: true,
                variants:
                [
                    ("WH-BSN-001-W", "White", new() { ["Finish"] = "White" }, null, true),
                    ("WH-BSN-001-G", "Graphite", new() { ["Finish"] = "Graphite" }, null, false)
                ]),

            NewProduct(
                "WH-PND-001", "Rattan Pendant Light", "রট্টান পেন্ডেন্ট লাইট", "rattan-pendant-light",
                categories["pendants"], studio, 4_200m, now,
                shortEn: "Hand-woven rattan shade. Bulb not included.",
                material: "Rattan", finish: "Natural",
                lengthCm: 40, widthCm: 40, heightCm: 35, weightKg: 2,
                variants:
                [
                    ("WH-PND-001-S", "Small", new() { ["Size"] = "Small" }, null, true),
                    ("WH-PND-001-L", "Large", new() { ["Size"] = "Large" }, 5_600m, false)
                ]),

            NewProduct(
                "WH-WAL-001", "Nakshi Wall Art", "নকশি ওয়াল আর্ট", "nakshi-wall-art",
                categories["wall-art"], studio, 3_400m, now,
                shortEn: "Hand-stitched panel, framed in Segun.",
                material: "Cotton, Segun frame", finish: "Framed",
                lengthCm: 60, widthCm: 4, heightCm: 60, weightKg: 2,
                variants: [("WH-WAL-001", "Standard", new(), null, true)]),

            NewProduct(
                "WH-PLT-001", "Terracotta Planter", "টেরাকোটা প্ল্যান্টার", "terracotta-planter",
                categories["planters"], studio, 1_250m, now,
                shortEn: "Thrown and fired in Rajshahi.",
                material: "Terracotta", finish: "Unglazed",
                lengthCm: 25, widthCm: 25, heightCm: 28, weightKg: 3,
                variants:
                [
                    ("WH-PLT-001-S", "Small", new() { ["Size"] = "Small" }, null, true),
                    ("WH-PLT-001-M", "Medium", new() { ["Size"] = "Medium" }, 1_800m, false),
                    ("WH-PLT-001-L", "Large", new() { ["Size"] = "Large" }, 2_400m, false)
                ])
        };

        // Deliberately left Draft. Gives the "drafts never reach the storefront"
        // rule something to be checked against by hand: it appears in the admin
        // grid and must 404 at /api/catalog/products/showcase-cabinet.
        var draft = NewProduct(
            "WH-SHW-001", "Showcase Cabinet", "শোকেস ক্যাবিনেট", "showcase-cabinet",
            categories["showcases"], house, 38_000m, now,
            shortEn: "Glass-fronted, four shelves. NOT YET PUBLISHED.",
            material: "Segun (Teak)", finish: "Natural Matte",
            lengthCm: 120, widthCm: 40, heightCm: 180, weightKg: 55,
            variants: [("WH-SHW-001", "Standard", new(), null, true)]);

        draft.Status = ProductStatus.Draft;
        draft.PublishedAt = null;
        products.Add(draft);

        context.Products.AddRange(products);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedCollectionsAsync(DataContext context, CancellationToken cancellationToken)
    {
        var published = await context.Products
            .Where(p => p.Status == ProductStatus.Active)
            .ToListAsync(cancellationToken);

        var bedroom = new Collection
        {
            Name = LocalizedText.Create("Shop the Bedroom", "শয়নকক্ষ সাজান"),
            Slug = Slug.From("shop-the-bedroom"),
            Description = LocalizedText.Create("Everything for a complete bedroom, chosen to go together."),
            IsFeatured = true,
            SortOrder = 0,
            Products = [.. published.Where(p =>
                p.Code.StartsWith("WH-BED", StringComparison.Ordinal)
                || p.Code.StartsWith("WH-WRD", StringComparison.Ordinal)
                || p.Code.StartsWith("WH-DRS", StringComparison.Ordinal))]
        };

        var minimalist = new Collection
        {
            Name = LocalizedText.Create("Minimalist Living", "মিনিমালিস্ট লিভিং"),
            Slug = Slug.From("minimalist-living"),
            Description = LocalizedText.Create("Clean lines and natural finishes for a calmer room."),
            IsFeatured = true,
            SortOrder = 1,
            Products = [.. published.Where(p =>
                p.Code.StartsWith("WH-SOF", StringComparison.Ordinal)
                || p.Code.StartsWith("WH-CTB", StringComparison.Ordinal)
                || p.Code.StartsWith("WH-PND", StringComparison.Ordinal)
                || p.Code.StartsWith("WH-PLT", StringComparison.Ordinal))]
        };

        context.Collections.AddRange(bedroom, minimalist);
        await context.SaveChangesAsync(cancellationToken);
    }

    // -------------------------------------------------------------------------

    private static Product NewProduct(
        string code,
        string nameEn,
        string nameBn,
        string slug,
        Category category,
        Brand brand,
        decimal basePrice,
        DateTimeOffset publishedAt,
        string? shortEn = null,
        decimal? compareAt = null,
        string? material = null,
        string? finish = null,
        int? warranty = null,
        decimal? lengthCm = null,
        decimal? widthCm = null,
        decimal? heightCm = null,
        decimal? weightKg = null,
        decimal? deliveryInsideDhaka = null,
        decimal? deliveryOutsideDhaka = null,
        ProductType productType = ProductType.Stocked,
        int? leadTimeDays = null,
        bool assemblyRequired = false,
        (string Sku, string Name, Dictionary<string, string> Options, decimal? Price, bool IsDefault)[]? variants = null)
    {
        var product = new Product
        {
            Code = code,
            Name = LocalizedText.Create(nameEn, nameBn),
            Slug = Slug.From(slug),
            ShortDescription = shortEn is null ? null : LocalizedText.Create(shortEn),
            CategoryId = category.Id,
            BrandId = brand.Id,
            ProductType = productType,
            Status = ProductStatus.Active,
            PublishedAt = publishedAt,
            BasePrice = Money.Taka(basePrice),
            CompareAtPrice = compareAt is null ? null : Money.Taka(compareAt.Value),
            Material = material,
            FinishType = finish,
            WarrantyMonths = warranty,
            LengthCm = lengthCm,
            WidthCm = widthCm,
            HeightCm = heightCm,
            WeightKg = weightKg,
            DeliveryChargeInsideDhaka =
                deliveryInsideDhaka is null ? null : Money.Taka(deliveryInsideDhaka.Value),
            DeliveryChargeOutsideDhaka =
                deliveryOutsideDhaka is null ? null : Money.Taka(deliveryOutsideDhaka.Value),
            LeadTimeDays = leadTimeDays,
            AssemblyRequired = assemblyRequired,
            SeoTitle = $"{nameEn} — WoodHeart",
            SeoDescription = shortEn
        };

        product.Variants = [.. (variants ?? [(code, "Standard", new Dictionary<string, string>(), null, true)])
            .Select((v, index) => new ProductVariant
            {
                Sku = v.Sku,
                VariantName = v.Name,
                OptionValues = v.Options,
                PriceOverride = v.Price is null ? null : Money.Taka(v.Price.Value),
                IsDefault = v.IsDefault,
                IsActive = true,
                SortOrder = index
            })];

        return product;
    }
}
