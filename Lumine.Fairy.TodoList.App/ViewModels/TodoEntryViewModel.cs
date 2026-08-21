using CommunityToolkit.Mvvm.ComponentModel;
using Lumine.Fairy.ContactManagement.Models;
using System.Globalization;

namespace Lumine.Fairy.TodoList.App.ViewModels;

public sealed partial class TodoEntryViewModel : ObservableObject
{
    private readonly TodoEntry _todoEntry;

    public TodoEntryViewModel(TodoEntry todoEntry)
    {
        _todoEntry = todoEntry ?? throw new ArgumentNullException(nameof(todoEntry));
    }

    public TodoStatus[] TodoStatusOptions { get; } = Enum.GetValues<TodoStatus>();

    public string Id => _todoEntry.Id;

    public DateOnly? WorkDate
    {
        get => _todoEntry.WorkDate;
        set
        {
            if (_todoEntry.WorkDate != value)
            {
                _todoEntry.WorkDate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WorkDateText));
            }
        }
    }

    public string WorkDateText
    {
        get => WorkDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        set
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                WorkDate = null;
                return;
            }

            if (DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactDate)
                || DateOnly.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.None, out exactDate)
                || DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out exactDate))
            {
                WorkDate = exactDate;
            }
        }
    }

    public string ProjectName
    {
        get => _todoEntry.ProjectName;
        set
        {
            if (_todoEntry.ProjectName != value)
            {
                _todoEntry.ProjectName = value;
                OnPropertyChanged();
            }
        }
    }

    public string Title
    {
        get => _todoEntry.Title;
        set
        {
            if (_todoEntry.Title != value)
            {
                _todoEntry.Title = value;
                OnPropertyChanged();
            }
        }
    }

    public string Content
    {
        get => _todoEntry.Content;
        set
        {
            if (_todoEntry.Content != value)
            {
                _todoEntry.Content = value;
                OnPropertyChanged();
            }
        }
    }

    public TodoStatus Status
    {
        get => _todoEntry.Status;
        set
        {
            if (_todoEntry.Status != value)
            {
                _todoEntry.Status = value;
                OnPropertyChanged();
            }
        }
    }

    public decimal? WorkHours
    {
        get => _todoEntry.WorkHours;
        set
        {
            if (_todoEntry.WorkHours != value)
            {
                _todoEntry.WorkHours = value;
                OnPropertyChanged();
            }
        }
    }

    public string WorkHoursText
    {
        get => WorkHours?.ToString("0.##") ?? string.Empty;
        set
        {
            if (decimal.TryParse(value, out var hours))
            {
                WorkHours = hours;
            }
            else
            {
                WorkHours = null;
            }
        }
    }

    public bool IsDaily
    {
        get => _todoEntry.IsDaily;
        set
        {
            if (_todoEntry.IsDaily != value)
            {
                _todoEntry.IsDaily = value;
                OnPropertyChanged();
            }
        }
    }

    public string Notes
    {
        get => _todoEntry.Notes;
        set
        {
            if (_todoEntry.Notes != value)
            {
                _todoEntry.Notes = value;
                OnPropertyChanged();
            }
        }
    }

    public int SortOrder => _todoEntry.SortOrder;

    public TodoEntry GetTodoEntry() => _todoEntry;
}