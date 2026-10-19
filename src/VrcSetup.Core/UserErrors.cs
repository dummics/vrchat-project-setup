using System.Net;
using System.Text.Json;

namespace VrcSetup.Core;

/// <summary>Short, actionable messages for normal users. Full exceptions belong in diagnostics.</summary>
public static class UserErrors
{
    public static string Message(Exception error) => UiText.Text(Advice(error.Message) ?? (error switch
    {
        ProjectsInUseException busy => string.Format(UiText.Text("Close Unity to import additional files into {0}. No project was changed."), string.Join(", ", busy.Projects)),
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
    private static string? Advice(string message) => message switch
    {
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
