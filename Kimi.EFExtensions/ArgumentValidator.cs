namespace Kimi.EFExtensions;

using System.Runtime.CompilerServices;

public static class ArgumentValidator
{
    /// <summary>
    /// Validates that the given object is not null.
    /// </summary>
    /// <param name="argument">The argument to validate.</param>
    /// <param name="message">Optional custom error message.</param>
    /// <param name="argumentExpression">Automatically captures the argument name.</param>
    public static void NotNull(
        object? argument,
        string? message = null,
        [CallerArgumentExpression("argument")] string? argumentExpression = null)
    {
        if (argument == null)
        {
            throw new ArgumentNullException(argumentExpression, message ?? $"{argumentExpression} cannot be null.");
        }
    }

    /// <summary>
    /// Validates that the given string is not null, empty, or whitespace.
    /// </summary>
    /// <param name="argument">The string argument to validate.</param>
    /// <param name="message">Optional custom error message.</param>
    /// <param name="argumentExpression">Automatically captures the argument name.</param>
    public static void NotNullOrWhiteSpace(
        string? argument,
        string? message = null,
        [CallerArgumentExpression("argument")] string? argumentExpression = null)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            throw new ArgumentException(message ?? $"{argumentExpression} cannot be null, empty, or whitespace.", argumentExpression);
        }
    }

    /// <summary>
    /// Validates that the given number is greater than zero.
    /// </summary>
    /// <param name="argument">The numeric argument to validate.</param>
    /// <param name="message">Optional custom error message.</param>
    /// <param name="argumentExpression">Automatically captures the argument name.</param>
    public static void GreaterThanZero(
        int argument,
        string? message = null,
        [CallerArgumentExpression("argument")] string? argumentExpression = null)
    {
        if (argument <= 0)
        {
            throw new ArgumentOutOfRangeException(argumentExpression, message ?? $"{argumentExpression} must be greater than zero.");
        }
    }

    /// <summary>
    /// Validates that the given number is greater than or equal to one.
    /// </summary>
    /// <param name="argument">The numeric argument to validate.</param>
    /// <param name="message">Optional custom error message.</param>
    /// <param name="argumentExpression">Automatically captures the argument name.</param>
    public static void GreaterThanOrEqualToOne(
        int argument,
        string? message = null,
        [CallerArgumentExpression("argument")] string? argumentExpression = null)
    {
        if (argument < 1)
        {
            throw new ArgumentOutOfRangeException(argumentExpression, message ?? $"{argumentExpression} must be greater than or equal to one.");
        }
    }
}
