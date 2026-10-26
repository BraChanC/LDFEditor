using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LdfEditor.ViewModels;

/// <summary>Vector-style property tree node: category (expandable) or name/value leaf.</summary>
public partial class PropNode : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private bool _isCategory;
    [ObservableProperty] private bool _isExpanded = true;

    public ObservableCollection<PropNode> Children { get; } = new();

    public static PropNode Category(string title)
    {
        return new PropNode { Name = title, IsCategory = true, IsExpanded = true };
    }

    public static PropNode Leaf(string name, string value) =>
        new() { Name = name, Value = value, IsCategory = false };

    public void Add(string name, string value) => Children.Add(Leaf(name, value));
}
