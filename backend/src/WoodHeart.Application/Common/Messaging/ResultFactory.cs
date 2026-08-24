using System.Collections.Concurrent;
using System.Reflection;
using WoodHeart.Domain.Common;

namespace WoodHeart.Application.Common.Messaging;

/// <summary>
/// Builds a failed <see cref="Result"/> or <see cref="Result{TValue}"/> when the
/// concrete type is only known as a generic parameter.
/// </summary>
/// <remarks>
/// Pipeline behaviours are generic over <c>TResponse</c>, but a validation
/// failure has to come back as the *right* result type. Rather than throwing an
/// exception and unwinding the stack — which is exactly the "exceptions as
/// control flow" we avoid elsewhere — the behaviour short-circuits by
/// constructing the correct failure here.
/// <para>
/// The reflection cost is paid once per closed generic type and then cached, so
/// the steady-state cost is a dictionary lookup.
/// </para>
/// </remarks>
internal static class ResultFactory
{
    private static readonly ConcurrentDictionary<Type, Func<Error, object>> Factories = new();

    private static readonly MethodInfo GenericFailureMethod = typeof(Result)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m is { Name: nameof(Result.Failure), IsGenericMethodDefinition: true });

    public static TResponse Failure<TResponse>(Error error)
    {
        var responseType = typeof(TResponse);

        // Non-generic Result — the common case for a plain command.
        if (responseType == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(error);
        }

        if (!responseType.IsGenericType || responseType.GetGenericTypeDefinition() != typeof(Result<>))
        {
            throw new InvalidOperationException(
                $"{responseType.Name} is not a Result. Every command and query must return " +
                "Result or Result<T> so the pipeline can short-circuit without throwing.");
        }

        var factory = Factories.GetOrAdd(responseType, static type =>
        {
            var closed = GenericFailureMethod.MakeGenericMethod(type.GetGenericArguments()[0]);
            return err => closed.Invoke(null, [err])!;
        });

        return (TResponse)factory(error);
    }
}
