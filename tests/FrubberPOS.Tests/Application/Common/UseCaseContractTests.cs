using System.Reflection;
using FrubberPOS.Application.Common;

namespace FrubberPOS.Tests.Application.Common;

public sealed class UseCaseContractTests
{
    private sealed record EchoRequest(string Text);

    private sealed record EchoResponse(string Text);

    private sealed class EchoUseCase : IUseCase<EchoRequest, EchoResponse>
    {
        public async Task<Result<EchoResponse>> ExecuteAsync(EchoRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(10, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return Result<EchoResponse>.Success(new EchoResponse(request.Text));
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSuccessResult()
    {
        IUseCase<EchoRequest, EchoResponse> useCase = new EchoUseCase();

        var result = await useCase.ExecuteAsync(new EchoRequest("hola"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("hola", result.Value.Text);
    }

    [Fact]
    public async Task ExecuteAsync_HonorsCancellationToken()
    {
        IUseCase<EchoRequest, EchoResponse> useCase = new EchoUseCase();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync(new EchoRequest("hola"), canceled.Token));
    }

    [Fact]
    public void Contract_ExecuteAsyncHasRequestAndCancellationToken()
    {
        var method = typeof(IUseCase<EchoRequest, EchoResponse>).GetMethod(nameof(IUseCase<EchoRequest, EchoResponse>.ExecuteAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Result<EchoResponse>>), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(EchoRequest), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }
}
