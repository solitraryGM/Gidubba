using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TableForge;

/// <summary>About Gidubba (see <see cref="ViewModels.AboutViewModel"/>).</summary>
public partial class AboutWindow : Window
{
    public AboutWindow() => InitializeComponent();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
