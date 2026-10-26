using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VrcSetup.Core;

/// <summary>Short, actionable messages for normal users. Full exceptions belong in diagnostics.</summary>
public static class UserErrors
{
    public static bool IsExpected(Exception error) => error is UserInputException or ProjectsInUseException || Advice(error.Message) is not null || error.Message.StartsWith("These packages cannot be installed together: ", StringComparison.Ordinal);
    public static string Message(Exception error, Func<string, string>? packageName = null)
    {
        var replacement = Regex.Match(error.Message, @"^These packages cannot be installed together: ([a-zA-Z0-9._-]+), ([a-zA-Z0-9._-]+)\.");
        if (replacement.Success)
            return string.Format(UiText.Text("{0} replaces {1}. Choose which package to keep before applying changes."),
                packageName?.Invoke(replacement.Groups[1].Value) ?? replacement.Groups[1].Value,
                packageName?.Invoke(replacement.Groups[2].Value) ?? replacement.Groups[2].Value);
        var conflict = Regex.Match(error.Message, @"^Package version conflict: ([a-zA-Z0-9._-]+)@([^;]+); resolved (\S+)\.$");
        if (conflict.Success)
            return string.Format(UiText.Text("{0} was pinned to {1}, but dependencies resolved to {2}. Choose Automatic or compatible package versions, then retry."),
                packageName?.Invoke(conflict.Groups[1].Value) ?? conflict.Groups[1].Value, conflict.Groups[2].Value, conflict.Groups[3].Value);
        var removal = Regex.Match(error.Message, @"Package removal blocked: ([a-zA-Z0-9._-]+)");
        if (removal.Success)
        {
            var id = removal.Groups[1].Value;
            return string.Format(UiText.Text("Cannot remove {0}: another package needs it. Keep it, or remove the dependent package first."), packageName?.Invoke(id) ?? id);
        }
        const string needed = "Package is still required by another package: ";
        if (error.Message.StartsWith(needed, StringComparison.Ordinal))
        {
            var id = error.Message[needed.Length..];
            return string.Format(UiText.Text("{0} is still needed by another package and stays installed."), packageName?.Invoke(id) ?? id);
        }
        var package = Regex.Match(error.Message, @"^(?:Package/version not found: |Package operation failed: |Package repair failed: |Package metadata validation failed: )([a-zA-Z0-9._-]+)");
        if (package.Success)
        {
            var id = package.Groups[1].Value.TrimEnd('.');
            return string.Format(UiText.Text(PackagePresence.IsGenerated(id)
                ? "{0} is generated automatically. Remove it from the preset; its tool will create it when needed."
                : "Could not install {0}. Check that its source is available and its version works with the other packages, then retry."), packageName?.Invoke(id) ?? id);
        }
        return UiText.Text(Advice(error.Message) ?? (error switch
    {
        UserInputException => error.Message,
        ImportQueueInUseException => "Another instance is already managing imports. Return to that window or close it before continuing.",
        InvalidOperationException when error.Message.StartsWith("Package/version not found: ", StringComparison.Ordinal) => "A selected package is unavailable. Check your connection and retry, or remove it from the preset.",
        ProjectsInUseException busy => string.Format(UiText.Text("Close Unity for {0} before applying changes. No project was changed."), string.Join(", ", busy.Projects)),
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden } => "The download service is busy. Please try again later.",
        HttpRequestException => "Could not connect. Check your connection and try again.",
        TimeoutException or TaskCanceledException => "This is taking longer than expected. Please try again.",
        UnauthorizedAccessException => "This folder cannot be changed. Choose another folder or check its permissions.",
        FileNotFoundException or DirectoryNotFoundException => "The file or folder is unavailable. Reconnect the drive or choose it again.",
        JsonException => "This file could not be read. Choose a valid settings or preset file.",
        ArgumentException => "Check the values you entered and try again.",
        IOException when error.Message.StartsWith("Choose Unity ", StringComparison.Ordinal) || error.Message.StartsWith("Select a valid Unity Editor", StringComparison.Ordinal) => "Choose the VRChat version of Unity. You can find it with Find Unity.",
        InvalidOperationException when error.Message == "Choose Unity and a projects folder to continue." => error.Message,
        InvalidOperationException when error.Message == "Imports are in progress." => "Wait for imports to finish before changing the app or its settings.",
        InvalidDataException => "This preset file is not supported. Ask for a new export from VRC Project Setup.",
        _ => "This action could not be completed. Open logs for details."
    }));
    }
    private static string? Advice(string message) => message switch
    {
        "Close Unity for this project before applying changes." or "Close Unity for this project before applying changes. No project was changed." or "Unity opened this project. Close it before applying changes." => "Close Unity for this project before applying changes. No project was changed.",
        "A newer successful change replaced this recovery copy. Use the latest backup in Activity." => message,
        "Choose a different preset name." => "This preset name is already in use. Enter another name.",
        "A path is required." => "Choose a folder to continue.",
        "A package version is required after @." or "Choose a version or latest." => "Choose a package version or Latest version.",
        "Project name contains invalid Windows filename characters." => "Enter a valid name for the project.",
        "Select at least one project." => "Select a project to manage its packages.",
        "Choose the projects folder in Settings first." => "Choose the projects folder in Settings before creating a project.",
        "A file already uses this project name. Choose another name." or "This name is already used by another folder. Choose another project name." or "The destination already exists. Choose another name." => "This name is already used by a file or folder. Enter another project name.",
        "No compatible version is available for every linked SDK component." or "Select the same version for linked VRChat SDK components." or "Select one version policy for linked VRChat SDK components." => "Choose the same version for the VRChat SDK packages.",
        "Project changed after review; skipped. Review again." => "This project changed while the operation was being prepared. Reopen its packages and retry.",
        "The destination is a file. Choose another project name." => "This name is already in use. Choose another project name.",
        "This project is currently open or importing in Unity. Wait for it to finish." or "Another operation is preparing this project. Wait for it to finish." => "This project is in use. Close Unity or wait for the import to finish, then try again.",
        "This folder has no VRCSETUP import state. Open it from Projects or choose another name." => "A project already exists here. Open it from Projects, or choose a new name for this import.",
        "This project was already completed. Existing assets stay unchanged; the same source can add missing assets only." => "This project was already imported. Choose its original package to complete missing files, or use a new project name.",
        "This unfinished project belongs to another source package. Select that file or choose a new project name." => "This project was created from a different package. Choose that package, or use a new project name.",
        "Project creation stopped before Unity initialized it. The partial folder is preserved; review it in Activity before restarting." => "This project did not finish. Open Activity and choose a new copy to start again.",
        "The project state could not be read. Its files have been preserved; inspect the project before continuing." or "The project import state is invalid or newer than this app. Its files have been preserved." or "The unfinished project has no recorded source package. Its files have been preserved." or "The recorded source path is invalid. Its files have been preserved." => "This project cannot be continued automatically. Your files are unchanged. Try a new project name, or contact support.",
        "Two files would create the same project name. Select each file and give it a different name." => "Two packages have the same project name. Rename one before importing.",
        _ => null
    };
}

public sealed class ProjectsInUseException(string[] projects) : IOException("Close Unity before importing additional files into: " + string.Join(", ", projects) + ". No project was changed.")
{
    public string[] Projects { get; } = projects.ToArray();
}
