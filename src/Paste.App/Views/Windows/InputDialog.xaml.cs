using System.Windows;
using Wpf.Ui.Controls;

namespace Paste.App.Views.Windows;

public partial class InputDialog : FluentWindow
{
    public string ResultText { get; private set; } = string.Empty;

    public InputDialog(string prompt, string defaultValue = "")
    {
        InitializeComponent();
        PromptText.Text = prompt;
        InputBox.Text = defaultValue;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        ResultText = InputBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
