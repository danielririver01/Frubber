namespace FrubberPOS.Application.Common;

/// <summary>
/// Base contract for every use case: one class, one async operation,
/// always flowing a <see cref="CancellationToken"/> end to end.
/// Business failures travel inside <see cref="Result{TResponse}"/>;
/// exceptions are reserved for unexpected technical faults
/// (P-DD-001, P-DD-003, P-DD-005).
/// </summary>
/// <typeparam name="TRequest">Immutable input. Use an explicit empty request when there is none.</typeparam>
/// <typeparam name="TResponse">Specific response. Cannot be nullable.</typeparam>
public interface IUseCase<TRequest, TResponse> where TResponse : notnull
{
    Task<Result<TResponse>> ExecuteAsync(TRequest request, CancellationToken cancellationToken);
}
