using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LdfEditor.ViewModels;

public enum PropEditorKind
{
    None,
    Text,
    Combo
}

/// <summary>Vector-style property tree node: category (expandable) or name/value leaf.</summary>
public partial class PropNode : ObservableObject
{
    private bool _suppressCommit;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private bool _isCategory;
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isEditable;
    [ObservableProperty] private PropEditorKind _editorKind = PropEditorKind.None;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorTooltip = "";

    /// <summary>Stable key used by MainViewModel to apply edits (e.g. Slot.Delay, Signal.Startbit).</summary>
    public string Key { get; init; } = "";

    public ObservableCollection<string> Options { get; } = new();
    public ObservableCollection<PropNode> Children { get; } = new();

    public Action<PropNode>? CommitHandler { get; set; }

    public bool IsTextEditor => !IsCategory && EditorKind == PropEditorKind.Text;
    public bool IsComboEditor => !IsCategory && EditorKind == PropEditorKind.Combo;
    public bool IsPlainValue => !IsCategory && EditorKind == PropEditorKind.None;

    public static PropNode Category(string title) =>
        new() { Name = title, IsCategory = true, IsExpanded = true };

    public static PropNode Leaf(string name, string value) =>
        new() { Name = name, Value = value, IsCategory = false };

    public void Add(string name, string value) => Children.Add(Leaf(name, value));

    public PropNode AddEditableText(string key, string name, string value, Action<PropNode>? commit = null)
    {
        var n = new PropNode
        {
            Key = key,
            Name = name,
            IsEditable = true,
            EditorKind = PropEditorKind.Text,
            CommitHandler = commit
        };
        n.SetValueSilent(value);
        Children.Add(n);
        return n;
    }

    public PropNode AddEditableCombo(string key, string name, string value, IEnumerable<string> options,
        Action<PropNode>? commit = null)
    {
        var n = new PropNode
        {
            Key = key,
            Name = name,
            IsEditable = true,
            EditorKind = PropEditorKind.Combo,
            CommitHandler = commit
        };
        foreach (var o in options) n.Options.Add(o);
        if (!string.IsNullOrEmpty(value) && !n.Options.Contains(value))
            n.Options.Insert(0, value);
        n.SetValueSilent(value);
        Children.Add(n);
        return n;
    }

    public void SetValueSilent(string value)
    {
        _suppressCommit = true;
        Value = value ?? "";
        _suppressCommit = false;
    }

    public void SetError(string? tooltip)
    {
        HasError = !string.IsNullOrEmpty(tooltip);
        ErrorTooltip = tooltip ?? "";
    }

    public void ClearError() => SetError(null);

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsTextEditor));
        OnPropertyChanged(nameof(IsComboEditor));
        OnPropertyChanged(nameof(IsPlainValue));
        if (_suppressCommit || !IsEditable || CommitHandler == null) return;
        CommitHandler(this);
    }

    partial void OnEditorKindChanged(PropEditorKind value)
    {
        OnPropertyChanged(nameof(IsTextEditor));
        OnPropertyChanged(nameof(IsComboEditor));
        OnPropertyChanged(nameof(IsPlainValue));
    }

    partial void OnIsCategoryChanged(bool value)
    {
        OnPropertyChanged(nameof(IsTextEditor));
        OnPropertyChanged(nameof(IsComboEditor));
        OnPropertyChanged(nameof(IsPlainValue));
    }
}
