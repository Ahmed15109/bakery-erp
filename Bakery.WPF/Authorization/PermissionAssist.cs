using System.Windows;
using Bakery.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Bakery.WPF.Authorization;

public static class PermissionAssist
{
    private static readonly DependencyProperty AuthorizationHandlerProperty =
        DependencyProperty.RegisterAttached(
            "AuthorizationHandler",
            typeof(EventHandler),
            typeof(PermissionAssist),
            new PropertyMetadata(null));

    public static readonly DependencyProperty RequiredProperty =
        DependencyProperty.RegisterAttached(
            "Required",
            typeof(string),
            typeof(PermissionAssist),
            new PropertyMetadata(null, OnRequiredChanged));

    public static string? GetRequired(DependencyObject obj) => (string?)obj.GetValue(RequiredProperty);

    public static void SetRequired(DependencyObject obj, string? value) => obj.SetValue(RequiredProperty, value);

    private static void OnRequiredChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        if (element.IsLoaded)
        {
            Apply(element);
            Subscribe(element);
        }
        else
        {
            element.Loaded -= OnLoaded;
            element.Loaded += OnLoaded;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.Loaded -= OnLoaded;
            Apply(element);
            Subscribe(element);
            element.Unloaded -= OnUnloaded;
            element.Unloaded += OnUnloaded;
        }
    }

    private static void Subscribe(FrameworkElement element)
    {
        if (element.GetValue(AuthorizationHandlerProperty) is EventHandler)
        {
            return;
        }

        var session = (System.Windows.Application.Current as App)?.Services.GetService<IUserSessionService>();
        if (session is null) return;
        EventHandler handler = (_, _) => element.Dispatcher.Invoke(() => Apply(element));
        element.SetValue(AuthorizationHandlerProperty, handler);
        session.AuthorizationChanged += handler;
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.GetValue(AuthorizationHandlerProperty) is not EventHandler handler)
        {
            return;
        }
        var session = (System.Windows.Application.Current as App)?.Services.GetService<IUserSessionService>();
        if (session is not null)
        {
            session.AuthorizationChanged -= handler;
        }
        element.ClearValue(AuthorizationHandlerProperty);
        element.Unloaded -= OnUnloaded;
    }

    private static void Apply(FrameworkElement element)
    {
        var required = GetRequired(element);
        if (string.IsNullOrWhiteSpace(required))
        {
            return;
        }

        var session = (System.Windows.Application.Current as App)?.Services.GetService<IUserSessionService>();
        if (session is null)
        {
            return;
        }

        var allowed = required
            .Split([',', ';', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(session.HasPermission);

        element.SetCurrentValue(UIElement.IsEnabledProperty, allowed);
    }
}
