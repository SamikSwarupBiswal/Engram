namespace Engram.Store.Validation;

/// <summary>
/// Thrown when input validation fails. Carries the field name and reason.
/// </summary>
public class EngramValidationException : Exception
{
    public string Field { get; }

    public EngramValidationException(string field, string message)
        : base($"Validation failed for '{field}': {message}")
    {
        Field = field;
    }
}
