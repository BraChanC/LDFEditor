using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LdfEditor.Controls;
using LdfEditor.Models;
using LdfEditor.ViewModels;

namespace LdfEditor;

public partial class MainWindow : Window
{
    public const string ScheduleDropFormat = "LdfEditor.ScheduleDropPayload";

    private Point _dragStart;
    private object? _pendingDragPayload;
    private bool _dragging;

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

    /* ---- Vector Pane2: drag Frames/Commands → Schedule Tables ---- */

    private void OnNavTreePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pendingDragPayload = null;
        _dragging = false;
        _dragStart = e.GetPosition(null);
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not { } tvi)
            return;
        if (tvi.DataContext is TreeItemViewModel { Payload: { } payload }
            && (Vm.CanDropOntoSchedule(payload) || Vm.CanDragForAssociation(payload)))
            _pendingDragPayload = payload;
    }

    private void OnNavTreePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pendingDragPayload == null || _dragging)
            return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        StartScheduleDrag(_pendingDragPayload);
    }

    private void OnFrameListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pendingDragPayload = null;
        _dragging = false;
        _dragStart = e.GetPosition(null);
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not { } row)
            return;
        if (row.Item is FrameListRow { Payload: { } payload }
            && (Vm.CanDropOntoSchedule(payload) || Vm.CanDragForAssociation(payload)))
            _pendingDragPayload = payload;
    }

    private void OnFrameListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pendingDragPayload == null || _dragging)
            return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        StartScheduleDrag(_pendingDragPayload);
    }

    private void StartScheduleDrag(object payload)
    {
        _dragging = true;
        var data = new DataObject(ScheduleDropFormat, payload);
        DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
        _pendingDragPayload = null;
        _dragging = false;
    }

    private void OnScheduleTreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ScheduleRow row && !ReferenceEquals(Vm.SelectedScheduleRow, row))
            Vm.SelectedScheduleRow = row;
    }

    /* ---- Vector: drag Unconditional Frame → Event-Triggered / Sporadic ---- */

    private void OnFramesTreeDragOver(object sender, DragEventArgs e)
    {
        if (!TryGetScheduleDropPayload(e.Data, out var payload) || payload is not LdfFrame)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var target = TreeItemUnder(sender as DependencyObject, e.GetPosition((IInputElement)sender));
        var targetPayload = target?.Payload;
        if (targetPayload is LdfEventTriggeredFrame or LdfSporadicFrame
            && Vm.CanDropOntoAssociationTarget(targetPayload, payload, out _))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        /* Show Copy even when validation fails so Drop can show the Vector warning. */
        if (targetPayload is LdfEventTriggeredFrame or LdfSporadicFrame)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFramesTreeDrop(object sender, DragEventArgs e)
    {
        if (!TryGetScheduleDropPayload(e.Data, out var payload) || payload is not LdfFrame frame)
        {
            e.Handled = true;
            return;
        }

        var target = TreeItemUnder(sender as DependencyObject, e.GetPosition((IInputElement)sender));
        if (target?.Payload is LdfEventTriggeredFrame or LdfSporadicFrame)
            Vm.TryAssociateFrame(target.Payload, frame);
        e.Handled = true;
    }

    private TreeItemViewModel? TreeItemUnder(DependencyObject? root, Point pos)
    {
        if (root is not Visual visual) return null;
        var hit = VisualTreeHelper.HitTest(visual, pos);
        if (hit == null) return null;
        var tvi = FindAncestor<TreeViewItem>(hit.VisualHit);
        return tvi?.DataContext as TreeItemViewModel;
    }

    private void OnScheduleDragOver(object sender, DragEventArgs e)
    {
        if (!TryGetScheduleDropPayload(e.Data, out var payload) || !Vm.CanDropOntoSchedule(payload))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var row = ScheduleRowUnder(e.GetPosition(ScheduleGrid));
        if (!Vm.ResolveScheduleDropTarget(row, out _, out _))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnScheduleDragLeave(object sender, DragEventArgs e)
    {
        e.Handled = true;
    }

    private void OnScheduleDrop(object sender, DragEventArgs e)
    {
        if (!TryGetScheduleDropPayload(e.Data, out var payload))
        {
            e.Handled = true;
            return;
        }

        var pos = e.GetPosition(ScheduleGrid);
        var row = ScheduleRowUnder(pos) ?? Vm.SelectedScheduleRow;
        if (!Vm.ResolveScheduleDropTarget(row, out var si, out var insertAt))
        {
            e.Handled = true;
            return;
        }

        Vm.TryDropOntoSchedule(payload!, si, insertAt);
        e.Handled = true;
    }

    private static bool TryGetScheduleDropPayload(IDataObject data, out object? payload)
    {
        payload = null;
        if (data.GetDataPresent(ScheduleDropFormat))
        {
            payload = data.GetData(ScheduleDropFormat);
            return payload != null;
        }
        return false;
    }

    private ScheduleRow? ScheduleRowUnder(Point posInTree)
    {
        var hit = VisualTreeHelper.HitTest(ScheduleGrid, posInTree);
        if (hit == null) return null;
        var tvi = FindAncestor<TreeViewItem>(hit.VisualHit);
        return tvi?.DataContext as ScheduleRow;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ScheduleDropFormat))
        {
            /* Let ScheduleGrid handle; elsewhere show none. */
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ScheduleDropFormat))
        {
            e.Handled = true;
            return;
        }
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var ldf = files.FirstOrDefault(f => f.EndsWith(".ldf", StringComparison.OrdinalIgnoreCase));
            if (ldf != null) Vm.LoadPath(ldf);
        }
    }
}
