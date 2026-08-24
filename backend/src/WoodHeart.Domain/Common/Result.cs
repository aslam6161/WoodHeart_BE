namespace WoodHeart.Domain.Common;

/// <summary>
/// The outcome of an operation: success, or a single <see cref="Error"/>.
/// Every command and query handler returns one of these.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        // A successful result carrying an error (or a failure carrying none) is
        // a programming mistake, so it throws rather than returning quietly.
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);

    /// <summary>Returns the first failure in <paramref name="results"/>, or success.</summary>
    public static Result FirstFailureOr(params Result[] results)
    {
        foreach (var result in results)
        {
            if (result.IsFailure)
            {
                return result;
            }
        }

        return Success();
    }
}

/// <summary>A <see cref="Result"/> that carries a value when successful.</summary>
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    protected internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    /// <summary>The value. Throws if the result is a failure — check <see cref="Result.IsSuccess"/> first.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot read the value of a failed result.");

    /// <summary>Implicit lift so handlers can <c>return someValue;</c> directly.</summary>
    public static implicit operator Result<TValue>(TValue value) => Success(value);

    /// <summary>Collapses both branches into a single value — handy in controllers.</summary>
    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<Error, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(Error);
}
