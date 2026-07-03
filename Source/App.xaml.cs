using System.Windows;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using TraceTiger.Services;
using TraceTiger.ViewModels;

namespace TraceTiger;

public partial class App : Application
{
    // Poor man's DI: single-window desktop app, so plain static singletons keep wiring obvious.
    public static PersistenceService Persistence { get; } = new();
    public static ScriptLibraryService Library { get; } = new();
    public static KustoQueryService Kusto { get; } = new();
    public static HistoryService History { get; private set; } = null!;
    public static MainViewModel MainViewModel { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();

        RegisterKqlHighlighting();

        History = new HistoryService(Persistence);
        MainViewModel = new MainViewModel(Persistence, Library, History);
        MainViewModel.RestoreSession();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        MainViewModel?.SaveSessionNow();
        Library.Dispose();
        base.OnExit(e);
    }

    private static void RegisterKqlHighlighting()
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream("TraceTiger.Themes.KqlSyntax.xshd")
            ?? throw new InvalidOperationException("KqlSyntax.xshd embedded resource missing.");
        using var reader = XmlReader.Create(stream);
        HighlightingManager.Instance.RegisterHighlighting(
            "KQL", [".kql"], HighlightingLoader.Load(reader, HighlightingManager.Instance));
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tracetiger-unhandled.txt"),
                e.Exception.ToString());
        }
        catch { }
        MessageBox.Show(e.Exception.Message, "TraceTiger — unexpected error",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
