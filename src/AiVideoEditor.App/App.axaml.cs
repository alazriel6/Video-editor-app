using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AiVideoEditor.App.ViewModels;
using AiVideoEditor.App.Views;

namespace AiVideoEditor.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
            desktop.Exit += (_, _) => vm.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
