using FluentValidation;
using Mediator;
using WoodHeart.Application.Common.Messaging;
using WoodHeart.Domain.Common;

namespace WoodHeart.Application.Common.Behaviours;

/// <summary>
/// Runs every registered <see cref="IValidator{T}"/> for the incoming message
/// before the handler sees it.
/// </summary>
/// <remarks>
/// <para>
/// Validation lives in the pipeline rather than in controllers so it cannot be
/// bypassed. The same command dispatched from a background job, an admin
/// endpoint or an integration test is validated identically.
/// </para>
/// <para>
/// On failure this short-circuits with a <see cref="ErrorType.Validation"/>
/// result rather than throwing, so the API layer maps it to a 400 with a
/// per-field <c>errors</c> dictionary that Angular's reactive forms can bind
/// straight to their controls.
/// </para>
/// </remarks>
public sealed class ValidationBehaviour<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var applicable = validators as IValidator<TMessage>[] ?? validators.ToArray();

        if (applicable.Length == 0)
        {
            return await next(message, cancellationToken);
        }

        var context = new ValidationContext<TMessage>(message);

        var failures = (await Task.WhenAll(
                applicable.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToArray();

        if (failures.Length == 0)
        {
            return await next(message, cancellationToken);
        }

        var errors = failures
            .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                g => ToCamelCase(g.Key),
                g => g.Select(f => f.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        var error = Error.Validation(
            "common.validation_failed",
            "One or more fields need your attention.",
            errors);

        return ResultFactory.Failure<TResponse>(error);
    }

    // Property names are camelCased to match what the Angular client sends and
    // expects, so a form control can be looked up by the key we return.
    private static string ToCamelCase(string propertyName) =>
        string.IsNullOrEmpty(propertyName) || char.IsLower(propertyName[0])
            ? propertyName
            : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
}
