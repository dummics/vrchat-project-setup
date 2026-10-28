using System.Text.RegularExpressions;

namespace VrcSetup.Core;

public static class ToolOutputSeverity
{
    public static EventSeverity Classify(string line)
    {
        // vrc-get and Unity prefix warning lines even when their description contains "error".
        if (Regex.IsMatch(line, @"(?i)^\s*(w:|\[[^\]]*\b(?:WARN|WARNING|WRN)\b[^\]]*\])")) return EventSeverity.Warning;
        if (Regex.IsMatch(line, @"(?i)^\s*e:|\b(ERR|ERROR|FAILED|FAILURE)\b")) return EventSeverity.Error;
        if (Regex.IsMatch(line, @"(?i)\b(WRN|WARN|WARNING)\b")) return EventSeverity.Warning;
        return EventSeverity.Info;
    }
}
