using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Comet.Views;

public sealed partial class MainWindow
{
    private async void AboutMenuItem_Click(object sender, RoutedEventArgs args)
    {
        var assembly = typeof(MainWindow).Assembly;
        var version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(version))
        {
            version = assembly.GetName().Version?.ToString() ?? "Unknown";
        }

        var metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            version = version[..metadataIndex];
        }

        AboutVersionTextBlock.Text = version;
        AboutDotNetTextBlock.Text = Environment.Version.ToString();
        AboutRuntimeTextBlock.Text = RuntimeInformation.FrameworkDescription;
        AboutOperatingSystemTextBlock.Text = RuntimeInformation.OSDescription;

        await AboutDialog.ShowAsync();
    }
}
