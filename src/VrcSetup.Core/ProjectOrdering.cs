namespace VrcSetup.Core;

public static class ProjectOrdering
{
    public static string Cycle(string current, string column)
        => current == column ? column + "-reverse" : current == column + "-reverse" ? "default" : column;

    public static ProjectSnapshot[] Apply(IEnumerable<ProjectSnapshot> projects, string sort)
    {
        var rows = sort switch
        {
            "name" => projects.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "name-reverse" => projects.OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "created" => projects.OrderByDescending(x => x.CreatedUtc),
            "created-reverse" => projects.OrderBy(x => x.CreatedUtc),
            "modified-reverse" => projects.OrderBy(x => x.LastUpdatedUtc),
            "modified" or "recent" => projects.OrderByDescending(x => x.LastUpdatedUtc),
            _ => projects.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
        };
        return rows.ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
