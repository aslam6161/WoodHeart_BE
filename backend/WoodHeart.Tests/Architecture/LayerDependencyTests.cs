using System.Reflection;
using NetArchTest.Rules;
using WoodHeart.Repository;
using WoodHeart.Service.Interfaces.Common;

namespace WoodHeart.Tests.Architecture;

/// <summary>
/// The layer rules, as executable tests.
/// </summary>
/// <remarks>
/// <para>
/// Layering that lives only in a README erodes in weeks — someone needs a
/// DbContext in a controller at 6pm on a Thursday and nothing stops them. These
/// tests make a violation a build failure instead of a code-review opinion.
/// </para>
/// <para>
/// When one of these fails, the fix is essentially never to relax the test. It
/// is to move the code into the layer it belongs in.
/// </para>
/// </remarks>
public class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(Domain.Entity.BaseEntity).Assembly;
    private static readonly Assembly Repository = typeof(DataContext).Assembly;
    private static readonly Assembly Service = typeof(ICurrentUserService).Assembly;
    private static readonly Assembly Presentation = typeof(Program).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_of_ours()
    {
        // The innermost layer. If Domain can reference Repository, then an
        // entity can reach the database, and "what does saving this touch?"
        // stops having a bounded answer.
        Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny("WoodHeart.Repository", "WoodHeart.Service", "WoodHeart.Presentation")
            .GetResult()
            .ShouldBeSuccessful("Domain is the innermost layer");
    }

    [Fact]
    public void Domain_does_not_depend_on_EF_Core()
    {
        // Identity's EF package is the one deliberate exception, since AppUser
        // derives from IdentityUser<long>. Nothing else may reach for a
        // DbContext or a DbSet.
        Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore.Storage",
                "Microsoft.EntityFrameworkCore.ChangeTracking",
                "Npgsql")
            .GetResult()
            .ShouldBeSuccessful("entities describe the business, not its storage");
    }

    [Fact]
    public void Repository_does_not_depend_on_Service_or_Presentation()
    {
        Types.InAssembly(Repository)
            .Should()
            .NotHaveDependencyOnAny("WoodHeart.Service", "WoodHeart.Presentation")
            .GetResult()
            .ShouldBeSuccessful("data access must not call business logic — that inverts the layering");
    }

    [Fact]
    public void Service_does_not_depend_on_Presentation()
    {
        // A service that knows about controllers cannot be called from a
        // Hangfire job, which is where half of this application's work runs.
        Types.InAssembly(Service)
            .Should()
            .NotHaveDependencyOnAny("WoodHeart.Presentation")
            .GetResult()
            .ShouldBeSuccessful("services must be callable from a background job, not only from HTTP");
    }

    [Fact]
    public void Controllers_do_not_touch_the_DataContext()
    {
        // The rule that keeps business logic out of controllers. A controller
        // with a DbContext writes a query, then a condition, then a rule — and
        // that rule is now untestable without HTTP.
        Types.InAssembly(Presentation)
            .That()
            .HaveNameEndingWith("Controller")
            .Should()
            .NotHaveDependencyOnAny("WoodHeart.Repository.DataContext", "Microsoft.EntityFrameworkCore")
            .GetResult()
            .ShouldBeSuccessful("controllers call services, never the database");
    }

    [Fact]
    public void Every_public_service_has_an_interface()
    {
        // Plain reflection rather than the fluent API: the rule is about a
        // type's interfaces, which reads more clearly stated directly.
        // An interface is what makes a service mockable in a test and
        // swappable in DI — a concrete-only service is neither.
        var offenders = Service.GetTypes()
            .Where(type => type.IsClass
                           && type.IsPublic
                           && !type.IsAbstract
                           && type.Namespace?.StartsWith(
                               "WoodHeart.Service.Services", StringComparison.Ordinal) == true
                           && type.GetInterfaces().Length == 0)
            .Select(type => type.FullName)
            .ToArray();

        offenders.ShouldBeEmpty(
            $"every service needs an interface. Missing on: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Repositories_live_in_the_Repository_project()
    {
        var strays = Service.GetTypes()
            .Where(type => type.IsClass && type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .ToArray();

        strays.ShouldBeEmpty(
            "repositories belong in WoodHeart.Repository, not in the service layer. "
            + $"Found: {string.Join(", ", strays)}");
    }
}
