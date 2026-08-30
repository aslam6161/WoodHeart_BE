using System.Reflection;
using Microsoft.Extensions.Hosting;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Service.DTOs.Common;
using WoodHeart.Service.Interfaces.Common;

namespace WoodHeart.Service.Services.Common;

/// <inheritdoc />
public class DiagnosticsService(IDateTimeProvider clock, IHostEnvironment environment) : IDiagnosticsService
{
    public PingResponseDto Ping() => new()
    {
        Status = "ok",
        Environment = environment.EnvironmentName,
        UtcNow = clock.UtcNow,
        DhakaNow = clock.DhakaNow,
        Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown"
    };

    public GeneralResponse<EchoResponseDto> Echo(EchoRequestDto request)
    {
        var response = new EchoResponseDto
        {
            Message = request.Message,
            ReceivedAtUtc = clock.UtcNow,
            ReceivedAtDhaka = clock.DhakaNow
        };

        // An absent phone number is not an error — phone is optional here, as it
        // is in several places across the app, so "missing" must not mean
        // "invalid".
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return GeneralResponse<EchoResponseDto>.Success(response);
        }

        // A malformed number IS an error, and it comes back as a value rather
        // than an exception, so the customer gets a 400 with a message they can
        // act on instead of a 500.
        if (!PhoneNumber.TryParse(request.PhoneNumber, out var phone))
        {
            return GeneralResponse<EchoResponseDto>.Fail(
                PhoneNumber.InvalidCode, PhoneNumber.InvalidMessage);
        }

        response.NormalizedPhone = phone!.Value;
        response.MaskedPhone = phone.Masked;

        return GeneralResponse<EchoResponseDto>.Success(response);
    }
}
