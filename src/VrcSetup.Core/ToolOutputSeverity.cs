using System.Text.RegularExpressions;

namespace VrcSetup.Core;

public static class ToolOutputSeverity
{
    public static EventSeverity Classify(string line)
    {
        // vrc-get and Unity prefix warning lines even when their description contains "error".
        if (Regex.IsMatch(line, @"(?i)^\s*(w:|\[[^\]]*\b(?:WARN|WARNING|WRN)\b[^\]]*\])")) return EventSeverity.Warning;
        // Unity logs the Bee command line while starting its compiler. Its
        // --continue-on-failure option is not itself a failure report.
        if (Regex.IsMatch(line, @"(?i)^\s*Starting:\s+.*[\\/]bee_backend\.exe(?:\s|$)")) return EventSeverity.Info;
        if (Regex.IsMatch(line, @"(?i)^\s*e:|\b(ERR|ERROR|FAILED|FAILURE)\b")) return EventSeverity.Error;
        if (Regex.IsMatch(line, @"(?i)\b(WRN|WARN|WARNING)\b")) return EventSeverity.Warning;
        return EventSeverity.Info;
    }
}
