using System.Windows.Controls;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF.Views;

public partial class ProductionOrdersView : UserControl
{
    public ProductionOrdersView()
    {
        InitializeComponent();
        DataContextChanged += (s, e) =>
        {
            if (e.NewValue is ProductionOrderViewModel vm)
            {
                vm.RequestFocus += OnRequestFocus;
            }
        };
    }

    private void OnRequestFocus(string name)
    {
        if (FindName(name) is Control control)
        {
            control.Focus();
            if (control is TextBox tb) tb.SelectAll();
        }
    }

    private void SuggestionsList_OnPreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ProductionOrderViewModel vm)
        {
            // The selection has already changed due to the click
            vm.ProcessCodeEntryCommand.Execute(null);
        }
    }
}
