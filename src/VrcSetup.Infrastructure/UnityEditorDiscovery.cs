using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace VrcSetup.Infrastructure;

public static class UnityEditorDiscovery
{
    public const string PinnedVersion = "2022.3.22f1";
    public static string? ReadVersion(string path)
    {
        try
        {
            if (!Path.GetFileName(path).Equals("Unity.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
            var version = FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "";
            var match = Regex.Match(version, @"^\d+\.\d+\.\d+[abfp]\d+");
            return match.Success ? match.Value : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
    public static bool IsPinned(string? path) => !string.IsNullOrWhiteSpace(path) && ReadVersion(path) == PinnedVersion;
    public static (string Version, string Path)[] FindEditors() => Inspect(Candidates(), ReadVersion);
    public static (string Version, string Path)[] Inspect(IEnumerable<string> candidates, Func<string, string?> readVersion) => candidates
        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(x => (Version: readVersion(x), Path: x)).Where(x => x.Version is not null)
        .Select(x => (Version: x.Version!, x.Path)).OrderByDescending(x => x.Version == PinnedVersion).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    private static IEnumerable<string> Candidates()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                if (Path.GetFileName(path).Equals("Unity.exe", StringComparison.OrdinalIgnoreCase)) candidates.Add(Path.GetFullPath(path));
                else
                {
                    candidates.Add(Path.Combine(path, "Editor", "Unity.exe"));
                    candidates.Add(Path.Combine(path, "Unity.exe"));
                    if (Directory.Exists(path))
                        foreach (var folder in Directory.EnumerateDirectories(path).Take(100)) candidates.Add(Path.Combine(folder, "Editor", "Unity.exe"));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity"));
        var hub = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnityHub");
        foreach (var name in new[] { "installedEditorBinaryMetadata.json", "secondaryInstallPath.json" })
        {
            try
            {
                var file = Path.Combine(hub, name);
                if (!File.Exists(file) || new FileInfo(file).Length > 2 * 1024 * 1024) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.ValueKind == JsonValueKind.String) Add(doc.RootElement.GetString());
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var property in doc.RootElement.EnumerateObject()) Add(property.Name);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        }
        if (OperatingSystem.IsWindows())
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    using var apps = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    foreach (var key in apps?.GetSubKeyNames() ?? [])
                    {
                        using var app = apps!.OpenSubKey(key);
                        var name = app?.GetValue("DisplayName") as string ?? "";
                        if (!name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) || name.Contains("Hub", StringComparison.OrdinalIgnoreCase)) continue;
                        Add(app?.GetValue("InstallLocation") as string);
                        Add((app?.GetValue("DisplayIcon") as string)?.Split(',')[0]);
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
        }
        return candidates;
    }
}
