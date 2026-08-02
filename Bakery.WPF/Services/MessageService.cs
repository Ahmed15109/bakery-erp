using System.Windows;

namespace Bakery.WPF.Services;

public sealed class MessageService : IMessageService
{
    public void ShowInfo(string message)
    {
        MessageBox.Show(message, "Bakery ERP", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ShowError(string message)
    {
        MessageBox.Show(message, "Bakery ERP", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public bool Confirm(string message)
    {
        return MessageBox.Show(message, "Bakery ERP", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public async Task<string?> ShowInputAsync(string title, string prompt, string defaultValue = "")
    {
        return await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new Views.InputDialog(title, prompt, defaultValue);
            dialog.Owner = System.Windows.Application.Current.MainWindow;
            if (dialog.ShowDialog() == true)
            {
                return dialog.InputValue;
            }
            return null;
        });
    }
}
