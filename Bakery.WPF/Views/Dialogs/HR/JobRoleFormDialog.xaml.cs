using Bakery.WPF.ViewModels;
using System.Windows;

namespace Bakery.WPF.Views;

public partial class JobRoleFormDialog : Window
{
    public JobRoleFormDialog(JobRoleFormViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
