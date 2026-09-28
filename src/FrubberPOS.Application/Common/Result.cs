namespace FrubberPOS.Application.Common;

/// <summary>
/// Operation outcome: success or a typed <see cref="Error"/>. Never both.
/// Base class for outcomes without a value; use <see cref="Result{T}"/>
/// when the operation produces one (P-DD-003).
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null)
        {
            throw new ArgumentException("Un resultado exitoso no puede contener un error.", nameof(error));
        }

        if (!isSuccess && error is null)
        {
            throw new ArgumentNullException(nameof(error), "Un resultado fallido requiere un error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error? Error { get; }

    public static Result Success() => new(true, null);

    public static Result Failure(Error error) => new(false, error);

    public static Result<T> Success<T>(T value) where T : notnull => Result<T>.Success(value);

    public static Result<T> Failure<T>(Error error) where T : notnull => Result<T>.Failure(error);
}

/// <summary>
/// Operation outcome carrying a value on success.
/// The value is never null on success and never readable on failure.
/// </summary>
/// <typeparam name="T">Response type. Cannot be nullable.</typeparam>
public sealed class Result<T> : Result where T : notnull
{
    private readonly T? value;

    private Result(T value)
        : base(isSuccess: true, error: null)
    {
        this.value = value ?? throw new ArgumentNullException(nameof(value), "Un resultado exitoso requiere un valor.");
    }

    private Result(Error error)
        : base(isSuccess: false, error: error)
    {
    }

    public T Value => IsSuccess
        ? value!
        : throw new InvalidOperationException("No se puede leer el valor de un resultado fallido.");

    public static Result<T> Success(T value) => new(value);

    public static new Result<T> Failure(Error error) => new(error);
}
