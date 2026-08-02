using System.Windows;
using Bakery.Application.Interfaces;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF.Views;

public partial class WasteEntryDialog : Window
{
    private readonly IStockCalculationService _stock;
    private readonly WasteEntryDialogViewModel _vm;

    public WasteEntryDialog(WasteEntryDialogViewModel viewModel, IStockCalculationService stockCalculationService)
    {
        InitializeComponent();
        _vm = viewModel;
        _stock = stockCalculationService;
        DataContext = viewModel;
        TodayLabel.Text = DateTime.Now.ToString("yyyy/MM/dd");
    }

    private async void ItemComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_vm.SelectedItem is { } item)
        {
            var stock = await _stock.GetCurrentStockAsync(item.Id);
            _vm.SetAvailableStock(stock);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.CanSave) return;
        DialogResult = true;
        Close();
    }
}
