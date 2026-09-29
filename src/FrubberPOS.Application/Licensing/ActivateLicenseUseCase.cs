using System.Text;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.System;
using FrubberPOS.Domain.Licensing;

namespace FrubberPOS.Application.Licensing;

public sealed record ActivateLicenseRequest(string LicenseText);

public sealed record ActivateLicenseResponse(bool IsLifetime);

/// <summary>
/// Activates the offline lifetime license bound to this team (JHO-25).
/// An invalid key changes nothing: every rejection returns before opening
/// the write unit, so stored state is untouched. A valid key is sealed with
/// DPAPI and persisted as the single LicenseState row.
/// </summary>
public sealed class ActivateLicenseUseCase : IUseCase<ActivateLicenseRequest, ActivateLicenseResponse>
{
    public const int PayloadVersion = 1;

    private const string LicenseField = "LicenseText";

    private readonly IMachineCodeProvider machineCodes;
    private readonly ILicenseSignatureVerifier verifier;
    private readonly ILicenseProtector protector;
    private readonly ILicenseRepository licenses;
    private readonly IUnitOfWorkFactory units;
    private readonly IClock clock;

    public ActivateLicenseUseCase(
        IMachineCodeProvider machineCodes,
        ILicenseSignatureVerifier verifier,
        ILicenseProtector protector,
        ILicenseRepository licenses,
        IUnitOfWorkFactory units,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(machineCodes);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(licenses);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(clock);
        this.machineCodes = machineCodes;
        this.verifier = verifier;
        this.protector = protector;
        this.licenses = licenses;
        this.units = units;
        this.clock = clock;
    }

    public async Task<Result<ActivateLicenseResponse>> ExecuteAsync(ActivateLicenseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!LicenseKeyFormat.TryParse(request.LicenseText, out var parsed) || parsed is null)
        {
            return Result<ActivateLicenseResponse>.Failure(
                new Error(ErrorCode.Validation, "El código de licencia no tiene un formato válido.", LicenseField));
        }

        var fingerprint = await machineCodes.GetFingerprintAsync(cancellationToken);
        if (!string.Equals(parsed.MachineCode, fingerprint.MachineCode, StringComparison.Ordinal))
        {
            return Result<ActivateLicenseResponse>.Failure(
                new Error(ErrorCode.Validation, "Esta licencia fue emitida para otro equipo.", LicenseField));
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        if (parsed.IssuedDate > today.AddDays(1))
        {
            return Result<ActivateLicenseResponse>.Failure(
                new Error(ErrorCode.Validation, "La fecha de emisión de la licencia no es válida.", LicenseField));
        }

        if (!verifier.Verify(parsed.KeyId, parsed.MachineCode, parsed.IssuedDate, parsed.Signature))
        {
            return Result<ActivateLicenseResponse>.Failure(
                new Error(ErrorCode.Validation, "La licencia no es válida para este equipo.", LicenseField));
        }

        var canonical = LicenseKeyFormat.BuildCanonicalPayload(parsed.KeyId, parsed.MachineCode, parsed.IssuedDate);
        var sealedPayload = protector.Protect(Encoding.UTF8.GetBytes(canonical));

        await using var unit = await units.CreateAsync(UnitOfWorkMode.Write, cancellationToken);
        await licenses.SaveAsync(
            unit,
            new StoredLicense(PayloadVersion, sealedPayload, clock.UtcNow.ToUnixTimeMilliseconds()),
            cancellationToken);
        await unit.CommitAsync(cancellationToken);

        return Result<ActivateLicenseResponse>.Success(new ActivateLicenseResponse(true));
    }
}
