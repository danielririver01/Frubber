using FrubberPOS.Domain.Licensing;

namespace FrubberPOS.Tests.Licensing;

/// <summary>
/// Proves the machine code is stable, 16 uppercase hex chars, and display-grouped.
/// </summary>
public sealed class MachineCodeTests
{
    [Fact]
    public void Compute_IsStableAndHex()
    {
        var first = MachineCode.Compute(["uuid-1", "board-1", "disk-1"]);
        var second = MachineCode.Compute(["uuid-1", "board-1", "disk-1"]);

        Assert.Equal(first, second);
        Assert.Equal(16, first.Length);
        Assert.Matches("^[0-9A-F]{16}$", first);
    }

    [Fact]
    public void Compute_ChangesWhenIdentifiersChange()
    {
        Assert.NotEqual(MachineCode.Compute(["uuid-1"]), MachineCode.Compute(["uuid-2"]));
    }

    [Fact]
    public void Compute_IgnoresBlankEntries()
    {
        Assert.Equal(MachineCode.Compute(["uuid-1"]), MachineCode.Compute(["  ", "uuid-1", ""]));
    }

    [Fact]
    public void Compute_WithoutIdentifiers_Throws()
    {
        Assert.Throws<ArgumentException>(() => MachineCode.Compute([]));
        Assert.Throws<ArgumentException>(() => MachineCode.Compute(["  "]));
    }

    [Fact]
    public void FormatDisplay_GroupsInFours()
    {
        Assert.Equal("0123-4567-89AB-CDEF", MachineCode.FormatDisplay("0123456789ABCDEF"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123")]
    [InlineData("0123456789ABCDEFF")]
    public void FormatDisplay_RejectsBadShape(string? code)
    {
        Assert.Throws<ArgumentException>(() => MachineCode.FormatDisplay(code!));
    }
}
