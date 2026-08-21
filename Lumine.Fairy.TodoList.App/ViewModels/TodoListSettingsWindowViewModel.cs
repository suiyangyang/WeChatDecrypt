using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumine.Fairy.TodoList.App.Services;

namespace Lumine.Fairy.TodoList.App.ViewModels;

public sealed partial class TodoListSettingsWindowViewModel : ObservableObject
{
    private readonly ITodoListSettingsService _settingsService;
    private readonly IReportMindConnectionProbeService _connectionProbeService;
    private readonly Action<bool> _closeAction;

    public TodoListSettingsWindowViewModel(
        ITodoListSettingsService settingsService,
        Action<bool> closeAction,
        IReportMindConnectionProbeService? connectionProbeService = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _closeAction = closeAction ?? throw new ArgumentNullException(nameof(closeAction));
        _connectionProbeService = connectionProbeService ?? new ReportMindConnectionProbeService();

        var settings = _settingsService.Load();
        ReportMindServerBaseUrl = settings.ReportMindServerBaseUrl;
        ReportMindUserName = settings.ReportMindUserName;
        ReportMindPassword = settings.ReportMindPassword;
    }

    [ObservableProperty]
    private string _reportMindServerBaseUrl = ReportMindRecentDailyReportUploadService.DefaultServerBaseUrl;

    [ObservableProperty]
    private string _reportMindUserName = string.Empty;

    [ObservableProperty]
    private string _reportMindPassword = string.Empty;

    [ObservableProperty]
    private bool _isPasswordVisible;

    [ObservableProperty]
    private string _statusText = "上传日报时会读取这里的配置。";

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isTestingConnection;

    public string SettingsFilePath => _settingsService.SettingsFilePath;

    public bool IsBusy => IsSaving || IsTestingConnection;

    public string PasswordVisibilityButtonText => IsPasswordVisible ? "隐藏" : "显示";

    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
    }

    partial void OnIsTestingConnectionChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
    }

    partial void OnIsPasswordVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordVisibilityButtonText));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (!Uri.TryCreate(NormalizeServerBaseUrl(ReportMindServerBaseUrl), UriKind.Absolute, out _))
        {
            StatusText = "ReportMind 服务地址格式不正确。";
            return;
        }


        if (!ValidateCredentials())
        {
            return;
        }

        try
        {
            IsSaving = true;
            await _settingsService.SaveAsync(new TodoListAppSettings
            {
                ReportMindServerBaseUrl = NormalizeServerBaseUrl(ReportMindServerBaseUrl),
                ReportMindUserName = ReportMindUserName.Trim(),
                ReportMindPassword = ReportMindPassword
            });
            StatusText = "设置已保存。";
            _closeAction(true);
        }
        catch (Exception ex)
        {
            StatusText = $"保存失败：{ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (!Uri.TryCreate(NormalizeServerBaseUrl(ReportMindServerBaseUrl), UriKind.Absolute, out _))
        {
            StatusText = "ReportMind 服务地址格式不正确。";
            return;
        }


        if (!ValidateCredentials())
        {
            return;
        }

        try
        {
            IsTestingConnection = true;
            StatusText = "正在测试连接...";

            var result = await _connectionProbeService.ProbeAsync(
                ReportMindServerBaseUrl,
                ReportMindUserName,
                ReportMindPassword);

            StatusText = $"登录成功，用户“{result.UserName}”可用。";
        }
        catch (Exception ex)
        {
            StatusText = $"连接失败：{ex.Message}";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _closeAction(false);
    }

    [RelayCommand]
    private void ResetServerBaseUrl()
    {
        ReportMindServerBaseUrl = ReportMindRecentDailyReportUploadService.DefaultServerBaseUrl;
    }

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordVisible = !IsPasswordVisible;
    }

    private bool ValidateCredentials()
    {
        if (string.IsNullOrWhiteSpace(ReportMindUserName))
        {
            StatusText = "请填写 ReportMind 用户名。";
            return false;
        }

        if (string.IsNullOrEmpty(ReportMindPassword))
        {
            StatusText = "请填写 ReportMind 密码。";
            return false;
        }

        return true;
    }

    private static string NormalizeServerBaseUrl(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? ReportMindRecentDailyReportUploadService.DefaultServerBaseUrl
            : value.Trim();

        return normalized.TrimEnd('/');
    }
}
