using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Service.DTOs.Catalog;
using WoodHeart.Service.Services.Catalog;

namespace WoodHeart.Tests.Catalog;

/// <summary>
/// The category tree's decisions: which failures are refused, and what happens
/// to the materialized path when a subtree moves.
/// </summary>
/// <remarks>
/// Substitutes, no database. The path rewriting is pure logic over strings and
/// integers, and it is the part most likely to be quietly wrong — a bad prefix
/// swap does not throw, it just detaches a branch from the tree.
/// </remarks>
public class CategoryServiceTests
{
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    /// <summary>
    /// Default stubs live in the constructor, not in <c>CreateService</c>.
    /// </summary>
    /// <remarks>
    /// xUnit builds a fresh instance per test, so the constructor runs before
    /// the test body and a test is free to re-stub anything. Putting these in a
    /// factory called at the end of a test would overwrite whatever that test
    /// had just set up — which is exactly the mistake that made the
    /// product-count assertion fail against an empty dictionary.
    /// </remarks>
    public CategoryServiceTests()
    {
        // The real unit of work runs the callback inside a transaction. The
        // substitute has to actually invoke it, or every method that commits
        // would silently return null.
        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<GeneralResponse<CategoryDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<GeneralResponse<CategoryDto>>>>()(
                CancellationToken.None));

        _categories.GetProductCountsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<long, int>());
    }

    private CategoryService CreateService() => new(_categories, _unitOfWork);

    // -------------------------------------------------------------------------
    // Guards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_refuses_while_the_category_still_has_children()
    {
        // Soft-deleting a parent would leave its children live but unreachable:
        // the query filter hides the parent, and the subtree vanishes from the
        // tree with no error and no obvious way back.
        var category = NewCategory(1, "living-room", "/1/");
        _categories.GetByIdAsync(1L, Arg.Any<CancellationToken>()).Returns(category);
        _categories.HasChildrenAsync(1, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().DeleteAsync(1);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(CatalogErrors.CategoryHasChildren);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_refuses_while_the_category_still_has_products()
    {
        var category = NewCategory(1, "sofas", "/1/");
        _categories.GetByIdAsync(1L, Arg.Any<CancellationToken>()).Returns(category);
        _categories.HasChildrenAsync(1, Arg.Any<CancellationToken>()).Returns(false);
        _categories.HasProductsAsync(1, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().DeleteAsync(1);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(CatalogErrors.CategoryHasProducts);
    }

    [Fact]
    public async Task Delete_succeeds_on_an_empty_leaf()
    {
        var category = NewCategory(9, "lamps", "/1/9/");
        _categories.GetByIdAsync(9L, Arg.Any<CancellationToken>()).Returns(category);
        _categories.HasChildrenAsync(9, Arg.Any<CancellationToken>()).Returns(false);
        _categories.HasProductsAsync(9, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateService().DeleteAsync(9);

        result.IsSuccess.ShouldBeTrue();
        _categories.Received(1).Delete(category);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_of_a_missing_category_is_a_not_found_not_an_exception()
    {
        _categories.GetByIdAsync(404L, Arg.Any<CancellationToken>()).Returns((Category?)null);

        var result = await CreateService().DeleteAsync(404);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(CatalogErrors.CategoryNotFound);
    }

    [Fact]
    public async Task Create_refuses_a_slug_that_is_already_taken()
    {
        _categories.SlugExistsAsync("sofas", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().CreateAsync(new CreateCategoryDto { NameEn = "Sofas" });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(CatalogErrors.CategorySlugTaken);
    }

    [Fact]
    public async Task Create_refuses_a_name_that_cannot_become_a_slug()
    {
        // "!!!" normalises to nothing. Slug.From throws, and that has to surface
        // as a validation failure rather than a 500.
        var result = await CreateService().CreateAsync(new CreateCategoryDto { NameEn = "!!!" });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(CatalogErrors.SlugNotDerivable);
    }

    [Fact]
    public async Task Create_under_a_missing_parent_fails()
    {
        _categories.GetByIdAsync(77L, Arg.Any<CancellationToken>()).Returns((Category?)null);

        var result = await CreateService()
            .CreateAsync(new CreateCategoryDto { NameEn = "Recliners", ParentId = 77 });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(CatalogErrors.ParentCategoryNotFound);
    }

    [Fact]
    public async Task Update_leaves_the_slug_alone_when_none_is_supplied()
    {
        // Renaming must not move a published URL. Every inbound link and every
        // Facebook share points at the old one.
        var category = NewCategory(1, "sofas", "/1/");
        _categories.GetByIdAsync(1L, Arg.Any<CancellationToken>()).Returns(category);

        var result = await CreateService()
            .UpdateAsync(1, new UpdateCategoryDto { NameEn = "Comfortable Sofas" });

        result.IsSuccess.ShouldBeTrue();
        category.Slug.Value.ShouldBe("sofas");
        category.Name.En.ShouldBe("Comfortable Sofas");
    }

    // -------------------------------------------------------------------------
    // Move
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Move_refuses_to_make_a_category_its_own_parent()
    {
        _categories.GetByIdAsync(1L, Arg.Any<CancellationToken>())
            .Returns(NewCategory(1, "living-room", "/1/"));

        var result = await CreateService().MoveAsync(1, new MoveCategoryDto { NewParentId = 1 });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(CatalogErrors.CategoryCycle);
    }

    [Fact]
    public async Task Move_refuses_to_put_a_category_beneath_its_own_descendant()
    {
        // The failure that matters. Moving Living Room under Sofas — which is
        // inside Living Room — detaches the whole branch: the rows survive and
        // nothing can navigate to them.
        var livingRoom = NewCategory(1, "living-room", "/1/");
        var sofas = NewCategory(14, "sofas", "/1/14/", parentId: 1, depth: 1);

        _categories.GetByIdAsync(1L, Arg.Any<CancellationToken>()).Returns(livingRoom);
        _categories.GetByIdAsync(14L, Arg.Any<CancellationToken>()).Returns(sofas);

        var result = await CreateService().MoveAsync(1, new MoveCategoryDto { NewParentId = 14 });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(CatalogErrors.CategoryCycle);
    }

    [Fact]
    public async Task Move_rewrites_the_path_and_depth_of_every_descendant()
    {
        // Bedroom/Beds/King  →  moved under Living Room.
        // Every path below the moved node has to have its prefix swapped, and
        // every depth shifted by the same delta.
        var bedroom = NewCategory(2, "bedroom", "/2/");
        var beds = NewCategory(20, "beds", "/2/20/", parentId: 2, depth: 1);
        var king = NewCategory(30, "king", "/2/20/30/", parentId: 20, depth: 2);
        var livingRoom = NewCategory(1, "living-room", "/1/");

        _categories.GetByIdAsync(20L, Arg.Any<CancellationToken>()).Returns(beds);
        _categories.GetByIdAsync(1L, Arg.Any<CancellationToken>()).Returns(livingRoom);
        _categories.GetSubtreeAsync("/2/20/", Arg.Any<CancellationToken>())
            .Returns([beds, king]);
        _categories.MaxSortOrderAsync(1L, Arg.Any<CancellationToken>()).Returns(3);

        var result = await CreateService().MoveAsync(20, new MoveCategoryDto { NewParentId = 1 });

        result.IsSuccess.ShouldBeTrue();

        beds.ParentId.ShouldBe(1);
        beds.MaterializedPath.ShouldBe("/1/20/");
        beds.Depth.ShouldBe(1);
        beds.SortOrder.ShouldBe(4);

        // The grandchild moved with it, and its depth is unchanged because the
        // node it hangs from did not change depth either.
        king.MaterializedPath.ShouldBe("/1/20/30/");
        king.Depth.ShouldBe(2);
        king.ParentId.ShouldBe(20);

        // Untouched.
        bedroom.MaterializedPath.ShouldBe("/2/");
    }

    [Fact]
    public async Task Move_to_the_root_shifts_descendant_depth_down()
    {
        var livingRoom = NewCategory(1, "living-room", "/1/");
        var sofas = NewCategory(14, "sofas", "/1/14/", parentId: 1, depth: 1);
        var lShaped = NewCategory(37, "l-shaped", "/1/14/37/", parentId: 14, depth: 2);

        _categories.GetByIdAsync(14L, Arg.Any<CancellationToken>()).Returns(sofas);
        _categories.GetSubtreeAsync("/1/14/", Arg.Any<CancellationToken>())
            .Returns([sofas, lShaped]);
        _categories.MaxSortOrderAsync(null, Arg.Any<CancellationToken>()).Returns(0);

        var result = await CreateService().MoveAsync(14, new MoveCategoryDto { NewParentId = null });

        result.IsSuccess.ShouldBeTrue();

        sofas.ParentId.ShouldBeNull();
        sofas.MaterializedPath.ShouldBe("/14/");
        sofas.Depth.ShouldBe(0);

        // Was depth 2 under a depth-1 parent; the parent became depth 0, so this
        // becomes 1. Getting the delta wrong here is invisible until someone
        // renders the tree with the wrong indentation.
        lShaped.MaterializedPath.ShouldBe("/14/37/");
        lShaped.Depth.ShouldBe(1);

        livingRoom.MaterializedPath.ShouldBe("/1/");
    }

    [Fact]
    public async Task Move_honours_an_explicit_sort_order()
    {
        var sofas = NewCategory(14, "sofas", "/1/14/", parentId: 1, depth: 1);
        var root = NewCategory(2, "bedroom", "/2/");

        _categories.GetByIdAsync(14L, Arg.Any<CancellationToken>()).Returns(sofas);
        _categories.GetByIdAsync(2L, Arg.Any<CancellationToken>()).Returns(root);
        _categories.GetSubtreeAsync("/1/14/", Arg.Any<CancellationToken>()).Returns([sofas]);

        var result = await CreateService()
            .MoveAsync(14, new MoveCategoryDto { NewParentId = 2, SortOrder = 0 });

        result.IsSuccess.ShouldBeTrue();
        sofas.SortOrder.ShouldBe(0);

        // The append path must not have been consulted at all.
        await _categories.DidNotReceive().MaxSortOrderAsync(2L, Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Tree assembly
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Tree_nests_children_under_their_parents()
    {
        // Living Room is sorted first deliberately, even though "bedroom"
        // sorts earlier alphabetically — the admin's manual order wins.
        _categories.GetTreeAsync(true, Arg.Any<CancellationToken>()).Returns(
        [
            NewCategory(1, "living-room", "/1/", sortOrder: 0),
            NewCategory(14, "sofas", "/1/14/", parentId: 1, depth: 1),
            NewCategory(37, "l-shaped", "/1/14/37/", parentId: 14, depth: 2),
            NewCategory(2, "bedroom", "/2/", sortOrder: 1)
        ]);

        var result = await CreateService().GetTreeAsync(includeInactive: true);

        result.IsSuccess.ShouldBeTrue();
        var roots = result.Data!;

        roots.Count.ShouldBe(2);
        roots[0].Slug.ShouldBe("living-room");
        roots[0].Children.Count.ShouldBe(1);
        roots[0].Children[0].Slug.ShouldBe("sofas");
        roots[0].Children[0].Children[0].Slug.ShouldBe("l-shaped");
        roots[1].Children.ShouldBeEmpty();
    }

    [Fact]
    public async Task Tree_promotes_an_orphan_to_the_root_rather_than_dropping_it()
    {
        // Happens legitimately: a live child whose parent is inactive and
        // therefore filtered out. Showing it at the top beats it disappearing
        // from the admin tree, where it would look deleted.
        _categories.GetTreeAsync(false, Arg.Any<CancellationToken>()).Returns(
        [
            NewCategory(14, "sofas", "/1/14/", parentId: 1, depth: 1)
        ]);

        var result = await CreateService().GetTreeAsync();

        result.Data!.Count.ShouldBe(1);
        result.Data[0].Slug.ShouldBe("sofas");
    }

    [Fact]
    public async Task Tree_attaches_product_counts_without_a_query_per_node()
    {
        _categories.GetTreeAsync(false, Arg.Any<CancellationToken>()).Returns(
        [
            NewCategory(1, "living-room", "/1/", sortOrder: 0),
            NewCategory(2, "bedroom", "/2/", sortOrder: 1)
        ]);
        _categories.GetProductCountsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<long, int> { [1] = 12 });

        var result = await CreateService().GetTreeAsync();

        result.Data![0].ProductCount.ShouldBe(12);
        result.Data[1].ProductCount.ShouldBe(0);

        // One grouped call, not one per category.
        await _categories.Received(1).GetProductCountsAsync(Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------

    private static Category NewCategory(
        long id, string slug, string path,
        long? parentId = null, int depth = 0, int sortOrder = 0) => new()
        {
            Id = id,
            Name = LocalizedText.Create(slug),
            Slug = Slug.From(slug),
            MaterializedPath = path,
            ParentId = parentId,
            Depth = depth,
            SortOrder = sortOrder,
            IsActive = true
        };
}
