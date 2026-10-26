using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LdfEditor.Models;
using LdfEditor.Services;
using LdfEditor.Views;
using Microsoft.Win32;

namespace LdfEditor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly LdfAntlrLoader _loader = new();
    private readonly RecentFilesStore _recentStore = new();
    private LdfDatabase? _db;
    private string _documentText = "";
    private bool _suppressSigSync;

    [ObservableProperty] private string _title = "LIN Explorer";
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _schedulePropTitle = "Schedule Properties";
    [ObservableProperty] private string _objectPropTitle = "Properties";
    [ObservableProperty] private string _detailTitle = "Nodes";
    [ObservableProperty] private bool _isFrameDetailMode;
    [ObservableProperty] private bool _isNodesDetailMode = true;
    [ObservableProperty] private bool _isFrameListDetailMode;
    [ObservableProperty] private int _detailTabIndex; /* 0=List, 1=Layout */
    [ObservableProperty] private TreeItemViewModel? _selectedTreeItem;
    [ObservableProperty] private ScheduleRow? _selectedScheduleRow;
    [ObservableProperty] private NodeRow? _selectedNodeRow;
    [ObservableProperty] private FrameListRow? _selectedFrameListRow;
    [ObservableProperty] private FrameSignalRow? _selectedFrameSignal;
    [ObservableProperty] private int _selectedSignalIndex = -1;
    [ObservableProperty] private LdfFrame? _detailFrame;
    [ObservableProperty] private LdfDatabase? _database;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _hasDocument;
    /// <summary>Vector tsbHexDec — GlobalNumberVisualizationMode Hex/Dec.</summary>
    [ObservableProperty] private bool _isHexMode = true;

    /// <summary>Vector tsbHexDec caption: "hex" when hex mode, "dec" when decimal.</summary>
    public string HexModeLabel => IsHexMode ? "hex" : "dec";

    public ObservableCollection<TreeItemViewModel> FramesTreeRoots { get; } = new();
    public ObservableCollection<TreeItemViewModel> NodesTreeRoots { get; } = new();
    public ObservableCollection<ScheduleRow> ScheduleRows { get; } = new();
    public ObservableCollection<NodeRow> NodeRows { get; } = new();
    public ObservableCollection<FrameListRow> FrameListRows { get; } = new();
    public ObservableCollection<FrameSignalRow> FrameSignals { get; } = new();
    public ObservableCollection<PropNode> ScheduleProps { get; } = new();
    public ObservableCollection<PropNode> BusProps { get; } = new();
    public ObservableCollection<RecentFileItem> RecentFiles { get; } = new();

    public event Action? RecentFilesChanged;

    private bool _suppressNodeSync;
    private bool _suppressFrameListSync;
    private bool _applyingIndex0;

    public MainViewModel()
    {
        ReloadRecentFiles();
    }

    partial void OnSelectedTreeItemChanged(TreeItemViewModel? value)
    {
        FillTreeSelection(value);
        NotifyEditCommands();
    }

    partial void OnSelectedScheduleRowChanged(ScheduleRow? value)
    {
        FillScheduleSelection(value);
        NotifyEditCommands();
    }

    partial void OnSelectedNodeRowChanged(NodeRow? value)
    {
        if (!_suppressNodeSync)
            FillNodeSelection(value);
        NotifyEditCommands();
    }

    partial void OnSelectedFrameListRowChanged(FrameListRow? value)
    {
        if (!_suppressFrameListSync)
            FillFrameListSelection(value);
        NotifyEditCommands();
    }

    partial void OnIsDirtyChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
    }
    partial void OnHasDocumentChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        SaveAllCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
        RunConsistencyCheckerCommand.NotifyCanExecuteChanged();
        NotifyEditCommands();
    }
    partial void OnFilePathChanged(string value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        SaveAllCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFrameSignalChanged(FrameSignalRow? value)
    {
        NotifyEditCommands();
        if (_suppressSigSync) return;
        if (value == null)
        {
            _suppressSigSync = true;
            SelectedSignalIndex = -1;
            _suppressSigSync = false;
            if (_db != null && DetailFrame != null)
                LdfPropertyBuilder.FillFrame(_db, DetailFrame, BusProps);
            return;
        }
        if (_db == null || DetailFrame == null) return;
        _suppressSigSync = true;
        SelectedSignalIndex = value.Index;
        _suppressSigSync = false;
        var sg = _db.FindSignal(value.Name);
        if (sg != null)
            LdfPropertyBuilder.FillSignal(_db, sg, BusProps, value.StartBit, DetailFrame.Name);
    }

    partial void OnSelectedSignalIndexChanged(int value)
    {
        if (_suppressSigSync) return;
        if (value < 0)
        {
            if (SelectedFrameSignal != null)
            {
                _suppressSigSync = true;
                SelectedFrameSignal = null;
                _suppressSigSync = false;
            }
            return;
        }
        var row = FrameSignals.FirstOrDefault(r => r.Index == value);
        if (row != null && !ReferenceEquals(SelectedFrameSignal, row))
        {
            _suppressSigSync = true;
            SelectedFrameSignal = row;
            _suppressSigSync = false;
        }
        if (_db != null && DetailFrame != null && value < DetailFrame.Signals.Count)
        {
            var fs = DetailFrame.Signals[value];
            var sg = _db.FindSignal(fs.Name);
            if (sg != null)
                LdfPropertyBuilder.FillSignal(_db, sg, BusProps, fs.StartBit, DetailFrame.Name);
        }
    }

    /// <summary>Vector ContinueAfterSaveOnDirtyCheck / AskUserSaveCurrentFile.</summary>
    private bool ContinueAfterSaveOnDirtyCheck()
    {
        if (!IsDirty || !HasDocument) return true;
        var r = MessageBox.Show("Save current file ?", "LIN Explorer",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes)
            return DoSave(promptIfNoPath: true);
        return true;
    }

    /// <summary>
    /// Vector NewFile(LinProtocol) → CreateNewLdf(protocol, askForSave:true):
    /// dirty-check, then DefaultModelCreator in-memory model (no Save dialog until Save).
    /// </summary>
    [RelayCommand]
    private void NewFile(string? dialect)
    {
        if (!ContinueAfterSaveOnDirtyCheck()) return;
        var db = LdfDefaultModelCreator.Create(dialect ?? "2.2");
        LoadDatabase(db, path: null);
    }

    [RelayCommand]
    private void Open()
    {
        if (!ContinueAfterSaveOnDirtyCheck()) return;
        var dlg = new OpenFileDialog { Filter = "LIN Description (*.ldf)|*.ldf|All (*.*)|*.*" };
        if (dlg.ShowDialog() == true) LoadPath(dlg.FileName);
    }

    [RelayCommand]
    private void OpenRecent(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!File.Exists(path))
        {
            MessageBox.Show($"File not found:\n{path}", "Recent Files",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ReloadRecentFiles();
            return;
        }
        if (string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase) && !IsDirty)
            return;
        if (!ContinueAfterSaveOnDirtyCheck()) return;
        LoadPath(path);
    }

    /* Vector SaveFile: untitled → Save As dialog; with path → overwrite. */
    private bool CanSave() => HasDocument && _db != null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => DoSave(promptIfNoPath: string.IsNullOrEmpty(FilePath));

    private bool CanSaveAs() => HasDocument && _db != null;

    [RelayCommand(CanExecute = nameof(CanSaveAs))]
    private void SaveAs() => DoSave(promptIfNoPath: true);

    /// <summary>Vector SaveFile / SaveFileAs / DoFileSave — serialize model via writer.</summary>
    private bool DoSave(bool promptIfNoPath)
    {
        if (_db == null) return false;
        var path = FilePath;
        if (promptIfNoPath || string.IsNullOrEmpty(path))
        {
            var dlg = new SaveFileDialog
            {
                Filter = "LIN Description (*.ldf)|*.ldf|All (*.*)|*.*",
                FileName = string.IsNullOrEmpty(path) ? "Untitled.ldf" : Path.GetFileName(path),
                Title = "Save File As"
            };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try
        {
            _documentText = LdfWriter.Write(_db);
            File.WriteAllText(path, _documentText);
            FilePath = path;
            Title = $"LIN Explorer — {Path.GetFileName(FilePath)}";
            IsDirty = false;
            PushRecent(FilePath);
            StatusText = $"Saved {FilePath}";
            SaveCommand.NotifyCanExecuteChanged();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private bool CanReload() => HasDocument && !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);

    [RelayCommand(CanExecute = nameof(CanReload))]
    private void Reload()
    {
        if (!CanReload()) return;
        if (IsDirty)
        {
            /* Vector ReloadFileAndRejectChanges */
            var r = MessageBox.Show(
                "Do you want to reload the file (your current modifications will be rejected)!?",
                "LIN Explorer", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }
        else
        {
            var r = MessageBox.Show("Do you want to reload the file?", "LIN Explorer",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }
        LoadPath(FilePath);
    }

    [RelayCommand]
    private void Exit()
    {
        if (!ContinueAfterSaveOnDirtyCheck()) return;
        Application.Current.Shutdown();
    }

    private bool CanMutateDocument() => HasDocument && _db != null;

    public IReadOnlyList<string> ExportableSlaveNodes =>
        _db?.Slaves.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList()
        ?? (IReadOnlyList<string>)Array.Empty<string>();

    public void NotifyEditMenuOpening() => NotifyEditCommands();

    [RelayCommand(CanExecute = nameof(CanMutateDocument))]
    private void CreateSlaveNode()
    {
        if (_db == null) return;
        var existing = NodeNames();
        var name = UniqueName("New_SlaveNode", existing.Contains);
        var dlg = new NameInputDialog("New Slave Node", "Name:", name, existing)
        {
            Owner = Application.Current.MainWindow
        };
        if (dlg.ShowDialog() != true) return;
        _db.Slaves.Add(dlg.ResultName);
        MarkDirtyAndRefresh();
    }

    /// <summary>
    /// Vector SFPControl.CreateFrame → FrameDialogForm(newObject: true).
    /// No separate name dialog; Edit Frame opens immediately; Cancel discards.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMutateDocument))]
    private void CreateUnconditionalFrame()
    {
        if (_db == null) return;
        var name = UniqueName("New_UnconditionalFrame", FrameLikeNames().Contains);
        var frame = new LdfFrame
        {
            Name = name,
            Id = NextFreeFrameId(),
            Publisher = string.IsNullOrEmpty(_db.Master) ? "" : _db.Master,
            Length = 8,
            Kind = "Unconditional"
        };
        _db.Frames.Add(frame);
        RebuildTree();
        FillNodes();
        OpenFrameEditor(frame, newlyCreated: true);
    }

    [RelayCommand(CanExecute = nameof(CanMutateDocument))]
    private void CreateEventTriggeredFrame()
    {
        if (_db == null) return;
        var existing = FrameLikeNames();
        var name = UniqueName("New_EventTriggeredFrame", existing.Contains);
        var dlg = new NameInputDialog("New Event-Triggered Frame", "Name:", name, existing)
        {
            Owner = Application.Current.MainWindow
        };
        if (dlg.ShowDialog() != true) return;
        _db.EventTriggeredFrames.Add(new LdfEventTriggeredFrame
        {
            Name = dlg.ResultName,
            CollisionResolvingSchedule = _db.Schedules.FirstOrDefault()?.Name ?? "",
            Id = NextFreeFrameId()
        });
        MarkDirtyAndRefresh();
    }

    [RelayCommand(CanExecute = nameof(CanMutateDocument))]
    private void CreateSporadicFrame()
    {
        if (_db == null) return;
        var existing = FrameLikeNames();
        var name = UniqueName("New_SporadicFrame", existing.Contains);
        var dlg = new NameInputDialog("New Sporadic Frame", "Name:", name, existing)
        {
            Owner = Application.Current.MainWindow
        };
        if (dlg.ShowDialog() != true) return;
        _db.SporadicFrames.Add(new LdfSporadicFrame { Name = dlg.ResultName });
        MarkDirtyAndRefresh();
    }

    [RelayCommand(CanExecute = nameof(CanMutateDocument))]
    private void CreateScheduleTable()
    {
        if (_db == null) return;
        var existing = _db.Schedules.Select(t => t.Name).ToList();
        var name = UniqueName("New_ScheduleTable", n =>
            existing.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase)));
        var dlg = new NameInputDialog("New Schedule Table", "Name:", name, existing)
        {
            Owner = Application.Current.MainWindow
        };
        if (dlg.ShowDialog() != true) return;
        _db.Schedules.Add(new LdfScheduleTable { Name = dlg.ResultName });
        MarkDirtyAndRefresh();
        SelectedScheduleRow = ScheduleRows.FirstOrDefault(r => r.IsHeader && r.Table == dlg.ResultName);
    }

    [RelayCommand(CanExecute = nameof(CanMutateDocument))]
    private void ImportNcf()
    {
        if (_db == null) return;
        var dlg = new OpenFileDialog { Filter = "Node Capability (*.ncf)|*.ncf|All (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var ncf = new NcfAntlrLoader().Load(dlg.FileName);
            var result = NcfImporter.Merge(_db, ncf);
            MarkDirtyAndRefresh();
            MessageBox.Show(result.Summary(), "Import NCF",
                MessageBoxButton.OK,
                result.Warnings.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Import NCF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanMutateDocument))]
    private void ExportNcf(string? slaveOrAll)
    {
        var label = slaveOrAll == "*" || string.IsNullOrEmpty(slaveOrAll)
            ? "all nodes" : slaveOrAll;
        MessageBox.Show($"Export NCF ({label}) is not implemented yet.", "Export NCF",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool CanEditSelection()
    {
        if (!HasDocument) return false;
        if (SelectedTreeItem?.Payload is LdfFrame or LdfSignal
            or LdfEventTriggeredFrame or LdfSporadicFrame) return true;
        if (SelectedFrameSignal != null && DetailFrame != null) return true;
        return false;
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void EditSelection()
    {
        if (SelectedTreeItem?.Payload is LdfFrame f)
        {
            OpenFrameEditor(f);
            return;
        }
        if (SelectedTreeItem?.Payload is LdfSignal s)
        {
            OpenSignalEditor(s);
            return;
        }
        if (SelectedTreeItem?.Payload is LdfEventTriggeredFrame or LdfSporadicFrame)
        {
            MessageBox.Show("Edit dialog for this frame type is not implemented yet.",
                "Edit", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenSelectedSignalEditor();
    }

    private bool CanDeleteSelection()
    {
        if (!HasDocument || _db == null) return false;
        if (SelectedTreeItem?.Payload is LdfFrame or LdfSignal) return true;
        if (SelectedTreeItem?.Payload is LdfEventTriggeredFrame or LdfSporadicFrame) return true;
        if (SelectedTreeItem is { Kind: "Node", Payload: string node }
            && !string.Equals(node, _db.Master, StringComparison.OrdinalIgnoreCase))
            return true;
        if (SelectedScheduleRow != null) return true;
        if (SelectedNodeRow != null
            && !string.Equals(SelectedNodeRow.Node, _db.Master, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelection))]
    private void DeleteSelection()
    {
        if (_db == null) return;
        if (SelectedTreeItem?.Payload is LdfFrame frame)
        {
            if (!AskYes(string.Format(
                    "Do you really want to delete Unconditional Frame <{0}> ?", frame.Name)))
                return;
            RemoveFrameReferences(frame.Name);
            _db.Frames.Remove(frame);
            MarkDirtyAndRefresh();
            return;
        }
        if (SelectedTreeItem?.Payload is LdfSignal signal)
        {
            if (!AskYes(string.Format(
                    "Dou you really want to delete Signal <{0}> ?", signal.Name)))
                return;
            _db.Signals.Remove(signal);
            _db.DiagnosticSignals.Remove(signal);
            foreach (var fr in _db.Frames)
                fr.Signals.RemoveAll(fs => fs.Name == signal.Name);
            foreach (var rep in _db.Representations.ToList())
            {
                rep.Signals.RemoveAll(n => n == signal.Name);
                if (rep.Signals.Count == 0) _db.Representations.Remove(rep);
            }
            MarkDirtyAndRefresh();
            return;
        }
        if (SelectedTreeItem?.Payload is LdfEventTriggeredFrame et)
        {
            if (!AskYes(string.Format(
                    "Do you really want to delete Event-Triggered Frame <{0}> ?", et.Name)))
                return;
            RemoveFrameReferences(et.Name);
            _db.EventTriggeredFrames.Remove(et);
            MarkDirtyAndRefresh();
            return;
        }
        if (SelectedTreeItem?.Payload is LdfSporadicFrame sp)
        {
            if (!AskYes(string.Format(
                    "Do you really want to delete Sporadic Frame <{0}> ?", sp.Name)))
                return;
            RemoveFrameReferences(sp.Name);
            _db.SporadicFrames.Remove(sp);
            MarkDirtyAndRefresh();
            return;
        }
        if (SelectedTreeItem is { Kind: "Node", Payload: string nodeName }
            && !string.Equals(nodeName, _db.Master, StringComparison.OrdinalIgnoreCase))
        {
            DeleteSlaveNode(nodeName);
            return;
        }
        if (SelectedScheduleRow is { } srow)
        {
            if (srow.IsHeader || srow.EntryIndex < 0)
            {
                if (srow.ScheduleIndex < 0 || srow.ScheduleIndex >= _db.Schedules.Count) return;
                var tname = _db.Schedules[srow.ScheduleIndex].Name;
                if (!AskYes(string.Format(
                        "Do you really want to delete Schedule Table <{0}> ?", tname)))
                    return;
                _db.Schedules.RemoveAt(srow.ScheduleIndex);
            }
            else
            {
                if (srow.ScheduleIndex < 0 || srow.ScheduleIndex >= _db.Schedules.Count) return;
                var table = _db.Schedules[srow.ScheduleIndex];
                if (srow.EntryIndex < 0 || srow.EntryIndex >= table.Slots.Count) return;
                if (!AskYes("Do you really want to delete Schedule Table Slot ?")) return;
                table.Slots.RemoveAt(srow.EntryIndex);
            }
            MarkDirtyAndRefresh();
            return;
        }
        if (SelectedNodeRow != null
            && !string.Equals(SelectedNodeRow.Node, _db.Master, StringComparison.OrdinalIgnoreCase))
            DeleteSlaveNode(SelectedNodeRow.Node);
    }

    /// <summary>Vector DeleteSlaveNodeQuestion + cascade published frames/signals/slots.</summary>
    private void DeleteSlaveNode(string nodeName)
    {
        if (_db == null) return;
        if (!AskYes(
                $"Do you really want to delete Slave Node <{nodeName}> ?\n\n" +
                "Warning: This also deletes all published Frames and Signals as well as all Schedule Slots referring to this Node !"))
            return;

        var pubFrames = _db.Frames
            .Where(f => string.Equals(f.Publisher, nodeName, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _db.Frames.RemoveAll(f => pubFrames.Contains(f.Name));
        _db.Signals.RemoveAll(s =>
            string.Equals(s.Publisher, nodeName, StringComparison.OrdinalIgnoreCase));
        foreach (var fr in _db.Frames)
            fr.Signals.RemoveAll(fs =>
                _db.FindSignal(fs.Name) == null);
        foreach (var t in _db.Schedules)
            t.Slots.RemoveAll(sl => pubFrames.Contains(sl.FrameName));
        foreach (var s in _db.Signals)
            s.Subscribers.RemoveAll(n => string.Equals(n, nodeName, StringComparison.OrdinalIgnoreCase));
        _db.Slaves.RemoveAll(s => string.Equals(s, nodeName, StringComparison.OrdinalIgnoreCase));
        _db.NodeAttributes.RemoveAll(a =>
            string.Equals(a.Name, nodeName, StringComparison.OrdinalIgnoreCase));
        MarkDirtyAndRefresh();
    }

    private void RemoveFrameReferences(string frameName)
    {
        if (_db == null) return;
        foreach (var t in _db.Schedules)
            t.Slots.RemoveAll(s => string.Equals(s.FrameName, frameName, StringComparison.OrdinalIgnoreCase));
        foreach (var sp in _db.SporadicFrames)
            sp.FrameNames.RemoveAll(n => string.Equals(n, frameName, StringComparison.OrdinalIgnoreCase));
        foreach (var et in _db.EventTriggeredFrames)
            et.FrameNames.RemoveAll(n => string.Equals(n, frameName, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand(CanExecute = nameof(CanMutateDocument))]
    private void UpdateAll()
    {
        /* Vector UpdateAll — refresh panes / status from model */
        if (_db == null) return;
        RebuildTree();
        FillSchedules();
        FillNodes();
        if (SelectedTreeItem != null)
            FillTreeSelection(SelectedTreeItem);
        else if (SelectedScheduleRow != null)
            FillScheduleSelection(SelectedScheduleRow);
        else
        {
            ShowDetailNodes();
            SelectIndex0AndFillObjectProps();
        }
        StatusText =
            $"LIN Commander: {_db.Master}   Jitter: {_db.JitterMs} ms   Timebase: {_db.TimebaseMs} ms   Channel: {_db.ChannelName}   LIN Speed: {_db.SpeedKbps} kbps   Protocol: {_db.ProtocolVersion}";
    }

    [RelayCommand]
    private void HelpContent()
    {
        /* Vector ShowHelp / tsbHelp_Click → HelpFilePath ../Help01/LDFExplorer.chm */
        MessageBox.Show(
            "Help content (LDFExplorer.chm) is not bundled with this build.",
            "Help Content", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void About()
    {
        MessageBox.Show(
            "LIN Explorer\nLDF viewer / editor (Vector LDF Explorer compatible)\n\nSupports LIN 1.3 / 2.0 / 2.1 / 2.2 / ISO17987 / J2602",
            "About LIN Explorer", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Vector tsbSaveFileAs / Save All (single-document: save current).</summary>
    [RelayCommand(CanExecute = nameof(CanSaveAs))]
    private void SaveAll() => DoSave(promptIfNoPath: string.IsNullOrEmpty(FilePath));

    private string? _lastOpenedPath;
    private string? _lastOpenedText;

    private bool CanRunConsistencyChecker() =>
        HasDocument || !string.IsNullOrEmpty(_lastOpenedPath);

    /// <summary>Vector tsbStartLINConsistencyChecker_Click / RunLINConsistencyChecker(file).</summary>
    [RelayCommand(CanExecute = nameof(CanRunConsistencyChecker))]
    private void RunConsistencyChecker()
    {
        var path = !string.IsNullOrEmpty(FilePath) ? FilePath : _lastOpenedPath;
        string? text = null;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            text = File.ReadAllText(path);
        else if (!string.IsNullOrEmpty(_lastOpenedText))
            text = _lastOpenedText;
        else if (_db != null)
            text = LdfWriter.Write(_db);

        var owner = Application.Current?.MainWindow;
        ConsistencyCheckerWindow.ShowFor(owner, path, text, _db,
            documentSaved: OnConsistencyCheckerSaved);
        StatusText = "LIN Consistency Checker opened.";
    }

    /// <summary>Apply LDF text saved from Consistency Checker into the main document when it parses.</summary>
    private void OnConsistencyCheckerSaved(string path, string text)
    {
        _lastOpenedPath = path;
        _lastOpenedText = text;
        try
        {
            var db = _loader.LoadText(text, path);
            LoadDatabase(db, path);
            /* Prefer the exact editor text (not re-serialized) so CC fixes stick. */
            _documentText = text;
            IsDirty = false;
            StatusText = $"Loaded from Consistency Checker: {Path.GetFileName(path)}";
        }
        catch (Exception)
        {
            /* Still broken — keep last text for next CC open; do not wipe main UI if already loaded. */
            StatusText = $"Consistency Checker saved {Path.GetFileName(path)} (still has errors).";
            RunConsistencyCheckerCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnIsHexModeChanged(bool value)
    {
        /* Vector tsbHexDec_CheckedChanged → GlobalNumberVisualizationMode
           → IModelObserver<NumberVisualizationMode>.OnNotifyPostPropChange
           → NodesFramesView.DoSmoothHexDecUpdate (in-place, keep selection). */
        LdfDisplayFormat.UseHex = value;
        OnPropertyChanged(nameof(HexModeLabel));
        if (_db != null)
            SmoothHexDecUpdate();
    }

    /// <summary>
    /// Vector DoSmoothHexDecUpdate / DoRecursiveSmoothUpdate —
    /// refresh ID/NAD displays without RebuildTree (avoids losing TreeView selection).
    /// </summary>
    private void SmoothHexDecUpdate()
    {
        if (_db == null) return;

        RefreshTreeHexDecTitles(FramesTreeRoots);
        RefreshTreeHexDecTitles(NodesTreeRoots);
        RefreshFrameListIds();
        RefreshNodeRowNads();
        RefreshPropertyPanesForHexDec();

        StatusText =
            $"Number display: {(IsHexMode ? "hexadecimal" : "decimal")}   " +
            $"LIN Commander: {_db.Master}   Protocol: {_db.ProtocolVersion}";
    }

    private static void RefreshTreeHexDecTitles(IEnumerable<TreeItemViewModel> roots)
    {
        foreach (var n in roots)
            RefreshTreeHexDecTitlesNode(n);
    }

    private static void RefreshTreeHexDecTitlesNode(TreeItemViewModel n)
    {
        switch (n.Payload)
        {
            case LdfFrame f:
                n.Title = $"{f.Name}  ({LdfDisplayFormat.FormatId(f.Id)})";
                break;
            case LdfEventTriggeredFrame e:
                n.Title = $"{e.Name}  ({LdfDisplayFormat.FormatId(e.Id)})";
                break;
        }
        foreach (var c in n.Children)
            RefreshTreeHexDecTitlesNode(c);
    }

    private void RefreshFrameListIds()
    {
        if (FrameListRows.Count == 0) return;
        string? keep = SelectedFrameListRow?.Name;
        for (int i = 0; i < FrameListRows.Count; i++)
        {
            var r = FrameListRows[i];
            string id = r.Payload switch
            {
                LdfFrame f => LdfDisplayFormat.FormatId(f.Id),
                LdfEventTriggeredFrame e => LdfDisplayFormat.FormatId(e.Id),
                _ => r.Id
            };
            if (id == r.Id) continue;
            FrameListRows[i] = new FrameListRow
            {
                Index = r.Index,
                Name = r.Name,
                Id = id,
                Publisher = r.Publisher,
                Length = r.Length,
                Type = r.Type,
                Payload = r.Payload
            };
        }
        if (keep == null) return;
        _suppressFrameListSync = true;
        SelectedFrameListRow = FrameListRows.FirstOrDefault(x =>
            string.Equals(x.Name, keep, StringComparison.OrdinalIgnoreCase));
        _suppressFrameListSync = false;
    }

    private void RefreshNodeRowNads()
    {
        if (NodeRows.Count == 0 || _db == null) return;
        string? keep = SelectedNodeRow?.Node;
        for (int i = 0; i < NodeRows.Count; i++)
        {
            var r = NodeRows[i];
            if (string.Equals(r.Node, _db.Master, StringComparison.OrdinalIgnoreCase))
                continue;
            var a = r.Payload as LdfNodeAttr
                    ?? _db.NodeAttributes.FirstOrDefault(x =>
                        string.Equals(x.Name, r.Node, StringComparison.OrdinalIgnoreCase));
            if (a == null) continue;
            string cfg = FormatNad(a.ConfiguredNad);
            string ini = a.HasInitialNad ? FormatNad(a.InitialNad) : "";
            if (cfg == r.ConfiguredNad && ini == r.InitialNad) continue;
            NodeRows[i] = new NodeRow
            {
                Node = r.Node,
                Timebase = r.Timebase,
                Jitter = r.Jitter,
                ConfiguredNad = cfg,
                InitialNad = ini,
                Protocol = r.Protocol,
                SupplierId = r.SupplierId,
                FunctionId = r.FunctionId,
                VariantId = r.VariantId,
                Payload = a
            };
        }
        if (keep == null) return;
        _suppressNodeSync = true;
        SelectedNodeRow = NodeRows.FirstOrDefault(x =>
            string.Equals(x.Node, keep, StringComparison.OrdinalIgnoreCase));
        _suppressNodeSync = false;
    }

    private void RefreshPropertyPanesForHexDec()
    {
        if (_db == null) return;

        /* Only refresh prop trees — do not re-run FillScheduleSelection (it resets middle pane). */
        if (SelectedScheduleRow is { } srow)
        {
            if (srow.IsHeader || srow.EntryIndex < 0)
                LdfPropertyBuilder.FillScheduleTable(_db, srow.ScheduleIndex, ScheduleProps);
            else
                LdfPropertyBuilder.FillScheduleSlot(_db, srow.ScheduleIndex, srow.EntryIndex, ScheduleProps);
        }

        if (SelectedFrameSignal != null)
        {
            var sg = _db.FindSignal(SelectedFrameSignal.Name);
            if (sg != null)
            {
                LdfPropertyBuilder.FillSignal(_db, sg, BusProps,
                    DetailFrame != null ? SelectedFrameSignal.StartBit : null,
                    DetailFrame?.Name);
                return;
            }
        }

        if (SelectedFrameListRow != null)
        {
            FillFrameListSelection(SelectedFrameListRow);
            return;
        }

        if (SelectedNodeRow != null)
        {
            LdfPropertyBuilder.FillNodeFromRow(SelectedNodeRow, BusProps);
            return;
        }

        if (SelectedTreeItem?.Payload is LdfFrame f)
        {
            LdfPropertyBuilder.FillFrame(_db, f, BusProps);
            return;
        }
        if (SelectedTreeItem?.Payload is LdfEventTriggeredFrame et)
        {
            LdfPropertyBuilder.FillEventTriggered(et, BusProps);
            return;
        }
        if (SelectedTreeItem?.Payload is LdfSporadicFrame sp)
        {
            LdfPropertyBuilder.FillSporadic(sp, BusProps);
            return;
        }
        if (SelectedTreeItem?.Payload is LdfSignal sig)
        {
            LdfPropertyBuilder.FillSignal(_db, sig, BusProps);
            return;
        }
        if (DetailFrame != null)
            LdfPropertyBuilder.FillFrame(_db, DetailFrame, BusProps);
    }


    private int NextFreeFrameId()
    {
        if (_db == null) return 0;
        var used = _db.Frames.Select(f => f.Id)
            .Concat(_db.EventTriggeredFrames.Select(e => e.Id))
            .ToHashSet();
        for (int id = 0; id < 64; id++)
            if (!used.Contains(id)) return id;
        return 0;
    }

    private HashSet<string> NodeNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_db == null) return set;
        if (!string.IsNullOrEmpty(_db.Master)) set.Add(_db.Master);
        foreach (var s in _db.Slaves) set.Add(s);
        return set;
    }

    private HashSet<string> FrameLikeNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_db == null) return set;
        foreach (var f in _db.Frames) set.Add(f.Name);
        foreach (var e in _db.EventTriggeredFrames) set.Add(e.Name);
        foreach (var s in _db.SporadicFrames) set.Add(s.Name);
        return set;
    }

    /// <summary>Vector UniqueNameGenerator: pattern, then pattern_1, pattern_2, …</summary>
    private static string UniqueName(string pattern, Func<string, bool> exists)
    {
        if (!exists(pattern)) return pattern;
        for (int i = 1; i < 10000; i++)
        {
            var n = $"{pattern}_{i}";
            if (!exists(n)) return n;
        }
        return pattern + "_" + Guid.NewGuid().ToString("N")[..4];
    }

    private static bool AskYes(string message) =>
        MessageBox.Show(message, "LIN Explorer", MessageBoxButton.YesNo, MessageBoxImage.Question)
        == MessageBoxResult.Yes;

    private void MarkDirtyAndRefresh()
    {
        IsDirty = true;
        var keepSched = SelectedScheduleRow;
        (int Si, int Ei, bool Header)? schedKey = keepSched == null
            ? null
            : (keepSched.ScheduleIndex, keepSched.EntryIndex, keepSched.IsHeader);
        RebuildTree();
        FillSchedules();
        FillNodes();
        if (schedKey is { } key)
        {
            SelectedScheduleRow = ScheduleRows.FirstOrDefault(r =>
                r.ScheduleIndex == key.Si
                && r.EntryIndex == key.Ei
                && r.IsHeader == key.Header)
                ?? ScheduleRows.FirstOrDefault(r => r.IsHeader);
        }
        else
            SelectedScheduleRow = ScheduleRows.FirstOrDefault(r => r.IsHeader);
        if (SelectedTreeItem != null)
            FillTreeSelection(SelectedTreeItem);
        else if (SelectedScheduleRow != null)
            FillScheduleSelection(SelectedScheduleRow);
        else if (_db != null)
            LdfPropertyBuilder.FillCommonLinBus(_db, BusProps);
        NotifyEditCommands();
        StatusText = "Document modified";
    }

    private void NotifyEditCommands()
    {
        CreateSlaveNodeCommand.NotifyCanExecuteChanged();
        CreateUnconditionalFrameCommand.NotifyCanExecuteChanged();
        CreateEventTriggeredFrameCommand.NotifyCanExecuteChanged();
        CreateSporadicFrameCommand.NotifyCanExecuteChanged();
        CreateScheduleTableCommand.NotifyCanExecuteChanged();
        ImportNcfCommand.NotifyCanExecuteChanged();
        ExportNcfCommand.NotifyCanExecuteChanged();
        EditSelectionCommand.NotifyCanExecuteChanged();
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        UpdateAllCommand.NotifyCanExecuteChanged();
    }

    private void ReloadRecentFiles()
    {
        RecentFiles.Clear();
        int i = 1;
        foreach (var path in _recentStore.Load())
        {
            RecentFiles.Add(new RecentFileItem
            {
                Index = i,
                Path = path,
                Display = $"_{i} {RecentFilesStore.FormatDisplay(path)}"
            });
            i++;
        }
        RecentFilesChanged?.Invoke();
    }

    private void PushRecent(string path)
    {
        _recentStore.Add(path);
        ReloadRecentFiles();
    }

    public void OpenFrameEditor(LdfFrame frame, bool newlyCreated = false)
    {
        if (_db == null) return;
        var owner = Application.Current.MainWindow;
        var dlg = new FrameEditorWindow(_db, frame) { Owner = owner };
        if (dlg.ShowDialog() == true)
        {
            IsDirty = true;
            RebuildTree();
            FillNodes();
            ShowDetailFrame(frame);
            FillTreeSelection(SelectedTreeItem);
            StatusText = "Document modified";
            return;
        }

        /* Vector CreateFrame: Cancel → discard new frame (working copy not synced). */
        if (newlyCreated)
        {
            RemoveFrameReferences(frame.Name);
            _db.Frames.Remove(frame);
            RebuildTree();
            FillNodes();
            ShowDetailNodes();
            NotifyEditCommands();
        }
    }

    public void OpenSignalEditor(LdfSignal signal, string? frameName = null, int startBit = -1)
    {
        if (_db == null) return;
        var owner = Application.Current.MainWindow;
        var dlg = new SignalEditorWindow(_db, signal, frameName, startBit) { Owner = owner };
        if (dlg.ShowDialog() == true)
        {
            IsDirty = true;
            RebuildTree();
            if (DetailFrame != null)
                ShowDetailFrame(DetailFrame);
        }
    }

    public void OpenSelectedFrameEditor()
    {
        if (SelectedTreeItem?.Payload is LdfFrame f)
            OpenFrameEditor(f);
        else if (DetailFrame != null)
            OpenFrameEditor(DetailFrame);
    }

    public void OpenSelectedSignalEditor()
    {
        if (_db == null) return;
        if (SelectedTreeItem?.Payload is LdfSignal s)
        {
            OpenSignalEditor(s);
            return;
        }
        if (SelectedFrameSignal != null && DetailFrame != null)
        {
            var sg = _db.FindSignal(SelectedFrameSignal.Name);
            if (sg != null)
                OpenSignalEditor(sg, DetailFrame.Name, SelectedFrameSignal.StartBit);
        }
    }

    public void OpenSignalEditorAt(int frameSigIndex)
    {
        if (_db == null || DetailFrame == null) return;
        if (frameSigIndex < 0 || frameSigIndex >= DetailFrame.Signals.Count) return;
        var fs = DetailFrame.Signals[frameSigIndex];
        var sg = _db.FindSignal(fs.Name);
        if (sg != null)
            OpenSignalEditor(sg, DetailFrame.Name, fs.StartBit);
    }

    public void LoadPath(string path)
    {
        string text = "";
        try
        {
            text = File.ReadAllText(path);
            _lastOpenedPath = path;
            _lastOpenedText = text;
            _documentText = text;
            var db = _loader.LoadText(text, path);
            LoadDatabase(db, path);
        }
        catch (Exception ex)
        {
            /* Vector OpenFile: Read Error dialog + RunLINConsistencyChecker(fileName). */
            var caption = ex is LdfParseException ? "Read Error" : "LDF load failed";
            MessageBox.Show(ex.Message, caption, MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Load failed";
            RunConsistencyCheckerCommand.NotifyCanExecuteChanged();
            try
            {
                var owner = Application.Current?.MainWindow;
                var pex = ex as LdfParseException;
                ConsistencyCheckerWindow.ShowFor(owner, path, text, db: null,
                    pex?.Issues, pex?.Diagnostics,
                    documentSaved: OnConsistencyCheckerSaved);
            }
            catch
            {
                /* ignore secondary UI failures */
            }
        }
    }

    /// <summary>Bind an in-memory or loaded model to the UI (Vector CreateNewLdf / OpenFile).</summary>
    private void LoadDatabase(LdfDatabase db, string? path)
    {
        _db = db;
        Database = _db;
        FilePath = path ?? "";
        HasDocument = true;
        IsDirty = false;
        _documentText = LdfWriter.Write(_db);
        Title = string.IsNullOrEmpty(FilePath)
            ? "LIN Explorer"
            : $"LIN Explorer — {Path.GetFileName(FilePath)}";
        RebuildTree();
        FillSchedules();
        FillNodes();
        ObjectPropTitle = "Properties";
        SelectedTreeItem = null;
        SelectedNodeRow = null;
        SelectedFrameListRow = null;
        SelectedFrameSignal = null;
        SelectedScheduleRow = ScheduleRows.FirstOrDefault(r => r.IsHeader)
                              ?? ScheduleRows.FirstOrDefault();
        if (SelectedScheduleRow == null)
        {
            ScheduleProps.Clear();
            SchedulePropTitle = "Schedule Properties";
            ShowDetailNodes();
            SelectIndex0AndFillObjectProps();
        }
        if (!string.IsNullOrEmpty(FilePath))
            PushRecent(FilePath);
        NotifyEditCommands();
        StatusText =
            $"LIN Commander: {_db.Master}   Jitter: {_db.JitterMs} ms   Timebase: {_db.TimebaseMs} ms   Channel: {_db.ChannelName}   LIN Speed: {_db.SpeedKbps} kbps   Protocol: {_db.ProtocolVersion}";
    }

    private void SetDetailPane(bool nodes, bool frameList, bool signals)
    {
        IsNodesDetailMode = nodes;
        IsFrameListDetailMode = frameList;
        IsFrameDetailMode = signals;
    }

    private void ShowDetailNodes(string? title = null)
    {
        SetDetailPane(nodes: true, frameList: false, signals: false);
        DetailFrame = null;
        FrameListRows.Clear();
        FrameSignals.Clear();
        _suppressFrameListSync = true;
        SelectedFrameListRow = null;
        _suppressFrameListSync = false;
        _suppressSigSync = true;
        SelectedSignalIndex = -1;
        SelectedFrameSignal = null;
        _suppressSigSync = false;
        var name = _db == null || string.IsNullOrEmpty(_db.ChannelName) ? "LDF" : _db.ChannelName;
        DetailTitle = title ?? $"Nodes of <{name}>";
    }

    private void ShowDetailFrameList(string title, IEnumerable<object> payloads)
    {
        if (_db == null) return;
        SetDetailPane(nodes: false, frameList: true, signals: false);
        DetailFrame = null;
        FrameSignals.Clear();
        _suppressSigSync = true;
        SelectedSignalIndex = -1;
        SelectedFrameSignal = null;
        _suppressSigSync = false;
        DetailTitle = title;
        FrameListRows.Clear();
        int i = 0;
        foreach (var p in payloads)
        {
            switch (p)
            {
                case LdfFrame f:
                    FrameListRows.Add(new FrameListRow
                    {
                        Index = i++,
                        Name = f.Name,
                        Id = LdfDisplayFormat.FormatId(f.Id),
                        Publisher = f.Publisher,
                        Length = (f.Length > 0 ? f.Length : 8).ToString(CultureInfo.InvariantCulture),
                        Type = f.IsDiagnostic ? "Diagnostic" : "Unconditional",
                        Payload = f
                    });
                    break;
                case LdfEventTriggeredFrame e:
                    FrameListRows.Add(new FrameListRow
                    {
                        Index = i++,
                        Name = e.Name,
                        Id = LdfDisplayFormat.FormatId(e.Id),
                        Publisher = "",
                        Length = "",
                        Type = "Event-Triggered",
                        Payload = e
                    });
                    break;
                case LdfSporadicFrame s:
                    FrameListRows.Add(new FrameListRow
                    {
                        Index = i++,
                        Name = s.Name,
                        Id = "",
                        Publisher = "",
                        Length = "",
                        Type = "Sporadic",
                        Payload = s
                    });
                    break;
            }
        }
        _suppressFrameListSync = true;
        SelectedFrameListRow = null;
        _suppressFrameListSync = false;
    }

    private void ShowDetailFrame(LdfFrame frame)
    {
        if (_db == null) return;
        SetDetailPane(nodes: false, frameList: false, signals: true);
        /* Same instance after SizeBits edit would skip DP change; nudge binding so Layout rebuilds. */
        if (ReferenceEquals(DetailFrame, frame))
            DetailFrame = null;
        DetailFrame = frame;
        FrameListRows.Clear();
        _suppressFrameListSync = true;
        SelectedFrameListRow = null;
        _suppressFrameListSync = false;
        DetailTitle = frame.IsDiagnostic
            ? $"Signals of Diagnostic Frame <{frame.Name}>"
            : $"Signals of Frame <{frame.Name}>";
        FrameSignals.Clear();
        for (int i = 0; i < frame.Signals.Count; i++)
        {
            var fs = frame.Signals[i];
            var sg = _db.FindSignal(fs.Name);
            FrameSignals.Add(new FrameSignalRow
            {
                Index = i,
                Name = fs.Name,
                StartBit = fs.StartBit,
                Length = sg?.SizeBits ?? 1,
                Publisher = sg?.Publisher ?? "",
                Subscribers = sg == null ? "" : string.Join(", ", sg.Subscribers)
            });
        }
        _suppressSigSync = true;
        SelectedSignalIndex = -1;
        SelectedFrameSignal = null;
        _suppressSigSync = false;
    }

    private void ShowDetailSignalList(string title, IEnumerable<LdfSignal> signals)
    {
        SetDetailPane(nodes: false, frameList: false, signals: true);
        DetailFrame = null;
        FrameListRows.Clear();
        _suppressFrameListSync = true;
        SelectedFrameListRow = null;
        _suppressFrameListSync = false;
        DetailTitle = title;
        FrameSignals.Clear();
        int i = 0;
        foreach (var s in signals)
        {
            FrameSignals.Add(new FrameSignalRow
            {
                Index = i++,
                Name = s.Name,
                StartBit = 0,
                Length = s.SizeBits,
                Publisher = s.Publisher,
                Subscribers = string.Join(", ", s.Subscribers)
            });
        }
        _suppressSigSync = true;
        SelectedSignalIndex = -1;
        SelectedFrameSignal = null;
        _suppressSigSync = false;
    }

    /// <summary>
    /// Vector Pane4→Pane5: after middle list is filled, focus row 0 and push its props to lower-right.
    /// </summary>
    private void SelectIndex0AndFillObjectProps()
    {
        if (_db == null) return;
        _applyingIndex0 = true;
        try
        {
            if (IsNodesDetailMode)
            {
                var row = NodeRows.FirstOrDefault();
                if (row == null)
                {
                    LdfPropertyBuilder.FillCommonLinBus(_db, BusProps);
                    return;
                }
                _suppressNodeSync = true;
                SelectedNodeRow = row;
                _suppressNodeSync = false;
                LdfPropertyBuilder.FillNodeFromRow(row, BusProps);
            }
            else if (IsFrameListDetailMode)
            {
                var row = FrameListRows.FirstOrDefault();
                if (row == null)
                {
                    LdfPropertyBuilder.FillCommonLinBus(_db, BusProps);
                    return;
                }
                _suppressFrameListSync = true;
                SelectedFrameListRow = row;
                _suppressFrameListSync = false;
                FillFrameListSelection(row);
            }
            else if (IsFrameDetailMode)
            {
                /* Frame bitmap/list: do not auto-select a signal (layout starts unselected). */
                if (DetailFrame != null)
                {
                    _suppressSigSync = true;
                    SelectedFrameSignal = null;
                    SelectedSignalIndex = -1;
                    _suppressSigSync = false;
                    LdfPropertyBuilder.FillFrame(_db, DetailFrame, BusProps);
                    return;
                }

                /* Signal-only lists (no frame layout): still focus first row for props. */
                var row = FrameSignals.FirstOrDefault();
                if (row == null)
                {
                    LdfPropertyBuilder.FillCommonLinBus(_db, BusProps);
                    return;
                }
                _suppressSigSync = true;
                SelectedFrameSignal = row;
                SelectedSignalIndex = row.Index;
                _suppressSigSync = false;
                var sg = _db.FindSignal(row.Name);
                if (sg != null)
                    LdfPropertyBuilder.FillSignal(_db, sg, BusProps, null, null);
            }
        }
        finally
        {
            _applyingIndex0 = false;
        }
    }

    private void RebuildTree()
    {
        FramesTreeRoots.Clear();
        NodesTreeRoots.Clear();
        if (_db == null) return;

        /* Root always uses LDF file name (not Channel_name, which is often "DB"). */
        var ldfName = Path.GetFileNameWithoutExtension(_db.FilePath);
        if (string.IsNullOrEmpty(ldfName))
            ldfName = string.IsNullOrEmpty(_db.Name) ? "LDF" : _db.Name;
        _db.Name = ldfName;

        var framesRoot = new TreeItemViewModel { Title = ldfName, Kind = "Root", Payload = _db };

        var uncond = new TreeItemViewModel { Title = "Unconditional Frames", Kind = "Cat" };
        foreach (var f in _db.Frames.Where(f => !f.IsDiagnostic))
            uncond.Children.Add(new TreeItemViewModel
            {
                Title = $"{f.Name}  ({LdfDisplayFormat.FormatId(f.Id)})",
                Kind = "Frame",
                Payload = f
            });

        var diag = new TreeItemViewModel { Title = "Diagnostic Frames", Kind = "Cat" };
        foreach (var f in _db.Frames.Where(f => f.IsDiagnostic))
            diag.Children.Add(new TreeItemViewModel
            {
                Title = $"{f.Name}  ({LdfDisplayFormat.FormatId(f.Id)})",
                Kind = "Frame",
                Payload = f
            });

        var evt = new TreeItemViewModel { Title = "Event-Triggered Frames", Kind = "Cat" };
        foreach (var e in _db.EventTriggeredFrames)
        {
            var etNode = new TreeItemViewModel
            {
                Title = $"{e.Name}  ({LdfDisplayFormat.FormatId(e.Id)})",
                Kind = "Event",
                Payload = e
            };
            foreach (var fn in e.FrameNames)
            {
                var fr = _db.FindFrame(fn);
                etNode.Children.Add(fr != null
                    ? new TreeItemViewModel
                    {
                        Title = $"{fr.Name}  ({LdfDisplayFormat.FormatId(fr.Id)})",
                        Kind = "Frame",
                        Payload = fr
                    }
                    : new TreeItemViewModel { Title = fn, Kind = "FrameRef" });
            }
            evt.Children.Add(etNode);
        }

        var spo = new TreeItemViewModel { Title = "Sporadic Frames", Kind = "Cat" };
        foreach (var s in _db.SporadicFrames)
        {
            var spNode = new TreeItemViewModel { Title = s.Name, Kind = "Sporadic", Payload = s };
            foreach (var fn in s.FrameNames)
            {
                var fr = _db.FindFrame(fn);
                spNode.Children.Add(fr != null
                    ? new TreeItemViewModel
                    {
                        Title = $"{fr.Name}  ({LdfDisplayFormat.FormatId(fr.Id)})",
                        Kind = "Frame",
                        Payload = fr
                    }
                    : new TreeItemViewModel { Title = fn, Kind = "FrameRef" });
            }
            spo.Children.Add(spNode);
        }

        /* Vector LDF Explorer predefined command list */
        var cmd = new TreeItemViewModel { Title = "Predefined Commands", Kind = "Cat" };
        foreach (var c in new[]
                 {
                     "AssignNAD", "ConditionalChangeNAD", "AssignFrameIdRange", "AssignFrameId",
                     "UnassignFrameId", "ReadByIdentifier", "SaveConfiguration", "DataDump", "FreeFormat"
                 })
            cmd.Children.Add(new TreeItemViewModel { Title = c, Kind = "Command", Payload = c });

        framesRoot.Children.Add(uncond);
        framesRoot.Children.Add(diag);
        framesRoot.Children.Add(evt);
        framesRoot.Children.Add(spo);
        framesRoot.Children.Add(cmd);
        FramesTreeRoots.Add(framesRoot);

        var nodesRoot = new TreeItemViewModel { Title = ldfName, Kind = "Root", Payload = _db };
        void AddNode(string name)
        {
            var n = new TreeItemViewModel { Title = name, Kind = "Node", Payload = name };
            var pubSig = new TreeItemViewModel { Title = "Published Signals", Kind = "PubSig" };
            var subSig = new TreeItemViewModel { Title = "Subscribed Signals", Kind = "SubSig" };
            var pubFrm = new TreeItemViewModel { Title = "Published Frames", Kind = "PubFrm" };
            var subFrm = new TreeItemViewModel { Title = "Subscribed Frames", Kind = "SubFrm" };
            foreach (var s in _db.Signals.Where(s => s.Publisher == name))
                pubSig.Children.Add(new TreeItemViewModel { Title = s.Name, Kind = "Signal", Payload = s });
            foreach (var s in _db.Signals.Where(s => s.Subscribers.Contains(name)))
                subSig.Children.Add(new TreeItemViewModel { Title = s.Name, Kind = "Signal", Payload = s });
            foreach (var f in _db.Frames.Where(f => f.Publisher == name))
                pubFrm.Children.Add(new TreeItemViewModel { Title = f.Name, Kind = "Frame", Payload = f });
            foreach (var f in _db.Frames.Where(f =>
                         f.Signals.Any(fs =>
                         {
                             var sg = _db.FindSignal(fs.Name);
                             return sg != null && sg.Subscribers.Contains(name);
                         })))
                subFrm.Children.Add(new TreeItemViewModel { Title = f.Name, Kind = "Frame", Payload = f });
            n.Children.Add(pubSig);
            n.Children.Add(subSig);
            n.Children.Add(pubFrm);
            n.Children.Add(subFrm);
            nodesRoot.Children.Add(n);
        }
        if (!string.IsNullOrEmpty(_db.Master)) AddNode(_db.Master);
        foreach (var s in _db.Slaves) AddNode(s);
        NodesTreeRoots.Add(nodesRoot);
    }

    private void FillSchedules()
    {
        ScheduleRows.Clear();
        if (_db == null) return;
        for (int si = 0; si < _db.Schedules.Count; si++)
        {
            var t = _db.Schedules[si];
            ScheduleRows.Add(new ScheduleRow
            {
                Table = t.Name,
                IsHeader = true,
                ScheduleIndex = si,
                EntryIndex = -1,
                Payload = t
            });
            for (int ei = 0; ei < t.Slots.Count; ei++)
            {
                var s = t.Slots[ei];
                int nbytes = LdfPropertyBuilder.ScheduleEntryPayloadBytes(_db, s);
                double tMin = LdfPropertyBuilder.CalcLinFrameTimeMs(_db, nbytes);
                double tMax = LdfPropertyBuilder.CalcLinFrameTimeMaxMs(_db, nbytes);
                LdfPropertyBuilder.CalcSlotPeriodMinMax(_db, si, ei, out var pMin, out var pMax);
                var label = string.IsNullOrEmpty(s.FrameName)
                    ? LdfPropertyBuilder.StripScheduleCommand(s.Command)
                    : s.FrameName;
                ScheduleRows.Add(new ScheduleRow
                {
                    Table = "    " + label,
                    Type = s.Type,
                    Delay = s.DelayMs.ToString("0.00", CultureInfo.InvariantCulture),
                    MaxFrame = tMax.ToString("0.00", CultureInfo.InvariantCulture),
                    MinFrame = tMin.ToString("0.00", CultureInfo.InvariantCulture),
                    Period = $"{pMin:0.00} / {pMax:0.00}",
                    ScheduleIndex = si,
                    EntryIndex = ei,
                    Payload = s
                });
            }
        }
    }

    private void FillNodes()
    {
        NodeRows.Clear();
        if (_db == null) return;
        if (!string.IsNullOrEmpty(_db.Master))
        {
            NodeRows.Add(new NodeRow
            {
                Node = _db.Master,
                Timebase = _db.TimebaseMs.ToString("0.###", CultureInfo.InvariantCulture),
                Jitter = _db.JitterMs.ToString("0.###", CultureInfo.InvariantCulture),
                Protocol = _db.ProtocolVersion
            });
        }
        foreach (var slave in _db.Slaves)
        {
            var a = _db.NodeAttributes.FirstOrDefault(x => x.Name == slave);
            if (a != null)
            {
                NodeRows.Add(new NodeRow
                {
                    Node = a.Name,
                    ConfiguredNad = FormatNad(a.ConfiguredNad),
                    InitialNad = a.HasInitialNad ? FormatNad(a.InitialNad) : "",
                    Protocol = a.Protocol,
                    SupplierId = a.SupplierId,
                    FunctionId = a.FunctionId,
                    VariantId = a.VariantId,
                    Payload = a
                });
            }
            else
                NodeRows.Add(new NodeRow { Node = slave, Protocol = _db.ProtocolVersion });
        }
    }

    private static string FormatNad(int nad) => LdfDisplayFormat.FormatNad(nad);

    private void FillTreeSelection(TreeItemViewModel? item)
    {
        if (_db == null || item == null) return;
        ObjectPropTitle = "Properties";

        switch (item.Kind)
        {
            case "Root":
                ShowDetailNodes();
                _suppressNodeSync = true;
                SelectedNodeRow = NodeRows.FirstOrDefault();
                _suppressNodeSync = false;
                LdfPropertyBuilder.FillCommonLinBus(_db, BusProps);
                return;

            case "Cat" when item.Title == "Unconditional Frames":
                ShowDetailFrameList("Unconditional Frames",
                    _db.Frames.Where(f => !f.IsDiagnostic));
                SelectIndex0AndFillObjectProps();
                return;

            case "Cat" when item.Title == "Diagnostic Frames":
                ShowDetailFrameList("Diagnostic Frames",
                    _db.Frames.Where(f => f.IsDiagnostic));
                SelectIndex0AndFillObjectProps();
                return;

            case "Cat" when item.Title == "Event-Triggered Frames":
                ShowDetailFrameList("Event-Triggered Frames", _db.EventTriggeredFrames);
                SelectIndex0AndFillObjectProps();
                return;

            case "Cat" when item.Title == "Sporadic Frames":
                ShowDetailFrameList("Sporadic Frames", _db.SporadicFrames);
                SelectIndex0AndFillObjectProps();
                return;

            case "Cat" when item.Title == "Predefined Commands":
                ShowDetailNodes();
                SelectIndex0AndFillObjectProps();
                return;

            case "PubFrm":
            {
                var node = item.FindAncestor("Node")?.Title ?? "";
                var frames = _db.Frames.Where(f =>
                    string.Equals(f.Publisher, node, StringComparison.OrdinalIgnoreCase));
                ShowDetailFrameList($"Published Frames of <{node}>", frames);
                SelectIndex0AndFillObjectProps();
                return;
            }

            case "SubFrm":
            {
                var node = item.FindAncestor("Node")?.Title ?? "";
                var frames = _db.Frames.Where(f =>
                    f.Signals.Any(fs =>
                    {
                        var sg = _db.FindSignal(fs.Name);
                        return sg != null && sg.Subscribers.Contains(node);
                    }));
                ShowDetailFrameList($"Subscribed Frames of <{node}>", frames);
                SelectIndex0AndFillObjectProps();
                return;
            }

            case "PubSig":
            {
                var node = item.FindAncestor("Node")?.Title ?? "";
                var sigs = _db.Signals.Where(s =>
                    string.Equals(s.Publisher, node, StringComparison.OrdinalIgnoreCase));
                ShowDetailSignalList($"Published Signals of <{node}>", sigs);
                SelectIndex0AndFillObjectProps();
                return;
            }

            case "SubSig":
            {
                var node = item.FindAncestor("Node")?.Title ?? "";
                var sigs = _db.Signals.Where(s => s.Subscribers.Contains(node));
                ShowDetailSignalList($"Subscribed Signals of <{node}>", sigs);
                SelectIndex0AndFillObjectProps();
                return;
            }

            case "Node" when item.Payload is string nodeName:
            {
                ShowDetailNodes();
                var nrow = NodeRows.FirstOrDefault(r =>
                    string.Equals(r.Node, nodeName, StringComparison.OrdinalIgnoreCase));
                if (nrow != null)
                {
                    _suppressNodeSync = true;
                    SelectedNodeRow = nrow;
                    _suppressNodeSync = false;
                    LdfPropertyBuilder.FillNodeFromRow(nrow, BusProps);
                }
                else
                    LdfPropertyBuilder.FillNodeNameOnly(nodeName, BusProps);
                return;
            }

            case "Command" when item.Payload is string cmd:
                ShowDetailNodes();
                _suppressNodeSync = true;
                SelectedNodeRow = NodeRows.FirstOrDefault();
                _suppressNodeSync = false;
                LdfPropertyBuilder.FillCommand(cmd, BusProps);
                return;

            case "Event" when item.Payload is LdfEventTriggeredFrame et:
            {
                var assoc = et.FrameNames
                    .Select(n => _db.FindFrame(n))
                    .Where(f => f != null)
                    .Cast<object>();
                ShowDetailFrameList($"Associated Frames of <{et.Name}>", assoc);
                if (FrameListRows.Count > 0)
                    SelectIndex0AndFillObjectProps();
                else
                    LdfPropertyBuilder.FillEventTriggered(et, BusProps);
                return;
            }

            case "Sporadic" when item.Payload is LdfSporadicFrame sp:
            {
                var assoc = sp.FrameNames
                    .Select(n => _db.FindFrame(n))
                    .Where(f => f != null)
                    .Cast<object>();
                ShowDetailFrameList($"Associated Frames of <{sp.Name}>", assoc);
                if (FrameListRows.Count > 0)
                    SelectIndex0AndFillObjectProps();
                else
                    LdfPropertyBuilder.FillSporadic(sp, BusProps);
                return;
            }
        }

        switch (item.Payload)
        {
            case LdfFrame f:
                ShowDetailFrame(f);
                SelectIndex0AndFillObjectProps();
                break;
            case LdfSignal s:
                ShowDetailNodes();
                _suppressNodeSync = true;
                SelectedNodeRow = NodeRows.FirstOrDefault();
                _suppressNodeSync = false;
                LdfPropertyBuilder.FillSignal(_db, s, BusProps);
                break;
            default:
                ShowDetailNodes();
                SelectIndex0AndFillObjectProps();
                break;
        }
    }

    private void FillScheduleSelection(ScheduleRow? row)
    {
        if (_db == null || row == null) return;
        SchedulePropTitle = "Schedule Properties";
        ObjectPropTitle = "Properties";
        if (row.IsHeader || row.EntryIndex < 0)
        {
            LdfPropertyBuilder.FillScheduleTable(_db, row.ScheduleIndex, ScheduleProps);
            var frames = CollectScheduleFrames(row.ScheduleIndex);
            if (frames.Count > 0)
            {
                var tname = row.ScheduleIndex >= 0 && row.ScheduleIndex < _db.Schedules.Count
                    ? _db.Schedules[row.ScheduleIndex].Name : "Schedule";
                ShowDetailFrameList($"Frames of Schedule <{tname}>", frames);
                SelectIndex0AndFillObjectProps();
            }
            else
            {
                ShowDetailNodes();
                SelectIndex0AndFillObjectProps();
            }
        }
        else
        {
            LdfPropertyBuilder.FillScheduleSlot(_db, row.ScheduleIndex, row.EntryIndex, ScheduleProps);
            var slot = _db.Schedules[row.ScheduleIndex].Slots[row.EntryIndex];
            var fname = string.IsNullOrEmpty(slot.FrameName)
                ? LdfPropertyBuilder.StripScheduleCommand(slot.Command)
                : slot.FrameName;
            var fr = _db.FindFrame(fname);
            if (fr != null)
            {
                ShowDetailFrame(fr);
                SelectIndex0AndFillObjectProps();
            }
            else
            {
                ShowDetailNodes();
                SelectIndex0AndFillObjectProps();
            }
        }
    }

    private List<object> CollectScheduleFrames(int scheduleIndex)
    {
        var list = new List<object>();
        if (_db == null || scheduleIndex < 0 || scheduleIndex >= _db.Schedules.Count)
            return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in _db.Schedules[scheduleIndex].Slots)
        {
            var fname = string.IsNullOrEmpty(slot.FrameName)
                ? LdfPropertyBuilder.StripScheduleCommand(slot.Command)
                : slot.FrameName;
            if (string.IsNullOrEmpty(fname) || !seen.Add(fname)) continue;
            var fr = _db.FindFrame(fname);
            if (fr != null) list.Add(fr);
        }
        return list;
    }

    private void FillNodeSelection(NodeRow? row)
    {
        if (row == null || _applyingIndex0) return;
        ObjectPropTitle = "Properties";
        LdfPropertyBuilder.FillNodeFromRow(row, BusProps);
    }

    private void FillFrameListSelection(FrameListRow? row)
    {
        if (_db == null || row == null) return;
        ObjectPropTitle = "Properties";
        switch (row.Payload)
        {
            case LdfFrame f:
                LdfPropertyBuilder.FillFrame(_db, f, BusProps);
                break;
            case LdfEventTriggeredFrame e:
                LdfPropertyBuilder.FillEventTriggered(e, BusProps);
                break;
            case LdfSporadicFrame s:
                LdfPropertyBuilder.FillSporadic(s, BusProps);
                break;
        }
    }
}

public sealed class FrameListRow
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Length { get; set; } = "";
    public string Type { get; set; } = "";
    public object? Payload { get; set; }
}
public sealed class RecentFileItem
{
    public int Index { get; set; }
    public string Path { get; set; } = "";
    public string Display { get; set; } = "";
}

public sealed class ScheduleRow
{
    public string Table { get; set; } = "";
    public string Type { get; set; } = "";
    public string Delay { get; set; } = "";
    public string MaxFrame { get; set; } = "";
    public string MinFrame { get; set; } = "";
    public string Period { get; set; } = "";
    public bool IsHeader { get; set; }
    public int ScheduleIndex { get; set; }
    public int EntryIndex { get; set; } = -1;
    public object? Payload { get; set; }
}

public sealed class NodeRow
{
    public string Node { get; set; } = "";
    public string Timebase { get; set; } = "";
    public string Jitter { get; set; } = "";
    public string ConfiguredNad { get; set; } = "";
    public string InitialNad { get; set; } = "";
    public string Protocol { get; set; } = "";
    public string SupplierId { get; set; } = "";
    public string FunctionId { get; set; } = "";
    public string VariantId { get; set; } = "";
    public object? Payload { get; set; }
}

public sealed class FrameSignalRow
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public int StartBit { get; set; }
    public int Length { get; set; }
    public string Publisher { get; set; } = "";
    public string Subscribers { get; set; } = "";
}
