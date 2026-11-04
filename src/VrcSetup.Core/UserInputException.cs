namespace VrcSetup.Core;

// Expected validation is useful feedback, not an unexpected application failure.
public sealed class UserInputException(string field, string message) : ArgumentException(message)
{
    public string Field { get; } = field;
}
