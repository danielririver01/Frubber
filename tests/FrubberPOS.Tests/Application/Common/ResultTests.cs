using FrubberPOS.Application.Common;

namespace FrubberPOS.Tests.Application.Common;

public sealed class ResultTests
{
    [Fact]
    public void Success_WithValue_IsSuccessAndExposesValue()
    {
        var result = Result<string>.Success("V-000001");

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
        Assert.Equal("V-000001", result.Value);
    }

    [Fact]
    public void Success_WithNullValue_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Result<string>.Success(null!));
    }

    [Fact]
    public void Failure_WithError_IsFailureAndExposesError()
    {
        var error = new Error(ErrorCode.NotFound, "La venta no existe.");
        var result = Result<string>.Failure(error);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void Failure_WithNullError_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Result<string>.Failure(null!));
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
    }

    [Fact]
    public void Failure_ReadingValue_ThrowsInvalidOperationException()
    {
        var result = Result<string>.Failure(new Error(ErrorCode.Validation, "Cantidad inválida."));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Success_HasNoError()
    {
        var result = Result<int>.Success(370000);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Equal(370000, result.Value);
    }

    [Fact]
    public void NonGeneric_SuccessAndFailure_RoundTrip()
    {
        var success = Result.Success();
        var failure = Result.Failure(new Error(ErrorCode.Conflict, "La caja ya está abierta."));

        Assert.True(success.IsSuccess);
        Assert.Null(success.Error);
        Assert.True(failure.IsFailure);
        Assert.Equal(ErrorCode.Conflict, failure.Error!.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Error_RequiresNonEmptyMessage(string? message)
    {
        Assert.Throws<ArgumentException>(() => new Error(ErrorCode.Validation, message!));
    }

    [Fact]
    public void Error_KeepsCodeMessageAndField()
    {
        var error = new Error(ErrorCode.Validation, "El descuento supera el subtotal.", "Discount");

        Assert.Equal(ErrorCode.Validation, error.Code);
        Assert.Equal("El descuento supera el subtotal.", error.Message);
        Assert.Equal("Discount", error.Field);
    }

    [Fact]
    public void AllDocumentedErrorCodes_Exist()
    {
        var expected = new[]
        {
            ErrorCode.Validation,
            ErrorCode.Authentication,
            ErrorCode.Authorization,
            ErrorCode.NotFound,
            ErrorCode.Conflict,
            ErrorCode.LicenseBlocked,
            ErrorCode.Storage,
            ErrorCode.Printing,
            ErrorCode.Backup,
            ErrorCode.Unexpected
        };

        Assert.Equal(expected, Enum.GetValues<ErrorCode>());
    }
}
