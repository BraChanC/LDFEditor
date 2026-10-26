using System.Windows;

namespace LdfEditor.Views;

public partial class ByteArrayInitDialog : Window
{
    public ByteArrayInitDialog(string initial)
    {
        InitializeComponent();
        ValueBox.Text = initial ?? "";
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string ResultValue => ValueBox.Text.Trim();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var raw = ResultValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            MessageBox.Show(this, "Value cannot be empty.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        /* Normalize to LDF byte-array form { b0, b1, ... }. */
        if (!raw.StartsWith('{'))
        {
            var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                MessageBox.Show(this, "Enter at least one byte value.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ValueBox.Text = "{ " + string.Join(", ", parts) + " }";
        }
        DialogResult = true;
    }
}
