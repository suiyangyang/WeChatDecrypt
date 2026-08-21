using Lumine.Fairy.Models;

namespace Lumine.Fairy.Services;

public interface ISchedulerInteractionService
{
    event EventHandler<SchedulerMessageEventArgs>? MessagePublished;

    event EventHandler<PendingCommandAction>? PendingCommandRequested;

    event EventHandler<PendingDesktopAction>? PendingDesktopRequested;

    void PublishMessage(string message, bool isError = false);

    void PublishPendingCommand(PendingCommandAction action);

    void PublishPendingDesktop(PendingDesktopAction action);
}

public sealed class SchedulerInteractionService : ISchedulerInteractionService
{
    public event EventHandler<SchedulerMessageEventArgs>? MessagePublished;

    public event EventHandler<PendingCommandAction>? PendingCommandRequested;

    public event EventHandler<PendingDesktopAction>? PendingDesktopRequested;

    public void PublishMessage(string message, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        MessagePublished?.Invoke(this, new SchedulerMessageEventArgs(message.Trim(), isError));
    }

    public void PublishPendingCommand(PendingCommandAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        PendingCommandRequested?.Invoke(this, action);
    }

    public void PublishPendingDesktop(PendingDesktopAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        PendingDesktopRequested?.Invoke(this, action);
    }
}

public sealed class SchedulerMessageEventArgs : EventArgs
{
    public SchedulerMessageEventArgs(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }

    public string Message { get; }

    public bool IsError { get; }
}