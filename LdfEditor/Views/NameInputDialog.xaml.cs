using System.Windows;

namespace LdfEditor.Views;

public partial class NameInputDialog : Window
{
    private readonly HashSet<string>? _existing;

    public NameInputDialog(string title, string prompt, string initial,
        IEnumerable<string>? existingNames = null)
    {
        _existing = existingNames == null
            ? null
            : new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        DataContext = new NameInputVm { Title = title, Prompt = prompt, Value = initial };
        InitializeComponent();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string ResultName => ((NameInputVm)DataContext).Value.Trim();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var name = ResultName;
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Name cannot be empty.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        /* Vector NewObjectDialogForm → ValidateSingleUniqueNameGeneric_Standard */
        if (_existing != null && _existing.Contains(name))
        {
            MessageBox.Show(this, "Name not unique", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}

public sealed class NameInputVm
{
    public string Title { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string Value { get; set; } = "";
}
