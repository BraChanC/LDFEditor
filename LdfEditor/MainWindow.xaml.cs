using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LdfEditor.Controls;
using LdfEditor.Models;
using LdfEditor.ViewModels;

namespace LdfEditor;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Vm.RecentFilesChanged += RebuildRecentFilesMenu;
        RebuildRecentFilesMenu();

        if (FindName("MainBitLayout") is LinBitLayoutControl layout)
            layout.SignalActivated += idx => Vm.OpenSignalEditorAt(idx);
    }

    private void RebuildRecentFilesMenu()
    {
        /* Remove previously injected recent items (between the two separators). */
        var items = FileMenu.Items;
        int top = items.IndexOf(RecentSepTop);
        int bottom = items.IndexOf(RecentSepBottom);
        if (top < 0 || bottom < 0 || bottom <= top) return;
        for (int i = bottom - 1; i > top; i--)
            items.RemoveAt(i);

        bottom = items.IndexOf(RecentSepBottom);
        RecentSepTop.Visibility = Vm.RecentFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentSepBottom.Visibility = Vm.RecentFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        int insertAt = top + 1;
        foreach (var recent in Vm.RecentFiles)
        {
            var mi = new MenuItem
            {
                Header = recent.Display,
                Command = Vm.OpenRecentCommand,
                CommandParameter = recent.Path,
                ToolTip = recent.Path
            };
            items.Insert(insertAt++, mi);
        }
    }

    /// <summary>Vector editToolStripMenuItem_DropDownOpening — refresh Edit/Delete enable.</summary>
    private void OnEditMenuOpening(object sender, RoutedEventArgs e) =>
        Vm.NotifyEditMenuOpening();

    /// <summary>Vector Export NCF submenu: one entry per slave + Export All.</summary>
    private void OnExportNcfOpening(object sender, RoutedEventArgs e)
    {
        var menu = ExportNcfMenu;
        /* Keep last item = Export All Nodes; rebuild slave entries before it. */
        while (menu.Items.Count > 1)
            menu.Items.RemoveAt(0);

        int insertAt = 0;
        foreach (var slave in Vm.ExportableSlaveNodes)
        {
            menu.Items.Insert(insertAt++, new MenuItem
            {
                Header = $"Export _{slave}...",
                Command = Vm.ExportNcfCommand,
                CommandParameter = slave
            });
        }
    }

    private void OnTreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeItemViewModel item)
            Vm.SelectedTreeItem = item;
    }

    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.SelectedTreeItem?.Payload is LdfFrame f)
        {
            Vm.OpenFrameEditor(f);
            e.Handled = true;
        }
        else if (Vm.SelectedTreeItem?.Payload is LdfSignal s)
        {
            Vm.OpenSignalEditor(s);
            e.Handled = true;
        }
    }

    private void OnFrameSignalGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Vm.OpenSelectedSignalEditor();
        e.Handled = true;
    }

    private void OnFrameListGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.SelectedFrameListRow?.Payload is LdfFrame f)
        {
            Vm.OpenFrameEditor(f);
            e.Handled = true;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var ldf = files.FirstOrDefault(f => f.EndsWith(".ldf", StringComparison.OrdinalIgnoreCase));
            if (ldf != null) Vm.LoadPath(ldf);
        }
    }
}
