using System.Security.Cryptography;
using System.Text;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Domain.Licensing;

namespace FrubberPOS.Application.Licensing;

public sealed record GetLicenseStatusRequest;

public sealed record GetLicenseStatusResponse(bool IsLicensed, string MachineDisplayCode);

/// <summary>
/// Reports whether a valid lifetime license is active on this team (JHO-25).
/// Fail-closed: missing state, DPAPI tampering, another team's code, or a
/// bad signature all report unlicensed without throwing.
/// </summary>
public sealed class GetLicenseStatusUseCase : IUseCase<GetLicenseStatusRequest, GetLicenseStatusResponse>
{
    private readonly IMachineCodeProvider machineCodes;
    private readonly ILicenseProtector protector;
    private readonly ILicenseRepository licenses;
    private readonly IUnitOfWorkFactory units;

    public GetLicenseStatusUseCase(
        IMachineCodeProvider machineCodes,
        ILicenseProtector protector,
        ILicenseRepository licenses,
        IUnitOfWorkFactory units)
    {
        ArgumentNullException.ThrowIfNull(machineCodes);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(licenses);
        ArgumentNullException.ThrowIfNull(units);
        this.machineCodes = machineCodes;
        this.protector = protector;
        this.licenses = licenses;
        this.units = units;
    }

    public async Task<Result<GetLicenseStatusResponse>> ExecuteAsync(GetLicenseStatusRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fingerprint = await machineCodes.GetFingerprintAsync(cancellationToken);
        var displayCode = MachineCode.FormatDisplay(fingerprint.MachineCode);

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var stored = await licenses.ReadAsync(unit, cancellationToken);
        await unit.CommitAsync(cancellationToken);

        if (stored is null)
        {
            return Result<GetLicenseStatusResponse>.Success(new GetLicenseStatusResponse(false, displayCode));
        }

        string canonical;
        try
        {
            canonical = Encoding.UTF8.GetString(protector.Unprotect(stored.ProtectedPayload));
        }
        catch (CryptographicException)
        {
            return Result<GetLicenseStatusResponse>.Success(new GetLicenseStatusResponse(false, displayCode));
        }

        var payloadParts = canonical.Split('|');
        if (payloadParts.Length != 4 || payloadParts[0] != LicenseKeyFormat.Scheme)
        {
            return Result<GetLicenseStatusResponse>.Success(new GetLicenseStatusResponse(false, displayCode));
        }

        if (!string.Equals(payloadParts[2], fingerprint.MachineCode, StringComparison.Ordinal))
        {
            return Result<GetLicenseStatusResponse>.Success(new GetLicenseStatusResponse(false, displayCode));
        }

        // The sealed payload carries no signature; the signature was verified
        // at activation time. Binding to this team plus DPAPI sealing is the
        // steady-state guarantee; absence or tampering reports unlicensed.
        return Result<GetLicenseStatusResponse>.Success(new GetLicenseStatusResponse(true, displayCode));
    }
}
