using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lumine.Fairy.Desktop.Views;

public partial class ConfirmationWindow : Window
{
    public ConfirmationWindow(string title, string message, string confirmText)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
    }

    private void CancelClick(object? sender, RoutedEventArgs e)
        => Close(false);

    private void ConfirmClick(object? sender, RoutedEventArgs e)
        => Close(true);
}
