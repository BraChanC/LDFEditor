using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LdfEditor.Models;

namespace LdfEditor.Views;

public partial class SignalEditorWindow : Window
{
    private readonly SignalEditorVm _vm;

    public SignalEditorWindow(LdfDatabase db, LdfSignal signal, string? frameName = null, int startBit = -1)
    {
        _vm = new SignalEditorVm(db, signal, frameName, startBit, this);
        DataContext = _vm;
        InitializeComponent();
        Title = "Edit Signal";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        _vm.CommitToModel();
        DialogResult = true;
    }

    private void OnEcuDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        _vm.AddSubscriberCommand.Execute(null);

    private void OnSubscriberDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        _vm.RemoveSubscriberCommand.Execute(null);
}

public partial class SignalEditorVm : ObservableObject
{
    private readonly LdfDatabase _db;
    private readonly LdfSignal _signal;
    private readonly Window _owner;
    private readonly string _originalName;
    private string? _frameName;
    private bool _loading;

    public string Publisher { get; }
    public string PositionLabel { get; }
    public string ByteOrder { get; }

    public IReadOnlyList<string> SignalTypes { get; } = ["Scalar", "Byte Array"];
    public IReadOnlyList<string> ByteOrders { get; } = ["Intel", "Motorola"];
    public ObservableCollection<string> EncodingNames { get; } = new();
    public ObservableCollection<string> EcuList { get; } = new();
    public ObservableCollection<string> Subscribers { get; } = new();
    public ObservableCollection<LogicalRow> LogicalRows { get; } = new();
    public ObservableCollection<PhysicalRow> PhysicalRows { get; } = new();

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _signalType = "Scalar";
    [ObservableProperty] private string _length = "";
    [ObservableProperty] private string _initValue = "";
    [ObservableProperty] private string _startBit = "";
    [ObservableProperty] private string _selectedEncodingName = "<None>";
    [ObservableProperty] private int _encodingPage;
    [ObservableProperty] private bool _canEditEncoding;
    [ObservableProperty] private string _physMin = "";
    [ObservableProperty] private string _physMax = "";
    [ObservableProperty] private string _physFactor = "";
    [ObservableProperty] private string _physOffset = "";
    [ObservableProperty] private string _physUnit = "";
    [ObservableProperty] private bool _isAscii;
    [ObservableProperty] private bool _isBcd;
    [ObservableProperty] private bool _isFloat;
    [ObservableProperty] private string? _selectedEcu;
    [ObservableProperty] private string? _selectedSubscriber;
    [ObservableProperty] private bool _isScalarType = true;
    [ObservableProperty] private bool _isByteArrayType;
    /* Vector HandleCheckedChange: ASCII/BCD/Float locks the other encoding tabs. */
    [ObservableProperty] private bool _canEditScalarEncoding = true;
    /* Vector ShowAndActivateEncodingInfo / btnAddFirstPhysEncoding. */
    [ObservableProperty] private bool _showAddPhysicalEncoding;
    [ObservableProperty] private bool _canEditPhysicalFields;
    [ObservableProperty] private string _physicalEncodingInfo = "";
    /* Vector SFPConfig.SupportLINFloatEncoding — LDF Explorer keeps this false. */
    [ObservableProperty] private bool _supportLinFloatEncoding;
    [ObservableProperty] private bool _showAsciiBcdHint;

    public SignalEditorVm(LdfDatabase db, LdfSignal s, string? frameName, int startBit, Window owner)
    {
        _db = db;
        _signal = s;
        _owner = owner;
        _originalName = s.Name;
        Name = s.Name;
        _loading = true;
        SignalType = !string.IsNullOrEmpty(s.InitValue) && s.InitValue.StartsWith('{')
            ? "Byte Array" : "Scalar";
        UpdateTypeFlags();
        Length = s.SizeBits.ToString(CultureInfo.InvariantCulture);
        InitValue = FormatInit(s.InitValue);
        _loading = false;
        Publisher = s.Publisher;
        ByteOrder = db.IsMotorolaByteOrder ? "Motorola" : "Intel";

        if (startBit < 0)
        {
            foreach (var f in db.Frames)
            {
                var fs = f.Signals.FirstOrDefault(x => x.Name == s.Name);
                if (fs == null) continue;
                startBit = fs.StartBit;
                frameName ??= f.Name;
                break;
            }
        }
        _frameName = frameName;
        PositionLabel = string.IsNullOrEmpty(frameName)
            ? "Position on Frame [Bit]:"
            : $"Position on Frame <{frameName}> [Bit]:";
        StartBit = startBit >= 0 ? startBit.ToString(CultureInfo.InvariantCulture) : "";

        foreach (var sub in s.Subscribers) Subscribers.Add(sub);
        var isSub = new HashSet<string>(s.Subscribers, StringComparer.OrdinalIgnoreCase);
        /* Vector InitAllNodesNotReceivers: all channel nodes NOT already receivers
           (publisher stays in ECUs list until moved to Subscribers). */
        void AddEcu(string node)
        {
            if (string.IsNullOrEmpty(node) || isSub.Contains(node)) return;
            if (!EcuList.Contains(node)) EcuList.Add(node);
        }
        AddEcu(db.Master);
        foreach (var slave in db.Slaves) AddEcu(slave);
        /* Also include any node seen as publisher/subscriber elsewhere */
        foreach (var sig in db.Signals)
        {
            AddEcu(sig.Publisher);
            foreach (var sub in sig.Subscribers) AddEcu(sub);
        }
        SelectedEcu = EcuList.FirstOrDefault();
        SelectedSubscriber = Subscribers.FirstOrDefault();

        RebuildEncodingList();
        var linked = db.FindEncodingForSignal(s.Name);
        _loading = true;
        SelectedEncodingName = linked?.Name ?? "<None>";
        _loading = false;
        LoadEncodingUi(SelectedEncodingName);
        RefreshEncodingCommands();
    }

    partial void OnSignalTypeChanged(string value)
    {
        UpdateTypeFlags();
        if (_loading) return;
        /* Vector ActivateCorrectControls + InitValueIsByteArray toggle. */
        if (IsByteArrayType)
        {
            var cur = (InitValue ?? "").Trim();
            if (!cur.StartsWith('{'))
            {
                var n = "0";
                if (cur.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(cur[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h))
                    n = h.ToString(CultureInfo.InvariantCulture);
                else if (int.TryParse(cur, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d))
                    n = d.ToString(CultureInfo.InvariantCulture);
                InitValue = "{" + n + "}";
            }
        }
        else
        {
            var cur = (InitValue ?? "").Trim();
            if (cur.StartsWith('{') && cur.EndsWith('}'))
            {
                var inner = cur[1..^1].Trim();
                var first = inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault() ?? "0";
                InitValue = FormatInit(first);
            }
        }
    }

    private void UpdateTypeFlags()
    {
        IsByteArrayType = string.Equals(SignalType, "Byte Array", StringComparison.OrdinalIgnoreCase);
        IsScalarType = !IsByteArrayType;
    }

    private void RebuildEncodingList()
    {
        EncodingNames.Clear();
        EncodingNames.Add("<None>");
        foreach (var enc in _db.Encodings.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            EncodingNames.Add(enc.Name);
    }

    partial void OnSelectedEncodingNameChanged(string value)
    {
        if (!_loading)
        {
            LinkSignalToEncoding(value);
            LoadEncodingUi(value);
        }
        CanEditEncoding = !string.IsNullOrEmpty(value) && value != "<None>";
        RefreshEncodingCommands();
    }

    partial void OnCanEditEncodingChanged(bool value)
    {
        RefreshEncodingCommands();
        SyncScalarEncodingLock();
    }

    private void RefreshEncodingCommands()
    {
        RenameEncodingCommand.NotifyCanExecuteChanged();
        DeleteEncodingCommand.NotifyCanExecuteChanged();
    }

    private void LinkSignalToEncoding(string encodingName)
    {
        /* Remove signal from all representations, then attach to selected encoding. */
        foreach (var rep in _db.Representations.ToList())
        {
            rep.Signals.RemoveAll(n => n == _signal.Name);
            if (rep.Signals.Count == 0)
                _db.Representations.Remove(rep);
        }
        if (string.IsNullOrEmpty(encodingName) || encodingName == "<None>") return;
        var target = _db.Representations.FirstOrDefault(r => r.Encoding == encodingName);
        if (target == null)
        {
            target = new LdfRepresentation { Encoding = encodingName };
            _db.Representations.Add(target);
        }
        if (!target.Signals.Contains(_signal.Name))
            target.Signals.Add(_signal.Name);
    }

    private void LoadEncodingUi(string encodingName)
    {
        LogicalRows.Clear();
        PhysicalRows.Clear();
        PhysMin = PhysMax = PhysFactor = PhysOffset = PhysUnit = "";
        _loading = true;
        IsAscii = IsBcd = IsFloat = false;
        _loading = false;
        CanEditEncoding = !string.IsNullOrEmpty(encodingName) && encodingName != "<None>";

        var enc = _db.Encodings.FirstOrDefault(e => e.Name == encodingName);
        if (enc == null)
        {
            RefreshPhysicalEncodingUi();
            return;
        }

        _loading = true;
        foreach (var v in enc.Values)
        {
            if (v.Kind == "logical")
                LogicalRows.Add(new LogicalRow { Value = v.Value.ToString(CultureInfo.InvariantCulture), Description = v.Text });
            else if (v.Kind == "physical")
            {
                PhysicalRows.Add(new PhysicalRow
                {
                    Min = v.MinValue.ToString(CultureInfo.InvariantCulture),
                    Max = v.MaxValue.ToString(CultureInfo.InvariantCulture),
                    Factor = v.Scale.ToString("G", CultureInfo.InvariantCulture),
                    Offset = v.Offset.ToString("G", CultureInfo.InvariantCulture),
                    Unit = v.Text
                });
            }
            else if (v.Kind == "ascii") IsAscii = true;
            else if (v.Kind == "bcd") IsBcd = true;
            else if (v.Kind == "float") IsFloat = true;
        }
        _loading = false;
        SyncScalarEncodingLock();

        if (PhysicalRows.Count >= 1)
        {
            var first = PhysicalRows[0];
            PhysMin = first.Min;
            PhysMax = first.Max;
            PhysFactor = first.Factor;
            PhysOffset = first.Offset;
            PhysUnit = first.Unit;
        }

        if (PhysicalRows.Count == 1)
            EncodingPage = 0;
        else if (PhysicalRows.Count > 1)
            EncodingPage = 2;
        else if (LogicalRows.Count > 0)
            EncodingPage = 1;
        else if (IsAscii || IsBcd || IsFloat)
            EncodingPage = 3;
        else
            EncodingPage = 0;

        RefreshPhysicalEncodingUi();
    }

    /// <summary>Vector ShowAndActivateEncodingInfo + field enable rules.</summary>
    private void RefreshPhysicalEncodingUi()
    {
        int n = PhysicalRows.Count;
        if (!CanEditEncoding)
        {
            ShowAddPhysicalEncoding = false;
            CanEditPhysicalFields = false;
            PhysicalEncodingInfo = "";
            return;
        }

        if (n == 0)
        {
            ShowAddPhysicalEncoding = CanEditScalarEncoding;
            CanEditPhysicalFields = false;
            PhysicalEncodingInfo = "No explicit Physical Encoding available.";
        }
        else if (n == 1)
        {
            ShowAddPhysicalEncoding = false;
            CanEditPhysicalFields = CanEditScalarEncoding;
            PhysicalEncodingInfo = "";
        }
        else
        {
            ShowAddPhysicalEncoding = false;
            CanEditPhysicalFields = CanEditScalarEncoding;
            PhysicalEncodingInfo =
                $"Showing the first of {n} Physical Encodings. See Multi-Range Tab to see all Physical Encodings.";
        }
        AddPhysicalEncodingCommand.NotifyCanExecuteChanged();
        AddPhysicalRowCommand.NotifyCanExecuteChanged();
    }

    private void SyncPhysFieldsFromFirstRow()
    {
        if (PhysicalRows.Count == 0)
        {
            PhysMin = PhysMax = PhysFactor = PhysOffset = PhysUnit = "";
            return;
        }
        var first = PhysicalRows[0];
        PhysMin = first.Min;
        PhysMax = first.Max;
        PhysFactor = first.Factor;
        PhysOffset = first.Offset;
        PhysUnit = first.Unit;
    }

    private void SyncFirstRowFromPhysFields()
    {
        if (PhysicalRows.Count == 0) return;
        PhysicalRows[0].Min = PhysMin ?? "0";
        PhysicalRows[0].Max = PhysMax ?? "0";
        PhysicalRows[0].Factor = string.IsNullOrWhiteSpace(PhysFactor) ? "1" : PhysFactor;
        PhysicalRows[0].Offset = string.IsNullOrWhiteSpace(PhysOffset) ? "0" : PhysOffset;
        PhysicalRows[0].Unit = PhysUnit ?? "";
    }

    private bool CanAddPhysicalEncoding() =>
        CanEditEncoding && CanEditScalarEncoding && PhysicalRows.Count == 0;

    private bool CanEditEncodingRows() => CanEditEncoding && CanEditScalarEncoding;

    /// <summary>Vector btnAddFirstPhysEncoding → AddPhysicalEncoding.</summary>
    [RelayCommand(CanExecute = nameof(CanAddPhysicalEncoding))]
    private void AddPhysicalEncoding() => AddPhysicalRowCore(firstOnly: true);

    /// <summary>Vector Multi-Range grid + button → AddPhysicalEncoding.</summary>
    [RelayCommand(CanExecute = nameof(CanEditEncodingRows))]
    private void AddPhysicalRow() => AddPhysicalRowCore(firstOnly: false);

    private void AddPhysicalRowCore(bool firstOnly)
    {
        if (!CanEditEncodingRows()) return;
        if (firstOnly && PhysicalRows.Count > 0) return;
        var enc = _db.Encodings.FirstOrDefault(e => e.Name == SelectedEncodingName);
        if (enc == null) return;

        /* Vector: first → bounds 0; next → highestUpper+1. */
        long bound = 0;
        if (PhysicalRows.Count > 0)
        {
            long hi = -1;
            foreach (var r in PhysicalRows)
            {
                if (TryParseLong(r.Max, out var m) && m > hi) hi = m;
            }
            bound = hi + 1;
        }

        var row = new PhysicalRow
        {
            Min = bound.ToString(CultureInfo.InvariantCulture),
            Max = bound.ToString(CultureInfo.InvariantCulture),
            Factor = "1",
            Offset = "0",
            Unit = ""
        };
        PhysicalRows.Add(row);
        if (PhysicalRows.Count == 1)
            SyncPhysFieldsFromFirstRow();

        enc.Values.Add(new LdfEncodingValue
        {
            Kind = "physical",
            MinValue = bound,
            MaxValue = bound,
            Scale = 1,
            Offset = 0,
            Text = ""
        });

        RefreshPhysicalEncodingUi();
    }

    [RelayCommand]
    private void RemovePhysicalRow(PhysicalRow? row)
    {
        if (row == null || !CanEditEncodingRows()) return;
        SyncFirstRowFromPhysFields();
        int idx = PhysicalRows.IndexOf(row);
        PhysicalRows.Remove(row);
        var enc = _db.Encodings.FirstOrDefault(e => e.Name == SelectedEncodingName);
        if (enc != null)
        {
            var phys = enc.Values.Where(v => v.Kind == "physical").ToList();
            if (idx >= 0 && idx < phys.Count)
                enc.Values.Remove(phys[idx]);
        }
        SyncPhysFieldsFromFirstRow();
        RefreshPhysicalEncodingUi();
    }

    [RelayCommand(CanExecute = nameof(CanEditEncodingRows))]
    private void AddLogicalRow()
    {
        if (!CanEditEncodingRows()) return;
        LogicalRows.Add(new LogicalRow { Value = "0", Description = "" });
    }

    [RelayCommand]
    private void RemoveLogicalRow(LogicalRow? row)
    {
        if (row == null || !CanEditEncodingRows()) return;
        LogicalRows.Remove(row);
    }

    private bool CanMutateEncoding() => CanEditEncoding;

    [RelayCommand(CanExecute = nameof(CanMutateEncoding))]
    private void RenameEncoding()
    {
        if (!CanEditEncoding) return;
        var dlg = new NameInputDialog("Rename Encoding", "New encoding name:", SelectedEncodingName)
        {
            Owner = _owner
        };
        if (dlg.ShowDialog() != true) return;
        var newName = dlg.ResultName;
        if (newName == SelectedEncodingName) return;
        if (_db.Encodings.Any(e => e.Name == newName) || newName == "<None>")
        {
            MessageBox.Show(_owner, $"Encoding '{newName}' already exists.", "Rename",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var enc = _db.Encodings.FirstOrDefault(e => e.Name == SelectedEncodingName);
        if (enc == null) return;
        var old = enc.Name;
        enc.Name = newName;
        foreach (var rep in _db.Representations.Where(r => r.Encoding == old))
            rep.Encoding = newName;
        RebuildEncodingList();
        _loading = true;
        SelectedEncodingName = newName;
        _loading = false;
        CanEditEncoding = true;
    }

    [RelayCommand]
    private void CreateEncoding()
    {
        /* Vector: auto-name "{Signal}_Encoding", no create dialog. */
        var baseName = $"{(string.IsNullOrWhiteSpace(Name) ? _originalName : Name.Trim())}_Encoding";
        var name = baseName;
        int i = 1;
        while (_db.Encodings.Any(e => e.Name == name) || EncodingNames.Contains(name))
            name = $"{baseName}_{i++}";

        _db.Encodings.Add(new LdfEncoding { Name = name });
        RebuildEncodingList();
        SelectedEncodingName = name;
        MessageBox.Show(_owner,
            $"Encoding type '{name}' created and assigned to signal '{Name}'.",
            "Create", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand(CanExecute = nameof(CanMutateEncoding))]
    private void DeleteEncoding()
    {
        if (!CanEditEncoding) return;
        var name = SelectedEncodingName;
        /* Vector DeleteEncodingDefinition: warn if other signals still use it. */
        var users = _db.Representations
            .Where(r => r.Encoding == name)
            .SelectMany(r => r.Signals)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var other = users.Where(n => !string.Equals(n, _signal.Name, StringComparison.Ordinal)).ToList();
        string msg = other.Count > 0
            ? $"Delete encoding '{name}'?\nAlso used by:\n{string.Join("\n", other.Select(n => $"  {n}"))}"
            : $"Delete encoding '{name}'?\nSignals using it will be unlinked.";
        var r = MessageBox.Show(_owner, msg, "Delete Encoding", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        _db.Encodings.RemoveAll(e => e.Name == name);
        _db.Representations.RemoveAll(rep => rep.Encoding == name);
        RebuildEncodingList();
        SelectedEncodingName = "<None>";
    }

    public void CommitToModel()
    {
        CommitSignalProperties();
        LinkSignalToEncoding(SelectedEncodingName);
        if (!CanEditEncoding) return;
        var enc = _db.Encodings.FirstOrDefault(e => e.Name == SelectedEncodingName);
        if (enc == null) return;

        /* Vector keeps Physical + Logical + ASCII/BCD on the same EncodingDefinition. */
        SyncFirstRowFromPhysFields();
        enc.Values.Clear();

        if (IsAscii)
            enc.Values.Add(new LdfEncodingValue { Kind = "ascii" });
        else if (IsBcd)
            enc.Values.Add(new LdfEncodingValue { Kind = "bcd" });
        else if (SupportLinFloatEncoding && IsFloat)
            enc.Values.Add(new LdfEncodingValue { Kind = "float" });
        else
        {
            foreach (var row in PhysicalRows)
            {
                if (!TryParseLong(row.Min, out var min) || !TryParseLong(row.Max, out var max)) continue;
                TryParseDouble(row.Factor, out var factor);
                TryParseDouble(row.Offset, out var offset);
                enc.Values.Add(new LdfEncodingValue
                {
                    Kind = "physical",
                    MinValue = min,
                    MaxValue = max,
                    Scale = factor == 0 ? 1 : factor,
                    Offset = offset,
                    Text = row.Unit ?? ""
                });
            }
            foreach (var row in LogicalRows.Where(r => !string.IsNullOrWhiteSpace(r.Value)))
            {
                if (!TryParseLong(row.Value, out var val)) continue;
                enc.Values.Add(new LdfEncodingValue { Kind = "logical", Value = val, Text = row.Description ?? "" });
            }
        }
    }

    private void CommitSignalProperties()
    {
        var newName = (Name ?? "").Trim();
        if (string.IsNullOrEmpty(newName))
            newName = _originalName;

        if (!string.Equals(newName, _originalName, StringComparison.Ordinal))
        {
            bool clash = _db.Signals.Any(s => s != _signal && s.Name == newName)
                         || _db.DiagnosticSignals.Any(s => s != _signal && s.Name == newName);
            if (clash)
            {
                MessageBox.Show(_owner, $"Signal '{newName}' already exists. Name was not changed.",
                    "Edit Signal", MessageBoxButton.OK, MessageBoxImage.Warning);
                newName = _originalName;
                Name = _originalName;
            }
            else
            {
                foreach (var fr in _db.Frames)
                    foreach (var fs in fr.Signals.Where(x => x.Name == _originalName))
                        fs.Name = newName;
                foreach (var rep in _db.Representations)
                    for (int i = 0; i < rep.Signals.Count; i++)
                        if (rep.Signals[i] == _originalName)
                            rep.Signals[i] = newName;
                _signal.Name = newName;
            }
        }

        if (int.TryParse(Length?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits)
            && bits > 0)
            _signal.SizeBits = bits;

        var init = (InitValue ?? "").Trim();
        if (init.Length == 0) init = "0";
        if (IsByteArrayType)
        {
            if (!init.StartsWith('{'))
            {
                var parts = init.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var nums = new List<string>();
                foreach (var p in parts)
                {
                    if (p.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(p[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hb))
                        nums.Add(hb.ToString(CultureInfo.InvariantCulture));
                    else if (int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var db))
                        nums.Add(db.ToString(CultureInfo.InvariantCulture));
                }
                _signal.InitValue = nums.Count > 0 ? "{" + string.Join(", ", nums) + "}" : "{0}";
            }
            else
                _signal.InitValue = init;
        }
        else if (init.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(init[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            _signal.InitValue = hex.ToString(CultureInfo.InvariantCulture);
        else
            _signal.InitValue = init.StartsWith('{') ? "0" : init;

        if (!string.IsNullOrEmpty(_frameName)
            && int.TryParse(StartBit?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sb)
            && sb >= 0)
        {
            var fr = _db.FindFrame(_frameName);
            var fs = fr?.Signals.FirstOrDefault(x => x.Name == _signal.Name);
            if (fs != null) fs.StartBit = sb;
        }

        _signal.Subscribers.Clear();
        foreach (var sub in Subscribers)
            if (!string.IsNullOrEmpty(sub))
                _signal.Subscribers.Add(sub);
    }

    [RelayCommand]
    private void AddSubscriber()
    {
        if (string.IsNullOrEmpty(SelectedEcu)) return;
        if (Subscribers.Contains(SelectedEcu)) return;
        Subscribers.Add(SelectedEcu);
        EcuList.Remove(SelectedEcu);
        SelectedEcu = EcuList.FirstOrDefault();
        SelectedSubscriber = Subscribers.LastOrDefault();
    }

    [RelayCommand]
    private void RemoveSubscriber()
    {
        if (string.IsNullOrEmpty(SelectedSubscriber)) return;
        var node = SelectedSubscriber;
        Subscribers.Remove(node);
        if (!EcuList.Contains(node))
            EcuList.Add(node);
        SelectedSubscriber = Subscribers.FirstOrDefault();
        SelectedEcu = node;
    }

    [RelayCommand]
    private void EditInitValue()
    {
        var dlg = new ByteArrayInitDialog(InitValue) { Owner = _owner };
        if (dlg.ShowDialog() == true)
            InitValue = dlg.ResultValue;
    }

    partial void OnIsAsciiChanged(bool value)
    {
        if (_loading) { SyncScalarEncodingLock(); return; }
        if (value)
        {
            _loading = true;
            IsBcd = IsFloat = false;
            _loading = false;
        }
        SyncScalarEncodingLock();
    }

    partial void OnIsBcdChanged(bool value)
    {
        if (_loading) { SyncScalarEncodingLock(); return; }
        if (value)
        {
            _loading = true;
            IsAscii = IsFloat = false;
            _loading = false;
        }
        SyncScalarEncodingLock();
    }

    partial void OnIsFloatChanged(bool value)
    {
        if (_loading) { SyncScalarEncodingLock(); return; }
        if (value)
        {
            _loading = true;
            IsAscii = IsBcd = false;
            _loading = false;
        }
        SyncScalarEncodingLock();
    }

    private void SyncScalarEncodingLock()
    {
        bool typed = IsAscii || IsBcd || (SupportLinFloatEncoding && IsFloat);
        CanEditScalarEncoding = CanEditEncoding && !typed;
        ShowAsciiBcdHint = typed;
        RefreshPhysicalEncodingUi();
        AddLogicalRowCommand.NotifyCanExecuteChanged();
        AddPhysicalRowCommand.NotifyCanExecuteChanged();
    }

    private static bool TryParseLong(string? s, out long v)
    {
        v = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    }

    private static bool TryParseDouble(string? s, out double v)
    {
        v = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    private static string FormatInit(string init)
    {
        if (string.IsNullOrEmpty(init)) return "0";
        if (init.StartsWith('{')) return init;
        if (int.TryParse(init, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n <= 9)
            return n.ToString(CultureInfo.InvariantCulture);
        if (init.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return init;
        if (int.TryParse(init, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return "0x" + v.ToString("X", CultureInfo.InvariantCulture);
        return init;
    }

    public sealed class LogicalRow
    {
        public string Value { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public sealed class PhysicalRow
    {
        public string Min { get; set; } = "";
        public string Max { get; set; } = "";
        public string Factor { get; set; } = "";
        public string Offset { get; set; } = "";
        public string Unit { get; set; } = "";
    }
}
