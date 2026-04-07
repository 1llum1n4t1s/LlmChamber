using Microsoft.UI.Xaml;

namespace Sample.Maui.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
    }

    protected override Microsoft.Maui.Hosting.MauiApp CreateMauiApp() => Sample.Maui.MauiProgram.CreateMauiApp();
}
