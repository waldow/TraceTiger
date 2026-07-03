using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Highlighting;
using TraceTiger.ViewModels;

namespace TraceTiger.Views;

public partial class QueryTabView : UserControl
{
    private QueryTabViewModel? _viewModel;
    private bool _syncingEditorText;

    public QueryTabView()
    {
        InitializeComponent();

        // Registered from KqlSyntax.xshd at startup (App.RegisterKqlHighlighting).
        KqlEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("KQL");

        // AvalonEdit's Text is not a DependencyProperty — sync manually with a re-entrancy guard.
        KqlEditor.TextChanged += KqlEditor_TextChanged;
        DataContextChanged += QueryTabView_DataContextChanged;
    }

    private void QueryTabView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

        _viewModel = e.NewValue as QueryTabViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            SyncEditorFromViewModel();
        }
        else
        {
            KqlEditor.Text = string.Empty;
        }
    }

    private void KqlEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_syncingEditorText || _viewModel is null)
            return;

        _viewModel.Text = KqlEditor.Text;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
            return;

        if (e.PropertyName == nameof(QueryTabViewModel.Text) && KqlEditor.Text != _viewModel.Text)
        {
            _syncingEditorText = true;
            KqlEditor.Text = _viewModel.Text;
            _syncingEditorText = false;
        }
    }

    private void SyncEditorFromViewModel()
    {
        if (_viewModel is null)
            return;

        _syncingEditorText = true;
        KqlEditor.Text = _viewModel.Text;
        _syncingEditorText = false;
    }
}
