using System.Security.Cryptography;
using System.Text;

namespace FrubberPOS.Infrastructure.Migrations;

/// <summary>
/// SHA-256 (uppercase hex, 64 chars) over the exact script text, matching
/// the <c>Checksum</c> constraint of <c>SchemaMigration</c>. Any byte-level
/// change to an applied script is a different migration and is refused.
/// </summary>
internal static class MigrationChecksum
{
    internal static string Sha256Hex(string sql) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
}
