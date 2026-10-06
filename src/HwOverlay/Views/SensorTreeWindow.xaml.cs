using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HwOverlay.ViewModels;

namespace HwOverlay.Views;

public partial class SensorTreeWindow : Window
{
    private readonly SensorTreeViewModel _vm;

    public SensorTreeWindow(SensorTreeViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    // TreeView.SelectedItem não é bindável: repassa a seleção para o ViewModel.
    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        _vm.SelectedNode = e.NewValue as TreeNodeViewModel;

    private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Só reage se o duplo clique foi sobre um item de sensor (não no expansor de um grupo).
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is SensorNodeViewModel sensor)
        {
            _vm.AddToOverlay(sensor);
            e.Handled = true;
        }
    }

    /// <summary>Configurações › Personalização › Barra de tarefas (onde se fixam os ícones da bandeja).</summary>
    private void OpenTaskbarSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true });
        }
        catch
        {
            // sem o app Configurações (raro): o texto do cartão já explica o caminho
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
