using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Lumine.Fairy.TodoList.App.ViewModels;

namespace Lumine.Fairy.TodoList.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void CopyDailyReport_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TodoMainWindowViewModel viewModel)
        {
            return;
        }

        await CopyDailyReportAsync(viewModel);
    }

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TodoMainWindowViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.F8)
        {
            viewModel.CycleTodoViewModeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                viewModel.ParseScratchNoteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.S:
                await viewModel.SaveTodoWorkspaceCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case Key.C when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                await CopyDailyReportAsync(viewModel);
                e.Handled = true;
                break;
        }
    }

    private async Task CopyDailyReportAsync(TodoMainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (string.IsNullOrWhiteSpace(viewModel.FormattedDailyReportText))
        {
            viewModel.UpdateStatus("当前没有可复制的日报文本。");
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            viewModel.UpdateStatus("当前环境不支持剪贴板操作。");
            return;
        }

        await clipboard.SetTextAsync(viewModel.FormattedDailyReportText);
        viewModel.UpdateStatus("日报文本已复制到剪贴板。");
    }

    private void DailyTodoEditor_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || DataContext is not TodoMainWindowViewModel viewModel)
        {
            return;
        }

        RestoreScratchEditorCaretPosition(textBox, viewModel);
    }

    private void TodoProjectNameEditor_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.DataContext is TodoEntryItemViewModel item && item.ShouldFocusProjectNameEditor)
        {
            FocusProjectNameEditor(comboBox, () => item.ShouldFocusProjectNameEditor = false);
        }
    }

    private void ClientIssueProjectNameEditor_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.DataContext is ClientIssueEntryItemViewModel item && item.ShouldFocusProjectNameEditor)
        {
            FocusProjectNameEditor(comboBox, () => item.ShouldFocusProjectNameEditor = false);
        }
    }

    private void ProjectNameEditor_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        if (comboBox.IsDropDownOpen)
        {
            return;
        }

        // Delay opening slightly so focus is fully settled before the popup appears;
        // opening synchronously in GotFocus may conflict with Avalonia's internal focus handling.
        Dispatcher.UIThread.Post(() =>
        {
            if (comboBox.IsFocused || comboBox.IsKeyboardFocusWithin)
            {
                comboBox.IsDropDownOpen = true;
            }
        }, DispatcherPriority.Input);
    }

    private static void FocusProjectNameEditor(InputElement inputElement, Action clearFocusRequest)
    {
        Dispatcher.UIThread.Post(() =>
        {
            inputElement.Focus();
            if (inputElement is ComboBox comboBox && (comboBox.ItemCount > 0 || comboBox.ItemsSource is not null))
            {
                comboBox.IsDropDownOpen = true;
            }
            clearFocusRequest();
        }, DispatcherPriority.Input);
    }

    private void ScratchEditor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || e.Key != Key.Tab)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return;
        }

        InsertTabSpacesAtCaret(textBox);
        e.Handled = true;
    }

    private static void InsertTabSpacesAtCaret(TextBox textBox)
    {
        var originalText = textBox.Text ?? string.Empty;
        var selectionStart = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
        var currentLineStart = FindCurrentLineStart(originalText, selectionStart);
        var currentLineText = originalText.Substring(currentLineStart, selectionStart - currentLineStart);
        var currentDisplayColumn = MultilineTextFieldHelper.GetDisplayColumn(currentLineText);
        var alignmentWidth = MultilineTextFieldHelper.GetTabPaddingWidth(currentDisplayColumn);
        InsertTextAtCaret(textBox, MultilineTextFieldHelper.BuildAlignmentWhitespace(alignmentWidth));
    }

    private static void InsertTextAtCaret(TextBox textBox, string insertedText)
    {
        var originalText = textBox.Text ?? string.Empty;
        var selectionStart = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
        var selectionEnd = Math.Max(textBox.SelectionStart, textBox.SelectionEnd);
        var updatedText = originalText.Remove(selectionStart, selectionEnd - selectionStart).Insert(selectionStart, insertedText);

        textBox.Text = updatedText;
        textBox.SelectionStart = selectionStart + insertedText.Length;
        textBox.SelectionEnd = textBox.SelectionStart;
        textBox.CaretIndex = textBox.SelectionStart;
    }

    private static int FindCurrentLineStart(string text, int caretIndex)
    {
        if (caretIndex <= 0)
        {
            return 0;
        }

        var lineBreakIndex = text.LastIndexOf('\n', caretIndex - 1);
        return lineBreakIndex >= 0 ? lineBreakIndex + 1 : 0;
    }

    private void ScratchEditor_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not TextBox textBox || DataContext is not TodoMainWindowViewModel viewModel)
        {
            return;
        }

        if (e.Property == TextBox.CaretIndexProperty)
        {
            viewModel.ScratchNoteCaretIndex = textBox.CaretIndex;
        }
    }

    private static void RestoreScratchEditorCaretPosition(TextBox textBox, TodoMainWindowViewModel viewModel)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var textLength = textBox.Text?.Length ?? 0;
            var caretIndex = Math.Min(viewModel.ScratchNoteCaretIndex, textLength);

            if (caretIndex > 0)
            {
                textBox.CaretIndex = caretIndex;
                textBox.SelectionStart = caretIndex;
                textBox.SelectionEnd = caretIndex;
            }
            else
            {
                textBox.CaretIndex = textLength;
                textBox.SelectionStart = textLength;
                textBox.SelectionEnd = textLength;
            }
        }, DispatcherPriority.Background);
    }

    private static void ScrollScratchEditorToBottom(TextBox textBox)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var textLength = textBox.Text?.Length ?? 0;
            textBox.CaretIndex = textLength;
            textBox.SelectionStart = textLength;
            textBox.SelectionEnd = textLength;
        }, DispatcherPriority.Background);
    }
}
