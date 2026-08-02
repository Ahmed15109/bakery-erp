using System.Windows;
using Bakery.Application.Interfaces;
using Bakery.WPF.Services;

namespace Bakery.WPF.Views;

public partial class OwnerResetCodeDialog : Window
{
    private readonly OwnerResetCodeAttemptSession _attemptSession;

    public OwnerResetCodeDialog(IOwnerResetCodeVerifier verifier)
    {
        InitializeComponent();
        _attemptSession = new OwnerResetCodeAttemptSession(verifier);
        Loaded += (_, _) => OwnerCodeBox.Focus();
        Closed += (_, _) => OwnerCodeBox.Clear();
    }

    public IOwnerResetAuthorization? Authorization { get; private set; }

    private void Verify_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Authorization = _attemptSession.TryAuthorize(OwnerCodeBox.Password);
            if (Authorization is not null)
            {
                DialogResult = true;
                Close();
                return;
            }

            if (_attemptSession.IsLocked)
            {
                MessageBox.Show(
                    "تم إغلاق جلسة التحقق بعد خمس محاولات غير صحيحة.",
                    "Bakery ERP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                DialogResult = false;
                Close();
                return;
            }

            AttemptMessage.Text = $"الرمز غير صحيح. المحاولات المتبقية: {_attemptSession.RemainingAttempts}.";
        }
        finally
        {
            OwnerCodeBox.Clear();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        OwnerCodeBox.Clear();
        DialogResult = false;
        Close();
    }
}
