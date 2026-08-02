using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;

namespace Bakery.WPF.Helpers;


public static class DatePickerHintAssist
{
    public static readonly DependencyProperty ShowHintOnlyWhenEmptyProperty =
        DependencyProperty.RegisterAttached(
            "ShowHintOnlyWhenEmpty",
            typeof(bool),
            typeof(DatePickerHintAssist),
            new PropertyMetadata(false, OnShowHintOnlyWhenEmptyChanged));

    private static readonly DependencyProperty OriginalHintProperty =
        DependencyProperty.RegisterAttached(
            "OriginalHint",
            typeof(object),
            typeof(DatePickerHintAssist),
            new PropertyMetadata(null));

    public static bool GetShowHintOnlyWhenEmpty(DependencyObject element) =>
        (bool)element.GetValue(ShowHintOnlyWhenEmptyProperty);

    public static void SetShowHintOnlyWhenEmpty(DependencyObject element, bool value) =>
        element.SetValue(ShowHintOnlyWhenEmptyProperty, value);

    private static void OnShowHintOnlyWhenEmptyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DatePicker picker)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            picker.Loaded += OnPickerLoaded;
            picker.SelectedDateChanged += OnSelectedDateChanged;
            UpdateHint(picker);
        }
        else
        {
            picker.Loaded -= OnPickerLoaded;
            picker.SelectedDateChanged -= OnSelectedDateChanged;
            RestoreHint(picker);
        }
    }

    private static void OnPickerLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DatePicker picker)
        {
            UpdateHint(picker);
        }
    }

    private static void OnSelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DatePicker picker)
        {
            UpdateHint(picker);
        }
    }

    private static void UpdateHint(DatePicker picker)
    {
        var currentHint = HintAssist.GetHint(picker);
        if (currentHint is not null && picker.GetValue(OriginalHintProperty) is null)
        {
            picker.SetValue(OriginalHintProperty, currentHint);
        }

        if (picker.SelectedDate.HasValue)
        {
            if (currentHint is not null)
            {
                picker.ClearValue(HintAssist.HintProperty);
            }
        }
        else
        {
            RestoreHint(picker);
        }
    }

    private static void RestoreHint(DatePicker picker)
    {
        var originalHint = picker.GetValue(OriginalHintProperty);
        if (originalHint is not null && HintAssist.GetHint(picker) is null)
        {
            HintAssist.SetHint(picker, originalHint);
        }
    }
}
