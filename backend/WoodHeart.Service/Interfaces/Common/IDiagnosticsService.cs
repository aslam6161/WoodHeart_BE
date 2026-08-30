using WoodHeart.Repository;
using WoodHeart.Service.DTOs.Common;

namespace WoodHeart.Service.Interfaces.Common;

/// <summary>
/// The reference example for how every service in this codebase is shaped: take
/// a DTO, do the work, return a <see cref="GeneralResponse{T}"/>.
/// </summary>
public interface IDiagnosticsService
{
    PingResponseDto Ping();

    GeneralResponse<EchoResponseDto> Echo(EchoRequestDto request);
}
