using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using LdfEditor.Services;
using LdfEditor.Services.Diagnostics;
using Microsoft.Win32;

namespace LdfEditor.Views;

public partial class ConsistencyCheckerWindow : Window
{
    private LdfConsistencyChecker.Report _report;
    private string? _path;
    private string _baselineText;
    private bool _dirty;
    private bool _suppressTextChanged;
    private ICollectionView? _view;

    /// <summary>Called after a successful Save / Save As (path, text). Main editor can reload.</summary>
    public Action<string, string>? DocumentSaved { get; set; }

    public string StatusLine { get; private set; } = "";

    public ConsistencyCheckerWindow(LdfConsistencyChecker.Report report)
    {
        InitializeComponent();
        DataContext = this;
        _report = report;
        _path = report.FilePath;
        _baselineText = report.FileText ?? "";
        ApplyReport(report, resetSource: true);
    }

    public static ConsistencyCheckerWindow ShowFor(
        Window? owner,
        string? path,
        string? text,
        Models.LdfDatabase? db,
        IReadOnlyList<LdfSectionOrderValidator.Issue>? priorIssues = null,
        IReadOnlyList<LdfDiagnostic>? priorDiagnostics = null,
        Action<string, string>? documentSaved = null)
    {
        if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(path) && File.Exists(path))
            text = File.ReadAllText(path);
        var report = LdfConsistencyChecker.CheckFile(path, text, db, priorIssues, priorDiagnostics);
        var w = new ConsistencyCheckerWindow(report)
        {
            Owner = owner,
            DocumentSaved = documentSaved
        };
        w.Show();
        return w;
    }

    private void ApplyReport(LdfConsistencyChecker.Report report, bool resetSource)
    {
        _report = report;
        _path = report.FilePath;

        UpdateSourceTitle();

        if (resetSource)
        {
            _suppressTextChanged = true;
            SourceBox.Text = report.FileText ?? "";
            _suppressTextChanged = false;
            _baselineText = SourceBox.Text;
            SetDirty(false);
        }

        _view = CollectionViewSource.GetDefaultView(report.Findings.ToList());
        _view.GroupDescriptions.Clear();
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LdfConsistencyChecker.Finding.Category)));
        ResultsGrid.ItemsSource = _view;

        ClearDetail();
        if (report.Findings.Count > 0)
        {
            ResultsGrid.SelectedIndex = 0;
            ShowDetail(report.Findings[0]);
            GoToFinding(report.Findings[0]);
        }

        int errors = report.Findings.Count(f => f.Severity == LdfConsistencyChecker.Severity.Error);
        StatusLine = report.ParseFailed
            ? $"Parse failed — {errors} error(s); fix in LDF source and Run Checks."
            : report.Ok
                ? "No issues found."
                : $"{errors} error(s), {report.Findings.Count} finding(s).";
        DataContext = null;
        DataContext = this;
    }

    private void UpdateSourceTitle()
    {
        var name = string.IsNullOrEmpty(_path) ? "Untitled.ldf" : Path.GetFileName(_path);
        SourceTitle.Text = $"{name} — editable";
        Title = _dirty
            ? $"LIN Consistency Checker — {name}*"
            : $"LIN Consistency Checker — {name}";
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        DirtyMark.Text = dirty ? "● Modified" : "";
        UpdateSourceTitle();
    }

    private void OnSourceTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged) return;
        bool dirty = !string.Equals(SourceBox.Text, _baselineText, StringComparison.Ordinal);
        if (dirty != _dirty)
            SetDirty(dirty);
    }

    private void OnRunChecks(object sender, RoutedEventArgs e) => RunChecks();

    private void RunChecks()
    {
        int caret = SourceBox.CaretIndex;
        double vOff = SourceBox.VerticalOffset;
        ApplyReport(LdfConsistencyChecker.CheckFile(_path, SourceBox.Text, db: null), resetSource: false);
        try
        {
            SourceBox.CaretIndex = Math.Min(caret, SourceBox.Text.Length);
            SourceBox.ScrollToVerticalOffset(vOff);
        }
        catch { /* ignore */ }
    }

    private void OnReloadFile(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_path) || !File.Exists(_path))
        {
            MessageBox.Show(this, "No file path to reload.", "Reload File",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_dirty)
        {
            var r = MessageBox.Show(this,
                "Discard current edits and reload from disk?",
                "Reload File", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }
        var text = File.ReadAllText(_path);
        ApplyReport(LdfConsistencyChecker.CheckFile(_path, text, db: null), resetSource: true);
    }

    private void OnSave(object sender, RoutedEventArgs e) => Save(promptAs: false);

    private void OnSaveAs(object sender, RoutedEventArgs e) => Save(promptAs: true);

    private bool Save(bool promptAs)
    {
        var path = _path;
        if (promptAs || string.IsNullOrEmpty(path))
        {
            var dlg = new SaveFileDialog
            {
                Filter = "LIN Description (*.ldf)|*.ldf|All (*.*)|*.*",
                FileName = string.IsNullOrEmpty(path) ? "Untitled.ldf" : Path.GetFileName(path),
                Title = "Save LDF As"
            };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }

        try
        {
            File.WriteAllText(path, SourceBox.Text ?? "");
            _path = path;
            _baselineText = SourceBox.Text ?? "";
            SetDirty(false);
            DocumentSaved?.Invoke(path, _baselineText);
            StatusLine = $"Saved {path}";
            DataContext = null;
            DataContext = this;
            /* Re-check after save so Results match disk */
            ApplyReport(LdfConsistencyChecker.CheckFile(_path, SourceBox.Text, db: null), resetSource: false);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (!_dirty) return;
        var r = MessageBox.Show(this,
            "LDF source has unsaved changes. Save before closing?",
            "LIN Consistency Checker",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return;
        }
        if (r == MessageBoxResult.Yes && !Save(promptAs: string.IsNullOrEmpty(_path)))
            e.Cancel = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Save(promptAs: false);
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Save(promptAs: true);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            RunChecks();
            e.Handled = true;
        }
    }

    private void OnResultSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not LdfConsistencyChecker.Finding f)
        {
            ClearDetail();
            return;
        }
        ShowDetail(f);
        GoToFinding(f);
    }

    private void ShowDetail(LdfConsistencyChecker.Finding f)
    {
        DetailRuleId.Text = string.IsNullOrEmpty(f.RuleId)
            ? $"[{f.Code}] {f.Category}"
            : $"[{f.Code}]  {f.RuleId}";
        DetailWhy.Text = string.IsNullOrWhiteSpace(f.Why)
            ? "(no additional explanation)"
            : f.Why;
        DetailConstraint.Text = string.IsNullOrWhiteSpace(f.Constraint)
            ? "(no constraint / rule text)"
            : f.Constraint;
    }

    private void ClearDetail()
    {
        DetailRuleId.Text = "";
        DetailWhy.Text = "Select a finding to see why it failed and which rule applies.";
        DetailConstraint.Text = "";
    }

    private void GoToFinding(LdfConsistencyChecker.Finding f)
    {
        if (f.Line is not int line || line <= 0) return;
        GoToLine(line, f.Column ?? 1, lengthHint: Math.Max(1, (f.Source?.Length ?? 0)));
    }

    private void GoToLine(int line, int column = 1, int lengthHint = 0)
    {
        var text = SourceBox.Text ?? "";
        int idx = 0;
        int cur = 1;
        while (cur < line && idx < text.Length)
        {
            int nl = text.IndexOf('\n', idx);
            if (nl < 0) break;
            idx = nl + 1;
            cur++;
        }
        if (cur != line) return;

        int lineEnd = text.IndexOf('\n', idx);
        if (lineEnd < 0) lineEnd = text.Length;
        /* Exclude trailing \r */
        if (lineEnd > idx && text[lineEnd - 1] == '\r') lineEnd--;

        int col = Math.Max(1, column);
        int start = Math.Min(idx + col - 1, lineEnd);
        int len = lengthHint > 0
            ? Math.Min(lengthHint, Math.Max(0, lineEnd - start))
            : Math.Max(0, lineEnd - start);
        if (len == 0 && start < lineEnd)
            len = lineEnd - start;

        SourceBox.Focus();
        SourceBox.CaretIndex = start;
        SourceBox.Select(start, len);
        var rect = SourceBox.GetRectFromCharacterIndex(start);
        if (!rect.IsEmpty)
            SourceBox.ScrollToVerticalOffset(Math.Max(0, SourceBox.VerticalOffset + rect.Top - 40));
    }
}
