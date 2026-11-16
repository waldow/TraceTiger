using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using TraceTiger.ViewModels;

namespace TraceTiger.Views;

public partial class QueryTabView : UserControl
{
    /// <summary>Per-tab editor state. The TabControl reuses ONE QueryTabView for every tab, so each
    /// tab needs its own document (undo history) plus remembered caret and scroll position.</summary>
    private sealed class EditorState
    {
        public TextDocument Document { get; } = new();
        public int CaretOffset { get; set; }
        public double VerticalOffset { get; set; }
        public double HorizontalOffset { get; set; }
    }

    // Weak keys: a closed tab's document goes away with its view model.
    private static readonly ConditionalWeakTable<QueryTabViewModel, EditorState> States = new();

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
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            RememberViewState(_viewModel);
        }

        _viewModel = e.NewValue as QueryTabViewModel;

        _syncingEditorText = true;
        try
        {
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                ShowDocumentFor(_viewModel);
            }
            else
            {
                KqlEditor.Document = new TextDocument();
            }
        }
        finally
        {
            _syncingEditorText = false;
        }
    }

    private void RememberViewState(QueryTabViewModel viewModel)
    {
        if (!States.TryGetValue(viewModel, out var state)) return;
        state.CaretOffset = KqlEditor.CaretOffset;
        state.VerticalOffset = KqlEditor.VerticalOffset;
        state.HorizontalOffset = KqlEditor.HorizontalOffset;
    }

    private void ShowDocumentFor(QueryTabViewModel viewModel)
    {
        var state = States.GetValue(viewModel, _ => new EditorState());
        var document = state.Document;

        // First show of this tab, or its text was replaced while another tab was visible.
        if (document.Text != viewModel.Text)
        {
            document.Text = viewModel.Text;
            document.UndoStack.ClearAll();
        }

        KqlEditor.Document = document;
        KqlEditor.CaretOffset = Math.Min(state.CaretOffset, document.TextLength);

        // Scroll offsets only stick once the editor has measured the swapped-in document.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!ReferenceEquals(_viewModel, viewModel)) return;
            KqlEditor.ScrollToVerticalOffset(state.VerticalOffset);
            KqlEditor.ScrollToHorizontalOffset(state.HorizontalOffset);
        });
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
            try
            {
                KqlEditor.Document.Text = _viewModel.Text;
            }
            finally
            {
                _syncingEditorText = false;
            }
        }
    }
}
