using FluentValidation;
using WoodHeart.Application.Common.Abstractions;
using WoodHeart.Application.Common.Messaging;
using WoodHeart.Domain.Common;

namespace WoodHeart.Application.Diagnostics;

/// <summary>
/// The walking skeleton: a query that exercises validation, the clock port,
/// a domain value object, and both branches of <see cref="Result{TValue}"/>.
/// </summary>
/// <remarks>
/// Kept permanently as a post-deploy smoke test. It is also the reference
/// example for how every later use case is shaped — request record, validator,
/// handler returning a Result, nothing else.
/// </remarks>
public sealed record EchoQuery(string Message, string? PhoneNumber) : IQuery<EchoResponse>;

public sealed record EchoResponse
{
    public required string Message { get; init; }

    public required DateTimeOffset ReceivedAtUtc { get; init; }

    public required DateTimeOffset ReceivedAtDhaka { get; init; }

    /// <summary>Normalised to E.164 when a phone number was supplied.</summary>
    public string? NormalizedPhone { get; init; }

    /// <summary>Masked form, showing how phone numbers appear in logs.</summary>
    public string? MaskedPhone { get; init; }
}

internal sealed class EchoQueryValidator : AbstractValidator<EchoQuery>
{
    public EchoQueryValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Message is required.")
            .MaximumLength(200).WithMessage("Message cannot exceed 200 characters.");
    }
}

internal sealed class EchoQueryHandler(IDateTimeProvider clock)
    : IQueryHandler<EchoQuery, EchoResponse>
{
    public ValueTask<Result<EchoResponse>> Handle(EchoQuery query, CancellationToken cancellationToken)
    {
        PhoneNumber? phone = null;

        if (!string.IsNullOrWhiteSpace(query.PhoneNumber))
        {
            var parsed = PhoneNumber.Create(query.PhoneNumber);

            // A failed parse returns the failure straight through — this is the
            // Result pattern in miniature, and no exception is involved.
            if (parsed.IsFailure)
            {
                return ValueTask.FromResult(Result.Failure<EchoResponse>(parsed.Error));
            }

            phone = parsed.Value;
        }

        var response = new EchoResponse
        {
            Message = query.Message,
            ReceivedAtUtc = clock.UtcNow,
            ReceivedAtDhaka = clock.DhakaNow,
            NormalizedPhone = phone?.Value,
            MaskedPhone = phone?.Masked
        };

        return ValueTask.FromResult(Result.Success(response));
    }
}
