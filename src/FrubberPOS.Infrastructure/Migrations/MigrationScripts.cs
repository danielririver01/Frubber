namespace FrubberPOS.Infrastructure.Migrations;

/// <summary>
/// Access to embedded migration scripts. Scripts are immutable approved
/// artifacts (JHO-19): never edited, never given BEGIN/COMMIT (the JHO-20
/// executor opens the transaction). Versioning and execution belong
/// to JHO-20.
/// </summary>
public static class MigrationScripts
{
    public const string InitialScriptResourceName = "FrubberPOS.Infrastructure.Migrations.001_Initial.sql";

    public const string TrialScriptResourceName = "FrubberPOS.Infrastructure.Migrations.002_Trial.sql";

    public const string OpeningBalanceCorrectionResourceName = "FrubberPOS.Infrastructure.Migrations.003_OpeningBalanceCorrection.sql";

    public static string GetInitialScript()
    {
        using var stream = typeof(MigrationScripts).Assembly.GetManifestResourceStream(InitialScriptResourceName)
            ?? throw new InvalidOperationException($"Recurso embebido no encontrado: {InitialScriptResourceName}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string GetTrialScript()
    {
        using var stream = typeof(MigrationScripts).Assembly.GetManifestResourceStream(TrialScriptResourceName)
            ?? throw new InvalidOperationException($"Recurso embebido no encontrado: {TrialScriptResourceName}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string GetOpeningBalanceCorrectionScript()
    {
        using var stream = typeof(MigrationScripts).Assembly.GetManifestResourceStream(OpeningBalanceCorrectionResourceName)
            ?? throw new InvalidOperationException($"Recurso embebido no encontrado: {OpeningBalanceCorrectionResourceName}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
