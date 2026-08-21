using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lumine.Fairy.ContactManagement.Abstractions.Services;

public sealed class AppSingleInstanceCoordinator : IDisposable
{
    public const string ActivateCommand = "activate";

    private const string MutexPrefix = @"Local\FairyOS.SingleInstance.";
    private const string PipePrefix = "FairyOS.SingleInstance.";
    private static readonly TimeSpan DefaultSignalTimeout = TimeSpan.FromSeconds(2);

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _listenCts = new();
    private readonly ConcurrentQueue<string> _pendingPayloads = new();
    private readonly Task _listenTask;
    private readonly object _handlerGate = new();

    private EventHandler<string>? _activationRequested;
    private bool _disposed;

    private AppSingleInstanceCoordinator(string applicationId, Mutex mutex)
    {
        ApplicationId = applicationId;
        _mutex = mutex;
        _pipeName = BuildPipeName(applicationId);
        _listenTask = Task.Run(ListenAsync);
    }

    public string ApplicationId { get; }

    public event EventHandler<string>? ActivationRequested
    {
        add
        {
            lock (_handlerGate)
            {
                _activationRequested += value;
            }
        }
        remove
        {
            lock (_handlerGate)
            {
                _activationRequested -= value;
            }
        }
    }

    public static AppSingleInstanceCoordinator? TryCreatePrimary(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

        var mutex = new Mutex(initiallyOwned: false, BuildMutexName(applicationId), out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new AppSingleInstanceCoordinator(applicationId, mutex);
    }

    public static string BuildPathScopedApplicationId(string applicationId, string? baseDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

        var normalizedPath = NormalizeScopePath(baseDirectory ?? AppContext.BaseDirectory);
        var pathBytes = Encoding.UTF8.GetBytes(normalizedPath);
        var pathHash = Convert.ToHexStringLower(SHA256.HashData(pathBytes));
        return $"{applicationId}@{pathHash}";
    }

    public static bool TrySignalPrimaryInstance(string applicationId, string payload = ActivateCommand, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        using var client = new NamedPipeClientStream(".", BuildPipeName(applicationId), PipeDirection.Out, PipeOptions.None);

        try
        {
            client.Connect((int)(timeout ?? DefaultSignalTimeout).TotalMilliseconds);
            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true)
            {
                AutoFlush = true
            };
            writer.WriteLine(payload);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void DispatchPendingActivationRequests()
    {
        ThrowIfDisposed();

        while (_pendingPayloads.TryDequeue(out var payload))
        {
            RaiseActivationRequested(payload);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listenCts.Cancel();
        try
        {
            _listenTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }

        _listenCts.Dispose();
        _mutex.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!_listenCts.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.In,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await server.WaitForConnectionAsync(_listenCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            string? payload;
            try
            {
                payload = await reader.ReadLineAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            QueueOrRaiseActivation(payload.Trim());
        }
    }

    private void QueueOrRaiseActivation(string payload)
    {
        EventHandler<string>? handlers;
        lock (_handlerGate)
        {
            handlers = _activationRequested;
            if (handlers is null)
            {
                _pendingPayloads.Enqueue(payload);
                return;
            }
        }

        handlers.Invoke(this, payload);
    }

    private void RaiseActivationRequested(string payload)
    {
        EventHandler<string>? handlers;
        lock (_handlerGate)
        {
            handlers = _activationRequested;
        }

        handlers?.Invoke(this, payload);
    }

    private static string BuildMutexName(string applicationId)
    {
        return MutexPrefix + Sanitize(applicationId);
    }

    private static string BuildPipeName(string applicationId)
    {
        return PipePrefix + Sanitize(applicationId);
    }

    private static string Sanitize(string applicationId)
    {
        var builder = new StringBuilder(applicationId.Length);
        foreach (var ch in applicationId)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        return builder.ToString();
    }

    private static string NormalizeScopePath(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var normalized = Path.GetFullPath(baseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return OperatingSystem.IsWindows()
            ? normalized.ToUpperInvariant()
            : normalized;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}