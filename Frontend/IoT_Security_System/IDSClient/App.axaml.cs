using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using IDSClient.ViewModels;
using IDSClient.Views; // <--- THIS WAS MISSING

namespace IDSClient
{
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
                // Now it works because we added 'using IDSClient.Views'
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainViewModel()
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}