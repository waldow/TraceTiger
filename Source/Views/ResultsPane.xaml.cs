using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Highlighting;
using TraceTiger.ViewModels;

namespace TraceTiger.Views;

public partial class ResultsPane : UserControl
{
    private QueryTabViewModel? _viewModel;

    public ResultsPane()
    {
        InitializeComponent();

        // AvalonEdit's built-in JavaScript definition is a good-enough JSON highlighter.
        var jsonHighlighting = HighlightingManager.Instance.GetDefinition("JavaScript");
        JsonEditor.SyntaxHighlighting = jsonHighlighting;
        InspectorEditor.SyntaxHighlighting = jsonHighlighting;

        DataContextChanged += ResultsPane_DataContextChanged;
    }

    private void ResultsPane_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

        _viewModel = e.NewValue as QueryTabViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            JsonEditor.Text = _viewModel.JsonText;
            InspectorEditor.Text = _viewModel.InspectorText;
        }
        else
        {
            JsonEditor.Text = string.Empty;
            InspectorEditor.Text = string.Empty;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null) return;

        if (e.PropertyName == nameof(QueryTabViewModel.JsonText))
            JsonEditor.Text = _viewModel.JsonText;
        else if (e.PropertyName == nameof(QueryTabViewModel.InspectorText))
            InspectorEditor.Text = _viewModel.InspectorText;
    }

    // KQL column names can contain '.', '/', '[' etc. (e.g. parse_json(...).x projections), which
    // corrupt the auto-generated binding path. Bind through the DataRowView string indexer instead.
    private void ResultsGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.Column is DataGridBoundColumn bound)
        {
            bound.Binding = new Binding($"[{e.PropertyName}]");
            e.Column.SortMemberPath = e.PropertyName;
            e.Column.Header = e.PropertyName;
        }
        e.Column.MaxWidth = 480;
    }

    private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null) return;

        var cell = ResultsGrid.CurrentCell;
        if (cell.Column is null || cell.Item is not DataRowView rowView) return;

        var columnName = cell.Column.SortMemberPath;
        if (string.IsNullOrEmpty(columnName)) return;

        _viewModel.InspectCell(columnName, rowView[columnName]);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _viewModel?.IsInspectorOpen == true)
        {
            _viewModel.IsInspectorOpen = false;
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }
}
