namespace Raffa.SharedKernel;

/// <summary>
/// Discriminated result type. Domain services return <see cref="Result{T}"/> instead of
/// throwing exceptions for expected failures.
/// </summary>
public sealed class Result<T>
{
    private readonly T? _value;
    private readonly string? _error;

    private Result(T value) { _value = value; IsSuccess = true; }
    private Result(string error) { _error = error; IsSuccess = false; }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on a failed result.");

    public string Error => !IsSuccess
        ? _error!
        : throw new InvalidOperationException("Cannot access Error on a successful result.");

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(string error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);
}
