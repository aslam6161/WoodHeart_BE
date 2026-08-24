using System.Reflection;
using NetArchTest.Rules;
using WoodHeart.Application.Common.Messaging;

namespace WoodHeart.ArchitectureTests;

/// <summary>
/// Enforces the onion dependency rule mechanically.
/// </summary>
/// <remarks>
/// <para>
/// Every architecture document in the world describes rules that erode within
/// six months, because nothing stops a developer under deadline pressure from
/// adding one convenient <c>using</c>. These tests make each rule a build
/// failure instead of a code-review opinion — which is the only form of
/// architectural constraint that survives contact with a real schedule.
/// </para>
/// <para>
/// If one of these fails, the fix is essentially never to relax the test.
/// It is to move the code to the layer it actually belongs in.
/// </para>
/// </remarks>
public class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(WoodHeart.Domain.Common.Entity).Assembly;
    private static readonly Assembly Application = typeof(ICommand).Assembly;
    private static readonly Assembly Infrastructure = typeof(WoodHeart.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private const string DomainNamespace = "WoodHeart.Domain";
    private const string ApplicationNamespace = "WoodHeart.Application";
    private const string InfrastructureNamespace = "WoodHeart.Infrastructure";
    private const string ApiNamespace = "WoodHeart.Api";

    [Fact]
    public void Domain_should_not_depend_on_any_other_layer()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace, ApiNamespace)
            .GetResult();

        result.ShouldBeSuccessful(
            "the Domain is the core of the onion and must depend on nothing");
    }

    /// <summary>
    /// The Domain must stay free of infrastructure packages entirely.
    /// </summary>
    /// <remarks>
    /// Not pedantry. The moment an EF attribute or a JSON attribute appears on
    /// an entity, the persistence choice has leaked into the business model, and
    /// every future change to storage becomes a change to the domain. Keeping
    /// this boundary is what makes the Domain unit tests fast and mock-free.
    /// </remarks>
    [Fact]
    public void Domain_should_not_depend_on_infrastructure_packages()
    {
        string[] forbidden =
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "Microsoft.Extensions.DependencyInjection",
            "Npgsql",
            "Mediator",
            "FluentValidation",
            "Newtonsoft.Json",
            "System.Text.Json",
            "Hangfire"
        ];

        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        result.ShouldBeSuccessful(
            "the Domain models the business, not the machinery that stores or serves it");
    }

    [Fact]
    public void Application_should_not_depend_on_infrastructure_or_api()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNamespace, ApiNamespace)
            .GetResult();

        result.ShouldBeSuccessful(
            "the Application layer declares ports; Infrastructure implements them, " +
            "and the dependency must point inward");
    }

    /// <summary>
    /// The Application layer must not reference EF Core.
    /// </summary>
    /// <remarks>
    /// This is the rule most often broken in practice, usually by exposing
    /// <c>DbSet&lt;T&gt;</c> or <c>IQueryable&lt;T&gt;</c> from a port "just for
    /// this one query". It is exactly how a codebase ends up unable to change
    /// its ORM, and how handlers acquire silent lazy-loading behaviour that only
    /// shows up as an N+1 in production.
    /// </remarks>
    [Fact]
    public void Application_should_not_depend_on_entity_framework()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        result.ShouldBeSuccessful(
            "data access belongs behind a repository port, not in the use-case layer");
    }

    [Fact]
    public void Infrastructure_should_not_depend_on_api()
    {
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot()
            .HaveDependencyOnAny(ApiNamespace)
            .GetResult();

        result.ShouldBeSuccessful(
            "adapters must not reach back into the presentation layer");
    }

    /// <summary>
    /// Controllers must not touch the DbContext directly.
    /// </summary>
    /// <remarks>
    /// A controller with a DbContext bypasses validation, authorization, the
    /// transaction boundary and domain-event dispatch — every guarantee the
    /// pipeline provides. It always starts as "just a quick read".
    /// </remarks>
    [Fact]
    public void Controllers_should_not_depend_on_the_dbcontext()
    {
        var result = Types.InAssembly(Api)
            .That()
            .HaveNameEndingWith("Controller")
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "WoodHeart.Infrastructure.Persistence")
            .GetResult();

        result.ShouldBeSuccessful(
            "controllers dispatch commands and queries; they do not query the database");
    }
}
