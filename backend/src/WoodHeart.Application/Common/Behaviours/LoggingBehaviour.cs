using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.Logging;
using WoodHeart.Application.Common.Abstractions;
using WoodHeart.Domain.Common;

namespace WoodHeart.Application.Common.Behaviours;

/// <summary>
/// Logs the start, outcome and duration of every use case, with the caller and
/// correlation id attached.
/// </summary>
/// <remarks>
/// <para>
/// This is the log line you will actually read at 11pm when a customer says
/// their payment failed: one structured entry per use case, correlatable from
/// the Angular request all the way through to the bKash call.
/// </para>
/// <para>
/// Business failures log at Warning, not Error. "Coupon expired" paging someone
/// at 3am trains everyone to ignore alerts, so <see cref="ErrorType"/> picks the
/// level: only <see cref="ErrorType.External"/> and unhandled exceptions are
/// genuine Errors.
/// </para>
/// <para>
/// Messages go through source-generated <c>LoggerMessage</c> delegates because
/// this runs on every single request — the allocation-free path matters here in
/// a way it would not in a one-off startup log.
/// </para>
/// </remarks>
public sealed class LoggingBehaviour<TMessage, TResponse>(
    ILogger<LoggingBehaviour<TMessage, TResponse>> logger,
    ICurrentUser currentUser,
    ICorrelationContext correlation)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    private const int SlowRequestMilliseconds = 1_500;

    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var useCase = typeof(TMessage).Name;

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["UseCase"] = useCase,
            ["CorrelationId"] = correlation.CorrelationId,
            ["UserId"] = currentUser.UserId,
        });

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next(message, cancellationToken);
            stopwatch.Stop();

            if (response is Result { IsFailure: true } failed)
            {
                UseCaseLog.Failed(
                    logger,
                    failed.Error.Type == ErrorType.External ? LogLevel.Error : LogLevel.Warning,
                    useCase,
                    stopwatch.ElapsedMilliseconds,
                    failed.Error.Code,
                    failed.Error.Description);

                return response;
            }

            if (stopwatch.ElapsedMilliseconds > SlowRequestMilliseconds)
            {
                UseCaseLog.CompletedSlowly(logger, useCase, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                UseCaseLog.Completed(logger, useCase, stopwatch.ElapsedMilliseconds);
            }

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Reaching here means a bug or an infrastructure fault, never an
            // expected business outcome — so it always logs at Error.
            UseCaseLog.Threw(logger, useCase, stopwatch.ElapsedMilliseconds, ex);
            throw;
        }
    }
}

/// <summary>Source-generated, allocation-free log delegates for the pipeline.</summary>
internal static partial class UseCaseLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information,
        Message = "{UseCase} completed in {ElapsedMs}ms")]
    public static partial void Completed(ILogger logger, string useCase, long elapsedMs);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning,
        Message = "{UseCase} completed SLOWLY in {ElapsedMs}ms")]
    public static partial void CompletedSlowly(ILogger logger, string useCase, long elapsedMs);

    [LoggerMessage(EventId = 1002,
        Message = "{UseCase} failed in {ElapsedMs}ms: {ErrorCode} — {ErrorDescription}")]
    public static partial void Failed(
        ILogger logger, LogLevel level, string useCase, long elapsedMs,
        string errorCode, string errorDescription);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Error,
        Message = "{UseCase} threw after {ElapsedMs}ms")]
    public static partial void Threw(ILogger logger, string useCase, long elapsedMs, Exception exception);
}
