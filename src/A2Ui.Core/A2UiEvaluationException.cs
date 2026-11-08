namespace Blazejewicz.A2Ui.Core;

/// <summary>
/// Thrown when an evaluation or execution error occurs (e.g. invalid function call, @index outside collection scope).
/// </summary>
public sealed class A2UiEvaluationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="A2UiEvaluationException"/> class.</summary>
    public A2UiEvaluationException() { }

    /// <summary>Initializes a new instance of the <see cref="A2UiEvaluationException"/> class with a specified error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public A2UiEvaluationException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="A2UiEvaluationException"/> class with a specified error message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public A2UiEvaluationException(string message, Exception innerException)
        : base(message, innerException) { }
}
