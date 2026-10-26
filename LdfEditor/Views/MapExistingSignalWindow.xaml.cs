using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using LdfEditor.Models;

namespace LdfEditor.Views;

public partial class MapExistingSignalWindow : Window
{
    private readonly MapExistingSignalVm _vm;

    public MapExistingSignalWindow(IEnumerable<LdfSignal> signals)
    {
        /* DataContext before InitializeComponent — avoids VS XAML binding-failure spam. */
        _vm = new MapExistingSignalVm(signals);
        DataContext = _vm;
        InitializeComponent();
    }

    /// <summary>Selected signal when DialogResult is true (OK or double-click — Vector same path).</summary>
    public LdfSignal? SelectedSignal => _vm.SelectedSignal;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSignal == null) return;
        DialogResult = true;
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.SelectedSignal == null) return;
        DialogResult = true;
    }
}

public partial class MapExistingSignalVm : ObservableObject
{
    public ObservableCollection<LdfSignal> Signals { get; } = new();

    [ObservableProperty] private LdfSignal? _selectedSignal;
    [ObservableProperty] private bool _hasSelection;

    public MapExistingSignalVm(IEnumerable<LdfSignal> signals)
    {
        foreach (var s in signals.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            Signals.Add(s);
        if (Signals.Count > 0)
            SelectedSignal = Signals[0];
    }

    partial void OnSelectedSignalChanged(LdfSignal? value) =>
        HasSelection = value != null;
}
