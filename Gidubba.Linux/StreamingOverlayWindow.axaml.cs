using Avalonia.Controls;
using Avalonia.Interactivity;
using TableForge.ViewModels;

namespace TableForge;

public partial class StreamingOverlayWindow : Window
{
    public StreamingOverlayWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as StreamingOverlayViewModel)?.Detach();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
