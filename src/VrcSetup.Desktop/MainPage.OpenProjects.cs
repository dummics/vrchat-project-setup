using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop;

public sealed partial class MainPage
{
    private DispatcherQueueTimer? openProjectTimer;
    private bool readingOpenProjects;
    private HashSet<string> openProjectPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<FontIcon> projectOpenIndicators = [];

    private void InitializeOpenProjectIndicators()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(4);
        timer.Tick += async (_, _) => await RefreshOpenProjectIndicatorsAsync();
        openProjectTimer = timer;
        Unloaded += (_, _) => timer.Stop();
    }

    private void SetOpenProjectTracking(bool visible)
    {
        if (openProjectTimer is not { } timer) return;
        if (!visible)
        {
            timer.Stop();
            return;
        }
        if (timer.IsRunning) return;
        timer.Start();
        _ = RefreshOpenProjectIndicatorsAsync();
    }

    private async Task RefreshOpenProjectIndicatorsAsync()
    {
        if (readingOpenProjects) return;
        readingOpenProjects = true;
        try
        {
            var paths = await Task.Run(ProjectWorkspace.GetInteractiveUnityProjectPaths);
            if (openProjectTimer?.IsRunning == true) ApplyOpenProjectPaths(paths);
        }
        catch (Exception error) when (error is System.Management.ManagementException or System.Runtime.InteropServices.COMException
            or UnauthorizedAccessException)
        {
            // WMI can be unavailable or access restricted. This indicator is informational.
            ApplyOpenProjectPaths(new(StringComparer.OrdinalIgnoreCase));
        }
        finally { readingOpenProjects = false; }
    }

    internal void ApplyOpenProjectPaths(HashSet<string> paths)
    {
        openProjectPaths = paths;
        foreach (var indicator in projectOpenIndicators) UpdateProjectOpenIndicator(indicator);
    }

    private void ProjectOpenIndicatorLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FontIcon indicator) return;
        projectOpenIndicators.Add(indicator);
        UpdateProjectOpenIndicator(indicator);
    }

    private void ProjectOpenIndicatorUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is FontIcon indicator) projectOpenIndicators.Remove(indicator);
    }

    private void ProjectOpenIndicatorContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is FontIcon indicator) UpdateProjectOpenIndicator(indicator);
    }

    private void UpdateProjectOpenIndicator(FontIcon indicator)
        => indicator.Visibility = indicator.DataContext is ProjectSnapshot project && openProjectPaths.Contains(project.Path)
            ? Visibility.Visible
            : Visibility.Collapsed;
}
