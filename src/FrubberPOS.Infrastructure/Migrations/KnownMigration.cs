namespace FrubberPOS.Infrastructure.Migrations;

/// <summary>
/// One immutable migration known to the application. Scripts come from
/// embedded approved artifacts (see <see cref="MigrationScripts"/>);
/// version and name must match their <c>NNN_Name.sql</c> file.
/// </summary>
public sealed record KnownMigration
{
    public KnownMigration(int version, string scriptName, string sql)
    {
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "La versión debe ser positiva.");
        }

        if (string.IsNullOrWhiteSpace(scriptName))
        {
            throw new ArgumentException("El nombre del script no puede estar vacío.", nameof(scriptName));
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("El SQL no puede estar vacío.", nameof(sql));
        }

        Version = version;
        ScriptName = scriptName;
        Sql = sql;
    }

    public int Version { get; }

    public string ScriptName { get; }

    public string Sql { get; }
}
