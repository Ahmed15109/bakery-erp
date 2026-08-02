using System.Windows;
using System.Windows.Controls;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF.Views;

public partial class InvoiceWorkspaceView : UserControl
{
    private InvoiceWorkspaceViewModel? _viewModel;

    public InvoiceWorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += (s, e) =>
        {
            if (_viewModel != null)
            {
                _viewModel.RequestFocus -= OnRequestFocus;
            }

            if (e.NewValue is InvoiceWorkspaceViewModel vm)
            {
                _viewModel = vm;
                _viewModel.RequestFocus += OnRequestFocus;
            }
            else
            {
                _viewModel = null;
            }
        };
        Unloaded += (_, _) =>
        {
            if (_viewModel != null)
            {
                _viewModel.RequestFocus -= OnRequestFocus;
            }
        };
    }

    private void OnRequestFocus(string name)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (FindName(name) is not UIElement element) return;

            element.Focus();
            if (element is TextBox textBox)
            {
                textBox.SelectAll();
            }
        });
    }
}
