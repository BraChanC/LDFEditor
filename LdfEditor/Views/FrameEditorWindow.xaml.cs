using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using LdfEditor.Models;
using LdfEditor.ViewModels;

namespace LdfEditor.Views;

public partial class FrameEditorWindow : Window
{
    private readonly LdfDatabase _db;
    private readonly LdfFrame _frame;
    public FrameEditorVm Vm { get; }

    public FrameEditorWindow(LdfDatabase db, LdfFrame frame)
    {
        _db = db;
        _frame = frame;
        Vm = new FrameEditorVm(db, frame);
        DataContext = Vm;
        InitializeComponent();
        Title = $"Edit Frame — {frame.Name}";
        BitLayout.SignalActivated += OpenSignalAt;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Vm.CommitToModel();
        DialogResult = true;
    }

    private void OnSignalGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.SelectedSignal != null)
            OpenSignalAt(Vm.SelectedSignal.Index);
    }

    private void OnEditSignal(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSignal != null)
            OpenSignalAt(Vm.SelectedSignal.Index);
    }

    /// <summary>Vector RemoveMappedSignal: unmap then offer to delete unused definition.</summary>
    private void OnRemoveSignal(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSignal == null) return;
        var name = Vm.SelectedSignal.Name;
        _frame.Signals.RemoveAll(fs => fs.Name == name);
        Vm.RefreshSignals();
        BitLayout.InvalidateVisual();

        bool stillMapped = _db.Frames.Any(f => f.Signals.Any(fs => fs.Name == name));
        if (stillMapped) return;

        var sg = _db.FindSignal(name);
        if (sg == null) return;
        var r = MessageBox.Show(this,
            $"Signal <{name}> is not used any more. Do you want to delete it now ?",
            "Remove Signal", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        _db.Signals.Remove(sg);
        _db.DiagnosticSignals.Remove(sg);
        foreach (var rep in _db.Representations.ToList())
        {
            rep.Signals.RemoveAll(n => n == name);
            if (rep.Signals.Count == 0)
                _db.Representations.Remove(rep);
        }
        Vm.RefreshSignals();
    }

    /// <summary>Vector FrameDialogController.CreateAndMapSignal.</summary>
    private void OnCreateAndMap(object sender, RoutedEventArgs e)
    {
        try
        {
            /* Persist frame props first so unique name / publisher match live model. */
            Vm.CommitToModel();
            var publisher = Vm.EffectivePublisher();
            if (string.IsNullOrEmpty(publisher))
            {
                MessageBox.Show(this, "Please select a Publisher before creating a signal.",
                    "Create and Map Signal", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var name = Vm.UniqueNewSignalName();
            var signal = new LdfSignal
            {
                Name = name,
                SizeBits = 8,
                InitValue = "0",
                Publisher = publisher
            };
            ApplyFrameReceivers(signal);
            _db.Signals.Add(signal);
            MapAndEdit(signal, newlyCreated: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Create and Map Signal",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Vector SignalPDUControl.btnMapExisting → MapExistingElementDialog → MapExistingSignal.</summary>
    private void OnMapExisting(object sender, RoutedEventArgs e)
    {
        try
        {
            Vm.CommitToModel();
            var candidates = Vm.GetMappableSignals();
            if (candidates.Count == 0)
            {
                MessageBox.Show(this,
                    "No unmapped signals available for this publisher.",
                    "Map Existing Signal", MessageBoxButton.OK, MessageBoxImage.Information);
                Vm.RefreshMapButtons();
                return;
            }

            var pick = new MapExistingSignalWindow(candidates) { Owner = this };
            if (pick.ShowDialog() != true || pick.SelectedSignal == null)
                return;

            MapAndEdit(pick.SelectedSignal, newlyCreated: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Map Existing Signal",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Vector MapExistingSignal → EditMappedSignal; Cancel unmaps (and deletes new signal).</summary>
    private void MapAndEdit(LdfSignal signal, bool newlyCreated)
    {
        var name = signal.Name;
        bool already = _frame.Signals.Any(fs =>
            string.Equals(fs.Name, name, StringComparison.OrdinalIgnoreCase));
        int startBit;
        if (!already)
        {
            startBit = Vm.NextFreeStartBit(signal.SizeBits > 0 ? signal.SizeBits : 1);
            var publisher = Vm.EffectivePublisher();
            if (string.IsNullOrEmpty(signal.Publisher))
                signal.Publisher = publisher;
            else if (!string.IsNullOrEmpty(publisher)
                     && !string.Equals(signal.Publisher, publisher, StringComparison.OrdinalIgnoreCase))
                signal.Publisher = publisher;
            ApplyFrameReceivers(signal);
            _frame.Signals.Add(new LdfFrameSignal { Name = signal.Name, StartBit = startBit });
        }
        else
            startBit = _frame.Signals.First(fs =>
                string.Equals(fs.Name, name, StringComparison.OrdinalIgnoreCase)).StartBit;

        var dlg = new SignalEditorWindow(_db, signal, _frame.Name, startBit) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            Vm.RefreshSignals();
            SelectMapped(signal.Name);
            BitLayout.InvalidateVisual();
            return;
        }

        _frame.Signals.RemoveAll(fs =>
            string.Equals(fs.Name, name, StringComparison.OrdinalIgnoreCase));
        if (newlyCreated)
        {
            _db.Signals.Remove(signal);
            foreach (var rep in _db.Representations.ToList())
            {
                rep.Signals.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                if (rep.Signals.Count == 0)
                    _db.Representations.Remove(rep);
            }
        }
        Vm.RefreshSignals();
        BitLayout.InvalidateVisual();
    }

    /// <summary>Vector MapExistingSignal: copy current frame receivers onto the signal.</summary>
    private void ApplyFrameReceivers(LdfSignal signal)
    {
        var subs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sub in signal.Subscribers) subs.Add(sub);
        foreach (var fs in _frame.Signals)
        {
            var sg = _db.FindSignal(fs.Name);
            if (sg == null) continue;
            foreach (var sub in sg.Subscribers) subs.Add(sub);
        }
        signal.Subscribers.Clear();
        foreach (var s in subs) signal.Subscribers.Add(s);
    }

    private void OpenSignalAt(int frameSigIndex)
    {
        if (frameSigIndex < 0 || frameSigIndex >= _frame.Signals.Count) return;
        var fs = _frame.Signals[frameSigIndex];
        var sg = _db.FindSignal(fs.Name);
        if (sg == null) return;
        var dlg = new SignalEditorWindow(_db, sg, _frame.Name, fs.StartBit) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            Vm.RefreshSignals();
            SelectMapped(sg.Name);
            BitLayout.InvalidateVisual();
        }
    }

    private void SelectMapped(string signalName)
    {
        var row = Vm.Signals.FirstOrDefault(r => r.Name == signalName);
        if (row != null)
            Vm.SelectedSignal = row;
    }
}

public partial class FrameEditorVm : ObservableObject
{
    public LdfDatabase Database { get; }
    public LdfFrame Frame { get; }
    public string Subscribers { get; private set; }
    public ObservableCollection<string> PublisherOptions { get; } = new();
    public ObservableCollection<FrameSignalRow> Signals { get; } = new();

    private bool _loading;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _length = "";
    [ObservableProperty] private string _idHex = "";
    [ObservableProperty] private string _publisher = "";
    [ObservableProperty] private FrameSignalRow? _selectedSignal;
    [ObservableProperty] private int _selectedSignalIndex = -1;
    [ObservableProperty] private int _mapTabIndex;
    [ObservableProperty] private bool _hasSignalSelection;
    [ObservableProperty] private bool _canCreateAndMap;
    [ObservableProperty] private bool _canMapExisting;

    public FrameEditorVm(LdfDatabase db, LdfFrame frame)
    {
        Database = db;
        Frame = frame;
        _loading = true;
        Name = frame.Name;
        Length = frame.Length.ToString();
        IdHex = $"0x{frame.Id:X}";
        Publisher = frame.Publisher;
        _loading = false;
        Subscribers = "";
        if (!string.IsNullOrEmpty(frame.Publisher))
            PublisherOptions.Add(frame.Publisher);
        if (!string.IsNullOrEmpty(db.Master) && !PublisherOptions.Contains(db.Master))
            PublisherOptions.Add(db.Master);
        foreach (var s in db.Slaves)
            if (!PublisherOptions.Contains(s)) PublisherOptions.Add(s);

        RefreshSignals();
    }

    /// <summary>Frame sender used for Create/Map (UI Publisher, then Frame, then Master).</summary>
    public string EffectivePublisher()
    {
        if (!string.IsNullOrWhiteSpace(Publisher)) return Publisher.Trim();
        if (!string.IsNullOrWhiteSpace(Frame.Publisher)) return Frame.Publisher.Trim();
        return (Database.Master ?? "").Trim();
    }

    /// <summary>Vector InitMappableSignalDataSource: same sender, not already mapped on this frame.</summary>
    public List<LdfSignal> GetMappableSignals()
    {
        var publisher = EffectivePublisher();
        if (string.IsNullOrEmpty(publisher)) return new List<LdfSignal>();

        var mapped = new HashSet<string>(
            Frame.Signals.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);

        return Database.Signals
            .Where(s => !mapped.Contains(s.Name)
                        && string.Equals(s.Publisher, publisher, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void RefreshMapButtons()
    {
        CanCreateAndMap = !string.IsNullOrEmpty(EffectivePublisher());
        CanMapExisting = GetMappableSignals().Count > 0;
    }

    public void CommitToModel()
    {
        var newName = (Name ?? "").Trim();
        if (!string.IsNullOrEmpty(newName) && newName != Frame.Name
            && !Database.Frames.Any(f => f != Frame && f.Name == newName))
        {
            foreach (var t in Database.Schedules)
                foreach (var slot in t.Slots)
                    if (slot.FrameName == Frame.Name) slot.FrameName = newName;
            Frame.Name = newName;
        }
        if (int.TryParse(Length?.Trim(), out var len) && len > 0)
            Frame.Length = len;
        var idText = (IdHex ?? "").Trim();
        if (idText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(idText[2..], System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var id))
                Frame.Id = id;
        }
        else if (int.TryParse(idText, out var idDec))
            Frame.Id = idDec;

        /* OK also persists publisher (ComboBox already applied live). */
        ApplyPublisher(Publisher, refreshUi: false);
    }

    /// <summary>Vector FrameRxTxControl.cbTransmitter_SelectedIndexChanged → SetNewSenderNode.</summary>
    partial void OnPublisherChanged(string value)
    {
        if (_loading) return;
        ApplyPublisher(value, refreshUi: true);
    }

    private void ApplyPublisher(string? node, bool refreshUi)
    {
        if (string.IsNullOrEmpty(node)) return;
        if (string.Equals(Frame.Publisher, node, StringComparison.Ordinal))
        {
            if (refreshUi) RefreshSignals();
            return;
        }

        Frame.Publisher = node;
        foreach (var fs in Frame.Signals)
        {
            var sg = Database.FindSignal(fs.Name);
            if (sg == null) continue;
            /* Skip signals also mapped on another frame with a different publisher. */
            bool sharedElsewhere = Database.Frames.Any(f =>
                !ReferenceEquals(f, Frame)
                && f.Signals.Any(x => x.Name == sg.Name)
                && !string.Equals(f.Publisher, node, StringComparison.OrdinalIgnoreCase));
            if (!sharedElsewhere)
                sg.Publisher = node;
        }
        if (refreshUi) RefreshSignals();
        else RefreshMapButtons();
    }

    public void RefreshSignals()
    {
        var keep = SelectedSignal?.Name;
        Signals.Clear();
        var subs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fs in Frame.Signals)
        {
            var sg = Database.FindSignal(fs.Name);
            Signals.Add(new FrameSignalRow
            {
                Index = Signals.Count,
                Name = fs.Name,
                StartBit = fs.StartBit,
                Length = sg?.SizeBits ?? 1,
                Publisher = sg?.Publisher ?? "",
                Subscribers = sg == null ? "" : string.Join(", ", sg.Subscribers)
            });
            if (sg != null)
                foreach (var sub in sg.Subscribers) subs.Add(sub);
        }
        Subscribers = string.Join(", ", subs);
        OnPropertyChanged(nameof(Subscribers));
        OnPropertyChanged(nameof(Frame));

        if (!string.IsNullOrEmpty(keep))
        {
            var row = Signals.FirstOrDefault(r =>
                string.Equals(r.Name, keep, StringComparison.OrdinalIgnoreCase));
            SelectedSignal = row;
            SelectedSignalIndex = row?.Index ?? -1;
        }
        else
        {
            SelectedSignal = null;
            SelectedSignalIndex = -1;
        }
        HasSignalSelection = SelectedSignal != null;
        RefreshMapButtons();
    }

    public string UniqueNewSignalName()
    {
        var frameName = string.IsNullOrWhiteSpace(Name) ? Frame.Name : Name.Trim();
        var baseName = $"{frameName}_NewSignal";
        if (!NameTaken(baseName)) return baseName;
        for (int i = 1; i < 10000; i++)
        {
            var n = $"{baseName}_{i}";
            if (!NameTaken(n)) return n;
        }
        return baseName + Guid.NewGuid().ToString("N")[..4];
    }

    private bool NameTaken(string name) =>
        Database.Signals.Any(s => s.Name == name)
        || Database.DiagnosticSignals.Any(s => s.Name == name);

    public int NextFreeStartBit(int lengthBits)
    {
        if (lengthBits < 1) lengthBits = 1;
        int capacity = Math.Max(1, Frame.Length) * 8;
        var occupied = new bool[capacity];
        foreach (var fs in Frame.Signals)
        {
            var sg = Database.FindSignal(fs.Name);
            int len = sg?.SizeBits ?? 1;
            for (int i = 0; i < len; i++)
            {
                int b = fs.StartBit + i;
                if (b >= 0 && b < capacity) occupied[b] = true;
            }
        }
        for (int i = 0; i <= capacity - lengthBits; i++)
        {
            bool free = true;
            for (int j = 0; j < lengthBits; j++)
            {
                if (occupied[i + j]) { free = false; break; }
            }
            if (free) return i;
        }
        return 0;
    }

    partial void OnSelectedSignalChanged(FrameSignalRow? value)
    {
        HasSignalSelection = value != null;
        if (value != null && SelectedSignalIndex != value.Index)
            SelectedSignalIndex = value.Index;
    }

    partial void OnSelectedSignalIndexChanged(int value)
    {
        if (value < 0)
        {
            if (SelectedSignal != null) SelectedSignal = null;
            HasSignalSelection = false;
            return;
        }
        var row = Signals.FirstOrDefault(r => r.Index == value);
        if (row != null && !ReferenceEquals(SelectedSignal, row))
            SelectedSignal = row;
        HasSignalSelection = SelectedSignal != null;
    }
}
