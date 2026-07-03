using System.Windows;

namespace TraceTiger.Views;

public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string confirmLabel)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
    }

    /// <summary>Shows a themed yes/no dialog; true when the destructive action was confirmed.</summary>
    public static bool Show(string title, string message, string confirmLabel = "OK")
    {
        var dialog = new ConfirmDialog(title, message, confirmLabel)
        {
            Owner = Application.Current?.MainWindow,
        };
        return dialog.ShowDialog() == true;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
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
