namespace VrcSetup.Core;

// A phase has its own measured denominator. Total == 0 means genuinely unknown work.
public sealed record SetupProgress(string Phase, long Completed, long Total)
{
    public int Percent => Total > 0 ? (int)Math.Clamp(Completed * 100d / Total, 0, 100) : 0;
}

public sealed record SetupMessage(string Kind, string Phase = "", long Completed = 0, long Total = 0,
    string? Text = null, bool StartMenu = true, bool Desktop = false, bool Launch = true, bool Accepted = false);
