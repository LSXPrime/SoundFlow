namespace SoundFlow.Structs;

/// <summary>
/// Represents the outcome of an operation, which can be either a success or a failure.
/// </summary>
public readonly struct Result
{
    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }
    
    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;
    
    /// <summary>
    /// Gets the error details if the operation failed, otherwise <c>null</c>.
    /// </summary>
    public IError? Error { get; }

    private Result(bool isSuccess, IError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>
    /// Creates a successful result instance.
    /// </summary>
    /// <returns>A successful <see cref="Result"/>.</returns>
    public static Result Ok() => new(true, null);
    
    /// <summary>
    /// Creates a failed result instance with the specified error.
    /// </summary>
    /// <param name="error">The error details.</param>
    /// <returns>A failed <see cref="Result"/>.</returns>
    public static Result Fail(IError error) => new(false, error);
    
    /// <summary>
    /// Implicitly converts an <see cref="Error"/> object into a failed <see cref="Result"/>.
    /// </summary>
    /// <param name="error">The error to convert.</param>
    public static implicit operator Result(Error error) => Fail(error);
}

/// <summary>
/// Represents the outcome of an operation that returns a value on success.
/// </summary>
/// <typeparam name="T">The type of the value returned on success.</typeparam>
public readonly struct Result<T>
{
    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }
    
    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;
    
    /// <summary>
    /// Gets the successful value if the operation succeeded, otherwise the default value of <typeparamref name="T"/>.
    /// </summary>
    public T? Value { get; }
    
    /// <summary>
    /// Gets the error details if the operation failed, otherwise <c>null</c>.
    /// </summary>
    public IError? Error { get; }

    private Result(bool isSuccess, T? value, IError? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    /// <summary>
    /// Creates a successful result instance with the specified value.
    /// </summary>
    /// <param name="value">The successful value.</param>
    /// <returns>A successful <see cref="Result{T}"/>.</returns>
    public static Result<T> Ok(T value) => new(true, value, null);
    
    /// <summary>
    /// Creates a failed result instance with the specified error.
    /// </summary>
    /// <param name="error">The error details.</param>
    /// <returns>A failed <see cref="Result{T}"/>.</returns>
    public static Result<T> Fail(IError error) => new(false, default, error);

    /// <summary>
    /// Implicitly converts a value of type <typeparamref name="T"/> into a successful <see cref="Result{T}"/>.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    public static implicit operator Result<T>(T value) => Ok(value);
    
    /// <summary>
    /// Implicitly converts an <see cref="Error"/> object into a failed <see cref="Result{T}"/>.
    /// </summary>
    /// <param name="error">The error to convert.</param>
    public static implicit operator Result<T>(Error error) => Fail(error);
    
    /// <summary>
    /// Implicitly converts a <see cref="Result{T}"/> into a non-generic <see cref="Result"/>.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    public static implicit operator Result(Result<T> result) => result.IsSuccess ? Result.Ok() : Result.Fail(result.Error!);
}