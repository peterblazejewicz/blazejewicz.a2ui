namespace Blazejewicz.A2Ui.Core;

/// <summary>
/// Thrown when catalog lookup, registration, or resolution constraints fail.
/// </summary>
public sealed class A2UiCatalogException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="A2UiCatalogException"/> class.</summary>
    public A2UiCatalogException() { }

    /// <summary>Initializes a new instance of the <see cref="A2UiCatalogException"/> class with a specified error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public A2UiCatalogException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="A2UiCatalogException"/> class with a specified error message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public A2UiCatalogException(string message, Exception innerException)
        : base(message, innerException) { }
}
