using System.Diagnostics;
using System.Windows;
using IntunePackagingTool.Services;

namespace IntunePackagingTool
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Initialize theme service
            ThemeService.Instance.Initialize();

            // Set up global exception handling
            this.DispatcherUnhandledException += (sender, ex) =>
            {
                // Log full exception details for debugging
                Debug.WriteLine($"Unhandled exception: {ex.Exception}");

                // Show generic message to user (don't expose internal details)
                MessageBox.Show(
                    "An unexpected error occurred. Please try again or contact support if the problem persists.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                ex.Handled = true;
            };
        }
    }
}