using System.Reflection;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using WoodHeart.Application.Common.Behaviours;

namespace WoodHeart.Application;

/// <summary>Registers the use-case layer: validators and the request pipeline.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Source-generated dispatch: no reflection at runtime, and a missing
        // handler is a compile-time error rather than a 3am surprise.
        //
        // This call MUST live in this project. The generator reads the lifetime
        // from the call site at compile time, so invoking AddMediator from the
        // API project silently produces a Singleton registration and a startup
        // failure. Registering handlers in the layer that owns them is also
        // simply the right place for it.
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

        services.AddValidatorsFromAssembly(ApplicationAssembly.Reference, includeInternalTypes: true);

        // Order is the execution order, outermost first:
        //
        //   Logging      — records everything below it, including failures
        //     Validation — rejects bad input before a transaction is opened
        //       UnitOfWork — opens the transaction, commits on success
        //         handler
        //
        // Logging must wrap validation so rejected requests still appear in the
        // logs; UnitOfWork must sit inside validation so an invalid request
        // never starts a transaction at all.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehaviour<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkBehaviour<,>));

        return services;
    }
}

/// <summary>Assembly marker, so scanning never depends on a magic string.</summary>
public static class ApplicationAssembly
{
    public static Assembly Reference => typeof(ApplicationAssembly).Assembly;
}
