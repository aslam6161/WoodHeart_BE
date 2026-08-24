using Mediator;
using WoodHeart.Domain.Common;

namespace WoodHeart.Application.Common.Messaging;

/// <summary>
/// A command changes state. Commands are named imperatively —
/// <c>PlaceOrderCommand</c>, <c>ConfirmBookingCommand</c>.
/// </summary>
/// <remarks>
/// Separating commands from queries is what lets the pipeline treat them
/// differently: commands run inside a transaction and dispatch domain events
/// afterwards, queries do neither. That distinction is impossible if everything
/// is just "a request".
/// </remarks>
public interface ICommand : ICommandMarker, IRequest<Result>;

/// <summary>A command that returns a value, typically the id of what it created.</summary>
public interface ICommand<TResponse> : ICommandMarker, IRequest<Result<TResponse>>;

/// <summary>
/// Non-generic marker shared by both command shapes, so pipeline behaviours can
/// ask "is this a command?" without knowing its response type.
/// </summary>
public interface ICommandMarker;

/// <summary>
/// A query reads state and must never change it.
/// </summary>
/// <remarks>
/// Queries are allowed to bypass repositories and project straight to DTOs.
/// Read models get to be pragmatic; write models do not.
/// </remarks>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;

/// <summary>
/// Marks a command as safe to replay. Combined with an <c>Idempotency-Key</c>
/// header this prevents the duplicate orders that double-tapping on a flaky
/// mobile connection would otherwise create.
/// </summary>
public interface IIdempotentCommand
{
    Guid RequestId { get; }
}

/// <summary>
/// Opts a request out of the ambient transaction. Use for commands that call an
/// external gateway and must not hold a database transaction open across the
/// network round trip.
/// </summary>
public interface ITransactionless;
