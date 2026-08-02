using System.Windows;

namespace Bakery.WPF.Views;

public partial class InputDialog : Window
{
    public string TitleText { get; set; } = "مدخل بيانات";
    public string Prompt { get; set; } = "يرجى الإدخال:";
    public string InputValue { get; set; } = "";

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        Prompt = prompt;
        InputValue = defaultValue;
        DataContext = this;
        InputTextBox.Focus();
        InputTextBox.SelectAll();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
