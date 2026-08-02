namespace Bakery.WPF.Services;

public interface IMessageService
{
    void ShowInfo(string message);
    void ShowError(string message);
    bool Confirm(string message);
    Task<string?> ShowInputAsync(string title, string prompt, string defaultValue = "");
}
