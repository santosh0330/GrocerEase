namespace GrocerEase.Application.Common;

/// <summary>
/// The outcome of a use case: either a value or an <see cref="ResultError"/>, never both.
/// Handlers return it for failures a caller should expect; unexpected failures stay exceptions.
/// </summary>
public sealed class Result<T> : IResult
{
    private readonly T? _value;
    private readonly ResultError? _error;

    // Created through the non-generic Result class, so a Result is always one of the two states.
    internal Result(T value)
    {
        IsSuccess = true;
        _value = value;
    }

    internal Result(ResultError error)
    {
        IsSuccess = false;
        _error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>The value. Throws if this is a failure; prefer <see cref="Match{TResult}"/>.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    /// <summary>The error. Throws if this is a success; prefer <see cref="Match{TResult}"/>.</summary>
    public ResultError Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("A successful result has no error.");

    /// <summary>Runs exactly one of the two functions, so a caller cannot forget the failure case.</summary>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<ResultError, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess(_value!) : onFailure(_error!);
    }
}

/// <summary>Creates <see cref="Result{T}"/> values. A separate non-generic class lets the compiler infer <c>T</c>: <c>Result.Success(user)</c>.</summary>
public static class Result
{
    public static Result<T> Success<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>(value);
    }

    public static Result<T> Failure<T>(ResultError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(error);
    }
}
