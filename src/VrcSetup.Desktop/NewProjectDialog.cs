using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcSetup.Core;
using VrcSetup_Desktop.ViewModels;

namespace VrcSetup_Desktop;

internal sealed class NewProjectDialog
{
    public TextBox Name { get; } = new() { Header = UiText.Text("Project name"), MinWidth = 320 };
    public ComboBox Preset { get; } = new() { Header = UiText.Text("Preset"), HorizontalAlignment = HorizontalAlignment.Stretch };
    public ComboBox Platform { get; } = new() { Header = UiText.Text("Platform"), HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemsSource = new[] { new DisplayOption("StandaloneWindows64", "Windows"), new DisplayOption("Android", "Android / Quest"), new DisplayOption("iOS", "iOS") }, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = "StandaloneWindows64" };
    public ContentDialog Dialog { get; }
    public NewProjectDialog(MainViewModel vm, DataTemplate presetTemplate)
    {
        Preset.ItemsSource = vm.Presets; Preset.ItemTemplate = presetTemplate; Preset.SelectedItem = vm.EditingPreset;
        var path = new TextBlock { Text = vm.ProjectsRoot, FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxWidth = 400, IsTextSelectionEnabled = true };
        Name.TextChanged += (_, _) => path.Text = System.IO.Path.Combine(vm.ProjectsRoot, Name.Text);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 400, Visibility = Visibility.Collapsed };
        var panel = new StackPanel { Spacing = 14 }; panel.Children.Add(Name); panel.Children.Add(path); panel.Children.Add(Platform); panel.Children.Add(Preset); panel.Children.Add(error);
        Dialog = new ContentDialog { Title = UiText.Text("New project"), Content = panel, PrimaryButtonText = UiText.Text("Create"), CloseButtonText = UiText.Text("Cancel"), DefaultButton = ContentDialogButton.Primary };
        Dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                ProjectRules.ValidateName(Name.Text.Trim());
                var target = System.IO.Path.Combine(vm.ProjectsRoot, Name.Text.Trim());
                if (System.IO.Directory.Exists(target) || System.IO.File.Exists(target)) throw new System.IO.IOException(UiText.Text("The destination already exists. Choose another name."));
            }
            catch (Exception problem) when (problem is ArgumentException or System.IO.IOException)
            { args.Cancel = true; error.Text = UiText.Text(problem.Message); error.Visibility = Visibility.Visible; }
        };
        Dialog.Opened += (_, _) => Name.Focus(FocusState.Programmatic);
    }
}
