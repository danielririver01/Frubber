using FrubberPOS.Application.Common;
using FrubberPOS.Domain.Licensing;

namespace FrubberPOS.Application.Licensing;

public sealed record GetMachineCodeRequest;

public sealed record GetMachineCodeResponse(string DisplayCode, IReadOnlyList<string> RedactedIdentifiers);

/// <summary>
/// Returns the copyable team code shown on the activation screen (JHO-25).
/// Shows redacted hardware tokens only; full serials never reach the UI.
/// </summary>
public sealed class GetMachineCodeUseCase : IUseCase<GetMachineCodeRequest, GetMachineCodeResponse>
{
    private readonly IMachineCodeProvider machineCodes;

    public GetMachineCodeUseCase(IMachineCodeProvider machineCodes)
    {
        ArgumentNullException.ThrowIfNull(machineCodes);
        this.machineCodes = machineCodes;
    }

    public async Task<Result<GetMachineCodeResponse>> ExecuteAsync(GetMachineCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fingerprint = await machineCodes.GetFingerprintAsync(cancellationToken);
        return Result<GetMachineCodeResponse>.Success(
            new GetMachineCodeResponse(MachineCode.FormatDisplay(fingerprint.MachineCode), fingerprint.RedactedIdentifiers));
    }
}
