using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Lumine.Fairy.Desktop.ViewModels;
using AvaloniaControl = Avalonia.Controls.Control;
using AvaloniaComboBox = Avalonia.Controls.ComboBox;
using AvaloniaDragDropEffects = Avalonia.Input.DragDropEffects;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaTextBox = Avalonia.Controls.TextBox;

namespace Lumine.Fairy.Desktop.Views;

public partial class MainWindow : Window
{
    private WorkItemViewModel? _dragItem;
    private PointerPressedEventArgs? _dragPressedArgs;
    private AvaloniaPoint _dragStartPoint;
    private bool _dragStarted;
    private TodoListViewModel? _dragList;
    private PointerPressedEventArgs? _dragListPressedArgs;
    private AvaloniaPoint _dragListStartPoint;
    private bool _dragListStarted;
    private Avalonia.Vector _todoListsScrollOffset;
    private bool _suppressTodoListsScrollTracking;
    private MainWindowViewModel? _workPageViewModel;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(AvaloniaTextBox.TextChangedEvent, TodoCompanyFilterTextChanged);
        DataContextChanged += MainWindowDataContextChanged;
        MainWindowDataContextChanged(this, EventArgs.Empty);
    }

    private static void TodoCompanyFilterTextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        if (e.Source is AvaloniaTextBox { IsFocused: true } textBox
            && textBox.FindAncestorOfType<AvaloniaComboBox>() is { DataContext: MainWindowViewModel viewModel } comboBox
            && comboBox.Classes.Contains("todo-company-filter")
            && viewModel.FilteredTodoCompanyFilters.Count > 0
            && !string.Equals(
                viewModel.TodoCompanyFilterText.Trim(),
                viewModel.SelectedTodoCompanyFilter?.Name.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            comboBox.SetCurrentValue(AvaloniaComboBox.IsDropDownOpenProperty, true);
        }
    }

    private void MainWindowDataContextChanged(object? sender, EventArgs e)
    {
        if (_workPageViewModel is not null)
            UnsubscribeFromWorkPageRefresh(_workPageViewModel);

        _workPageViewModel = DataContext as MainWindowViewModel;
        if (_workPageViewModel is not null)
            SubscribeToWorkPageRefresh(_workPageViewModel);
    }

    private void SubscribeToWorkPageRefresh(MainWindowViewModel viewModel)
    {
        viewModel.WorkPageRefreshing += CaptureTodoListsScrollOffset;
        viewModel.WorkPageRefreshed += RestoreTodoListsScrollOffset;
    }

    private void UnsubscribeFromWorkPageRefresh(MainWindowViewModel viewModel)
    {
        viewModel.WorkPageRefreshing -= CaptureTodoListsScrollOffset;
        viewModel.WorkPageRefreshed -= RestoreTodoListsScrollOffset;
    }

    private void TodoListsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!_suppressTodoListsScrollTracking && sender is ScrollViewer scrollViewer)
            _todoListsScrollOffset = scrollViewer.Offset;
    }

    private void CaptureTodoListsScrollOffset(object? sender, EventArgs e)
    {
        _todoListsScrollOffset = TodoListsScrollViewer.Offset;
        _suppressTodoListsScrollTracking = true;
    }

    private void RestoreTodoListsScrollOffset(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _suppressTodoListsScrollTracking = true;
            TodoListsScrollViewer.Offset = _todoListsScrollOffset;
            Dispatcher.UIThread.Post(
                () => _suppressTodoListsScrollTracking = false,
                DispatcherPriority.Background);
        }, DispatcherPriority.Render);
    }

    private void NotificationScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer
            && scrollViewer.Extent.Height - scrollViewer.Offset.Y - scrollViewer.Viewport.Height < 120
            && DataContext is MainWindowViewModel viewModel)
            viewModel.LoadMoreNotificationsCommand.Execute(null);
    }

    private void NotificationSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is Avalonia.Controls.ListBox listBox && listBox.SelectedIndex >= 0)
            listBox.SelectedIndex = -1;
    }

    private void TodoCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsTodoCardMoreSource(e.Source))
            return;
        if (sender is not AvaloniaControl control
            || control.DataContext is not WorkItemViewModel item
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        _dragItem = item;
        _dragPressedArgs = e;
        _dragStartPoint = e.GetPosition(control);
        _dragStarted = false;
        e.Pointer.Capture(control);
    }

    private void TodoCardPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem is null
            || sender is not AvaloniaControl control
            || DataContext is MainWindowViewModel { CanReorderTodos: false }
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        if (!_dragStarted)
        {
            var delta = e.GetPosition(control) - _dragStartPoint;
            if (Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y) < 8)
            {
                return;
            }

            _dragStarted = true;
            ShowDragPreview($"卡片：{_dragItem.Title}", e.GetPosition(this));
        }

        UpdateDragPreviewPosition(e.GetPosition(this));
    }

    private async void TodoCardPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is AvaloniaControl control)
            e.Pointer.Capture(null);
        var item = _dragItem;
        var wasDragging = _dragStarted;
        var pointerPosition = e.GetPosition(this);
        _dragItem = null;
        _dragPressedArgs = null;
        _dragStarted = false;
        HideDragPreview();
        if (item is not null && wasDragging)
            await CompleteTodoDropAsync(item, pointerPosition, target => e.GetPosition(target));
    }

    private static void TodoCardDragOver(object? sender, Avalonia.Input.DragEventArgs e)
    {
        if (e.DataTransfer.TryGetText() is string text && text.StartsWith("todo:", StringComparison.Ordinal))
        {
            e.DragEffects = AvaloniaDragDropEffects.Move;
            e.Handled = true;
        }
    }

    private static void TodoListDragOver(object? sender, Avalonia.Input.DragEventArgs e)
        => TodoCardDragOver(sender, e);

    private async void TodoCardDrop(object? sender, Avalonia.Input.DragEventArgs e)
    {
        if (e.DataTransfer.TryGetText() is not string payload
            || !payload.StartsWith("todo:", StringComparison.Ordinal)
            || DataContext is not MainWindowViewModel viewModel)
            return;

        e.Handled = true;
        var item = viewModel.WorkItems.FirstOrDefault(card =>
            string.Equals(card.Id, payload[5..], StringComparison.OrdinalIgnoreCase));
        if (item is not null)
            await CompleteTodoDropAsync(item, e.GetPosition(this), target => e.GetPosition(target));
    }

    private async void TodoListDrop(object? sender, Avalonia.Input.DragEventArgs e)
    {
        if (e.DataTransfer.TryGetText() is not string payload
            || !payload.StartsWith("todo:", StringComparison.Ordinal)
            || sender is not AvaloniaControl control
            || control.DataContext is not TodoListViewModel list
            || DataContext is not MainWindowViewModel viewModel)
            return;

        e.Handled = true;
        var position = list.Cards.Count == 0 ? 0 : list.Cards.Max(card => card.Record.Position) + 1;
        await viewModel.MoveTodoAsync(payload[5..], list.Id, position);
    }

    private void TodoListHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not AvaloniaControl control
            || control.DataContext is not TodoListViewModel list
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        _dragList = list;
        _dragListPressedArgs = e;
        _dragListStartPoint = e.GetPosition(control);
        _dragListStarted = false;
        e.Pointer.Capture(control);
    }

    private void TodoListHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragList is null
            || sender is not AvaloniaControl control
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        if (!_dragListStarted)
        {
            var delta = e.GetPosition(control) - _dragListStartPoint;
            if (Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y) < 8)
            {
                return;
            }

            _dragListStarted = true;
            ShowDragPreview($"列表：{_dragList.Name}", e.GetPosition(this));
        }

        UpdateDragPreviewPosition(e.GetPosition(this));
    }

    private async void TodoListHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is AvaloniaControl control)
            e.Pointer.Capture(null);
        var list = _dragList;
        var wasDragging = _dragListStarted;
        var pointerPosition = e.GetPosition(this);
        _dragList = null;
        _dragListPressedArgs = null;
        _dragListStarted = false;
        HideDragPreview();
        if (list is not null && wasDragging)
            await CompleteTodoListDropAsync(list, pointerPosition);
    }

    private static void TodoListContainerDragOver(object? sender, Avalonia.Input.DragEventArgs e)
    {
        if (e.DataTransfer.TryGetText() is string payload
            && (payload.StartsWith("todo:", StringComparison.Ordinal)
                || payload.StartsWith("list:", StringComparison.Ordinal)))
        {
            e.DragEffects = AvaloniaDragDropEffects.Move;
            e.Handled = true;
        }
    }

    private async void TodoListContainerDrop(object? sender, Avalonia.Input.DragEventArgs e)
    {
        if (e.DataTransfer.TryGetText() is not string payload
            || sender is not AvaloniaControl control
            || control.DataContext is not TodoListViewModel target
            || DataContext is not MainWindowViewModel viewModel)
            return;

        e.Handled = true;
        if (payload.StartsWith("list:", StringComparison.Ordinal))
        {
            await viewModel.MoveTodoListAsync(payload[5..], target.Record.Position);
            return;
        }

        if (payload.StartsWith("todo:", StringComparison.Ordinal))
        {
            var position = target.Cards.Count == 0 ? 0 : target.Cards.Max(card => card.Record.Position) + 1;
            await viewModel.MoveTodoAsync(payload[5..], target.Id, position);
        }
    }

    private async void TodoListNameLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.TextBox textBox
            && textBox.DataContext is TodoListViewModel list
            && list.IsEditingName)
            await list.SaveNameAsync();
    }

    private async void NewTodoListName_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.TextBox textBox
            && !string.IsNullOrWhiteSpace(textBox.Text)
            && DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.AddTodoListCommand.ExecuteAsync(null);
        }
    }

    private async void NewTodoListName_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter
            || sender is not Avalonia.Controls.TextBox textBox
            || string.IsNullOrWhiteSpace(textBox.Text)
            || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        await viewModel.AddTodoListCommand.ExecuteAsync(null);
    }

    private void TodoRenameListClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Avalonia.Controls.MenuItem menuItem
            || menuItem.DataContext is not TodoListViewModel list)
            return;

        list.BeginRenameCommand.Execute(null);
        Dispatcher.UIThread.Post(() =>
        {
            var textBox = this.GetVisualDescendants()
                .OfType<Avalonia.Controls.TextBox>()
                .FirstOrDefault(item => ReferenceEquals(item.DataContext, list));
            textBox?.Focus();
            textBox?.SelectAll();
        });
    }

    private static void TodoCardMoreClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => e.Handled = true;

    private static bool IsTodoCardMoreSource(object? source)
        => source is AvaloniaControl control
           && (control.Classes.Contains("todo-card-more")
               || control.FindAncestorOfType<Avalonia.Controls.Button>()?.Classes.Contains("todo-card-more") == true);

    private void ShowDragPreview(string text, AvaloniaPoint pointerPosition)
    {
        DragPreviewText.Text = text;
        DragPreviewBorder.IsVisible = true;
        UpdateDragPreviewPosition(pointerPosition);
    }

    private void UpdateDragPreviewPosition(AvaloniaPoint pointerPosition)
    {
        const double pointerOffset = 14;
        var left = pointerPosition.X + pointerOffset;
        var top = pointerPosition.Y + pointerOffset;
        if (Bounds.Width > 0)
            left = Math.Min(left, Math.Max(0, Bounds.Width - DragPreviewBorder.Width - 8));
        if (Bounds.Height > 0)
            top = Math.Min(top, Math.Max(0, Bounds.Height - DragPreviewBorder.Bounds.Height - 8));

        Canvas.SetLeft(DragPreviewBorder, Math.Max(0, left));
        Canvas.SetTop(DragPreviewBorder, Math.Max(0, top));
    }

    private void HideDragPreview()
    {
        DragPreviewBorder.IsVisible = false;
        DragPreviewText.Text = string.Empty;
    }

    private async Task CompleteTodoDropAsync(
        WorkItemViewModel item,
        AvaloniaPoint pointerPosition,
        Func<AvaloniaControl, AvaloniaPoint> getPosition)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var hit = this.InputHitTest(pointerPosition);
        var targetCard = FindControlWithDataContext<WorkItemViewModel>(hit);
        if (targetCard?.DataContext is WorkItemViewModel targetItem)
        {
            if (string.Equals(targetItem.Id, item.Id, StringComparison.OrdinalIgnoreCase))
                return;

            var targetCardList = viewModel.TodoLists.FirstOrDefault(list => list.Cards.Contains(targetItem));
            if (targetCardList is null)
                return;

            var targetIndex = targetCardList.Cards.IndexOf(targetItem);
            if (targetIndex < 0)
                return;

            var insertAfter = getPosition(targetCard).Y > targetCard.Bounds.Height / 2;
            var position = insertAfter ? targetIndex + 1 : targetIndex;
            var sourceList = viewModel.TodoLists.FirstOrDefault(list => list.Cards.Contains(item));
            if (ReferenceEquals(sourceList, targetCardList))
            {
                var sourceIndex = sourceList.Cards.IndexOf(item);
                if (sourceIndex >= 0 && sourceIndex < targetIndex)
                    position--;
            }

            await viewModel.MoveTodoAsync(item.Id, targetCardList.Id, position);
            return;
        }

        var targetList = FindDataContext<TodoListViewModel>(hit);
        if (targetList is not null)
        {
            var position = targetList.Cards.Count;
            await viewModel.MoveTodoAsync(item.Id, targetList.Id, position);
        }
    }

    private async Task CompleteTodoListDropAsync(TodoListViewModel list, AvaloniaPoint pointerPosition)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var target = FindDataContext<TodoListViewModel>(this.InputHitTest(pointerPosition));
        if (target is not null && !string.Equals(target.Id, list.Id, StringComparison.OrdinalIgnoreCase))
            await viewModel.MoveTodoListAsync(list.Id, target.Record.Position);
    }

    private static T? FindDataContext<T>(IInputElement? input)
        where T : class
        => input is Avalonia.Visual visual
            ? visual.GetSelfAndVisualAncestors()
                .OfType<AvaloniaControl>()
                .Select(control => control.DataContext)
                .OfType<T>()
                .FirstOrDefault()
            : null;

    private static AvaloniaControl? FindControlWithDataContext<T>(IInputElement? input)
        where T : class
        => input is Avalonia.Visual visual
            ? visual.GetSelfAndVisualAncestors()
                .OfType<AvaloniaControl>()
                .FirstOrDefault(control => control.Classes.Contains("todo-card") && control.DataContext is T)
            : null;
}
