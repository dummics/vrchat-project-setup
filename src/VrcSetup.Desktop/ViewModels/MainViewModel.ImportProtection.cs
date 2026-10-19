using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private bool RequireImportsIdle()
    {
        if (!HasActiveImports) return true;
        ErrorMessage = UiText.Text("Wait for imports to finish before changing the app or its settings.");
        Status = ErrorMessage; return false;
    }
}
