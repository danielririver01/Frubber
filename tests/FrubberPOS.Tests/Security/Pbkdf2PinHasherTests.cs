using FrubberPOS.Infrastructure.Security;

namespace FrubberPOS.Tests.Security;

public sealed class Pbkdf2PinHasherTests
{
    [Fact]
    public void HashThenVerify_Succeeds()
    {
        var hasher = new Pbkdf2PinHasher();

        var derived = hasher.HashPin("1234");

        Assert.Equal(16, derived.Salt.Length);
        Assert.Equal(32, derived.Hash.Length);
        Assert.Equal(1, derived.AlgorithmVersion);
        Assert.True(derived.Iterations > 0);
        Assert.True(hasher.VerifyPin("1234", derived.Salt, derived.Hash, derived.Iterations, derived.AlgorithmVersion));
    }

    [Fact]
    public void WrongPin_Fails()
    {
        var hasher = new Pbkdf2PinHasher();
        var derived = hasher.HashPin("1234");

        Assert.False(hasher.VerifyPin("4321", derived.Salt, derived.Hash, derived.Iterations, derived.AlgorithmVersion));
    }

    [Fact]
    public void DistinctSalts_ProduceDistinctHashes()
    {
        var hasher = new Pbkdf2PinHasher();

        var first = hasher.HashPin("1234");
        var second = hasher.HashPin("1234");

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnknownAlgorithmVersion_FailsClosed(int version)
    {
        var hasher = new Pbkdf2PinHasher();
        var derived = hasher.HashPin("1234");

        Assert.False(hasher.VerifyPin("1234", derived.Salt, derived.Hash, derived.Iterations, version));
    }

    [Fact]
    public void TamperedMaterial_Fails()
    {
        var hasher = new Pbkdf2PinHasher();
        var derived = hasher.HashPin("1234");
        var tamperedHash = (byte[])derived.Hash.Clone();
        tamperedHash[0] ^= 0xFF;

        Assert.False(hasher.VerifyPin("1234", derived.Salt, tamperedHash, derived.Iterations, derived.AlgorithmVersion));
        Assert.False(hasher.VerifyPin("1234", new byte[16], derived.Hash, derived.Iterations, derived.AlgorithmVersion));
        Assert.False(hasher.VerifyPin("1234", derived.Salt, derived.Hash, 0, derived.AlgorithmVersion));
    }
}
