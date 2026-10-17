namespace VrcSetup.Core;

public sealed record PackageSelectionPreview(string Id, string Version, bool Selected, DependencySuggestion? Evidence);
