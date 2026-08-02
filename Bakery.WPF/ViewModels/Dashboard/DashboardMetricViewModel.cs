namespace Bakery.WPF.ViewModels;

public sealed record DashboardMetricViewModel(
    string Title,
    string Value,
    string IconKind,
    string IconColor = "#8B5E3C",
    string IconBackground = "#F7EEE8",
    bool IsEnabled = true,
    string? Subtitle = null);
