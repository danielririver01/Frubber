namespace FrubberPOS.Application.Licensing;

/// <summary>
/// Verifies offline license signatures (JHO-25). The application carries
/// only public keys (ADR-020 §16.7): unknown key identifiers verify as
/// <c>false</c> instead of throwing, so callers map them to a plain
/// license rejection.
/// </summary>
public interface ILicenseSignatureVerifier
{
    bool Verify(string keyId, string machineCode, DateOnly issuedDate, byte[] signature);
}
