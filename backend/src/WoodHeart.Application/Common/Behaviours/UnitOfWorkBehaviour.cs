using Mediator;
using WoodHeart.Application.Common.Abstractions;
using WoodHeart.Application.Common.Messaging;
using WoodHeart.Domain.Common;

namespace WoodHeart.Application.Common.Behaviours;

/// <summary>
/// Wraps commands — and only commands — in a database transaction, committing
/// once the handler succeeds.
/// </summary>
/// <remarks>
/// <para>
/// This is what lets a handler read like the business operation it models:
/// load the aggregate, call its methods, return. No handler has to remember to
/// open a transaction, and none can forget to roll one back.
/// </para>
/// <para>
/// Three deliberate exclusions:
/// </para>
/// <list type="bullet">
///   <item><b>Queries</b> never enter a transaction — pure reads gain nothing
///         and would hold locks for no reason.</item>
///   <item><b>A failed <see cref="Result"/> rolls back.</b> A handler that
///         returns "insufficient stock" after having already written a
///         reservation must not leave that write behind. Returning a failure is
///         as final as throwing.</item>
///   <item><b><see cref="ITransactionless"/> commands opt out</b> — anything
///         calling bKash or an SMS gateway mid-handler must not hold a
///         transaction open across a network round trip that can hang.</item>
/// </list>
/// </remarks>
public sealed class UnitOfWorkBehaviour<TMessage, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        if (message is not ICommandMarker || message is ITransactionless)
        {
            return await next(message, cancellationToken);
        }

        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var response = await next(message, ct);

                if (response is Result { IsFailure: true })
                {
                    // Signals rollback; the original failure is handed back below.
                    throw new UseCaseFailedException(response!);
                }

                await unitOfWork.SaveChangesAsync(ct);
                return response;
            }, cancellationToken);
        }
        catch (UseCaseFailedException failed)
        {
            return (TResponse)failed.Response;
        }
    }
}

/// <summary>
/// Carries a failed result out through the transaction boundary so the
/// transaction rolls back. An implementation detail of
/// <see cref="UnitOfWorkBehaviour{TMessage,TResponse}"/> that never escapes it.
/// </summary>
internal sealed class UseCaseFailedException(object response)
    : Exception("Use case returned a failure result.")
{
    public object Response { get; } = response;
}
