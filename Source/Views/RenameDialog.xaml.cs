using System.Windows;

namespace TraceTiger.Views;

public partial class RenameDialog : Window
{
    private RenameDialog(string title, string prompt, string initial)
    {
        InitializeComponent();
        TitleText.Text = title;
        PromptText.Text = prompt;
        NameBox.Text = initial;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    /// <summary>Shows the dialog; returns the entered text or null when cancelled.</summary>
    public static string? Show(string title, string prompt, string initial = "")
    {
        var dialog = new RenameDialog(title, prompt, initial)
        {
            Owner = Application.Current?.MainWindow,
        };
        return dialog.ShowDialog() == true ? dialog.NameBox.Text : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
