using FrubberPOS.Application.Common;
using FrubberPOS.Application.System;
using FrubberPOS.Application.Trial;
using FrubberPOS.Domain.Licensing;
using FrubberPOS.Infrastructure.Licensing;

namespace FrubberPOS.Infrastructure.Persistence.Workstations;

public sealed class Postgres20LicenseGate(
    Postgres20WorkstationStore store,
    EcdsaLicenseSignatureVerifier verifier,
    string serverMachineCode,
    IClock clock) : IOperationalGate
{
    public async Task<Error?> CheckAsync(CancellationToken cancellationToken)
    {
        var state = await store.ReadLicenseAsync(cancellationToken);
        if (state.IsFailure)
        {
            return state.Error;
        }

        var row = state.Value;
        if (!WorkstationLicenseFormat.TryParse(row.SignedPayload, out var license) ||
            license!.Key.MachineCode != serverMachineCode ||
            license.Key.IssuedDate > DateOnly.FromDateTime(clock.UtcNow.UtcDateTime) ||
            !verifier.VerifyCanonical(license.Key.KeyId, license.CanonicalPayload, license.Key.Signature))
        {
            return new Error(ErrorCode.LicenseBlocked, "La licencia del servidor no es válida para este negocio.");
        }

        return row.Slot > 0 && row.Slot <= license.Seats
            ? null
            : new Error(ErrorCode.LicenseBlocked, "La licencia del negocio no autoriza este puesto.");
    }
}
