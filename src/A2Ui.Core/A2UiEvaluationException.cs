namespace Blazejewicz.A2Ui.Core;

/// <summary>
/// Thrown when an evaluation or execution error occurs (e.g. invalid function call, @index outside collection scope).
/// </summary>
public sealed class A2UiEvaluationException : Exception
{
    /// <summary>Gets the optional function call identifier associated with this evaluation error.</summary>
    public string? FunctionCallId { get; init; }

    /// <summary>Gets the protocol error code associated with this evaluation error (defaults to <c>"EXECUTION_ERROR"</c>).</summary>
    public string ErrorCode { get; init; } = "EXECUTION_ERROR";

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

    /// <summary>Initializes a new instance of the <see cref="A2UiEvaluationException"/> class with a specified error message, function call identifier, and optional inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="functionCallId">The optional function call identifier associated with this error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or <see langword="null"/>.</param>
    public A2UiEvaluationException(string message, string? functionCallId, Exception? innerException = null)
        : base(message, innerException)
    {
        this.FunctionCallId = functionCallId;
    }
}
