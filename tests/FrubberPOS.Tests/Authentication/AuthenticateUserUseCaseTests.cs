using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Domain.Users;

namespace FrubberPOS.Tests.Authentication;

public sealed class AuthenticateUserUseCaseTests
{
    [Fact]
    public async Task CorrectPin_SignsInAndReturnsResponse()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var useCase = new AuthenticateUserUseCase(bed.Users, bed.Hasher, bed.Session, bed.Units);

        var result = await useCase.ExecuteAsync(new AuthenticateUserRequest(2, "2222"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.UserId);
        Assert.Equal("Zed Cajero", result.Value.DisplayName);
        Assert.Equal(UserRole.Cashier, result.Value.Role);
        Assert.NotNull(bed.Session.Current);
        Assert.Equal(2, bed.Session.Current.UserId);
    }

    [Fact]
    public async Task WrongPin_FailsWithoutSession()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var useCase = new AuthenticateUserUseCase(bed.Users, bed.Hasher, bed.Session, bed.Units);

        var result = await useCase.ExecuteAsync(new AuthenticateUserRequest(2, "9999"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Authentication, result.Error!.Code);
        Assert.Null(bed.Session.Current);
    }

    [Fact]
    public async Task UnknownInactiveAndWrongPin_ShareOneMessage()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var useCase = new AuthenticateUserUseCase(bed.Users, bed.Hasher, bed.Session, bed.Units);

        var wrong = await useCase.ExecuteAsync(new AuthenticateUserRequest(2, "9999"), CancellationToken.None);
        var unknown = await useCase.ExecuteAsync(new AuthenticateUserRequest(999, "2222"), CancellationToken.None);
        var inactive = await useCase.ExecuteAsync(new AuthenticateUserRequest(4, "4444"), CancellationToken.None);

        Assert.Equal("Usuario o PIN inválidos.", wrong.Error!.Message);
        Assert.Equal(wrong.Error.Message, unknown.Error!.Message);
        Assert.Equal(wrong.Error.Message, inactive.Error!.Message);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData("")]
    public async Task BadFormat_FailsValidation(string pin)
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var useCase = new AuthenticateUserUseCase(bed.Users, bed.Hasher, bed.Session, bed.Units);

        var result = await useCase.ExecuteAsync(new AuthenticateUserRequest(2, pin), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Equal("Pin", result.Error.Field);
        Assert.Null(bed.Session.Current);
    }

    [Fact]
    public async Task FailureMessage_ContainsNoSecrets()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var useCase = new AuthenticateUserUseCase(bed.Users, bed.Hasher, bed.Session, bed.Units);

        var result = await useCase.ExecuteAsync(new AuthenticateUserRequest(2, "1234"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain("1234", result.Error!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hash", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("salt", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
