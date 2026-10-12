namespace FrubberPOS.Infrastructure.Migrations;

/// <summary>
/// Ordered catalog of migrations known to this build. New approved scripts
/// are appended here with their file version and name; existing entries
/// are never edited (ADR-008).
/// </summary>
public static class MigrationCatalog
{
    public static IReadOnlyList<KnownMigration> Default { get; } =
    [
        new KnownMigration(1, "001_Initial.sql", MigrationScripts.GetInitialScript()),
        new KnownMigration(2, "002_Trial.sql", MigrationScripts.GetTrialScript()),
        new KnownMigration(3, "003_OpeningBalanceCorrection.sql", MigrationScripts.GetOpeningBalanceCorrectionScript()),
        new KnownMigration(4, "004_Scale.sql", MigrationScripts.GetScaleScript())
    ];
}
