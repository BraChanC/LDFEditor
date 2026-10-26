using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LdfEditor.ViewModels;

public partial class TreeItemViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _kind = "";
    [ObservableProperty] private object? _payload;

    public TreeItemViewModel? Parent { get; private set; }

    public ObservableCollection<TreeItemViewModel> Children { get; } = new();

    public TreeItemViewModel()
    {
        Children.CollectionChanged += OnChildrenChanged;
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (TreeItemViewModel child in e.NewItems)
                child.Parent = this;
        }
    }

    public TreeItemViewModel? FindAncestor(string kind)
    {
        for (var p = Parent; p != null; p = p.Parent)
            if (p.Kind == kind) return p;
        return null;
    }
}
