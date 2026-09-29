using System.Text.RegularExpressions;
using FrubberPOS.Infrastructure.Migrations;

namespace FrubberPOS.Tests.Infrastructure.Migrations;

public sealed class MigrationScriptsTests
{
    [Fact]
    public void GetInitialScript_ReturnsApprovedContent()
    {
        var script = MigrationScripts.GetInitialScript();

        Assert.False(string.IsNullOrWhiteSpace(script));
        Assert.Contains("-- Migración: 001_Initial.sql", script, StringComparison.Ordinal);

        string[] tables =
        [
            "SchemaMigration", "SystemSequence", "\"User\"", "Product",
            "CashSession", "Sale", "SaleLine", "SaleVoid",
            "BusinessSetting", "LicenseState"
        ];
        foreach (var table in tables)
        {
            Assert.Contains($"CREATE TABLE {table}", script, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GetInitialScript_UsesStrictTables()
    {
        var script = MigrationScripts.GetInitialScript();

        Assert.Equal(10, Regex.Matches(script, @"\) STRICT;").Count);
    }

    [Fact]
    public void GetInitialScript_HasNoRealType()
    {
        var script = MigrationScripts.GetInitialScript();

        Assert.DoesNotMatch(@"\bREAL\b", script);
    }

    [Theory]
    [InlineData("PriceCop INTEGER")]
    [InlineData("StockMilli INTEGER")]
    [InlineData("QuantityMilli INTEGER")]
    [InlineData("SubtotalCop INTEGER")]
    [InlineData("TotalCop INTEGER")]
    [InlineData("DiscountCop INTEGER")]
    public void GetInitialScript_StoresMoneyAndQuantitiesAsIntegers(string declaration)
    {
        var script = MigrationScripts.GetInitialScript();

        Assert.Contains(declaration, script, StringComparison.Ordinal);
    }

    [Fact]
    public void GetInitialScript_HasNoTransactionControl()
    {
        // BEGIN inside CREATE TRIGGER bodies is legitimate trigger syntax,
        // not transaction control. Only standalone transaction statements
        // (which the executor owns) are forbidden.
        var script = MigrationScripts.GetInitialScript();

        Assert.DoesNotMatch(@"(?im)^\s*BEGIN\s*;", script);
        Assert.DoesNotMatch(@"(?im)^\s*BEGIN\s+(TRANSACTION|DEFERRED|IMMEDIATE|EXCLUSIVE)\b", script);
        Assert.DoesNotMatch(@"(?im)^\s*COMMIT\s*;?\s*$", script);
    }
}
