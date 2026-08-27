using System.Threading.Channels;
using Comet.Models;
using Comet.Services.Abstractions;

namespace Comet.Recording;

/// <summary>
/// Writes raw receive buffers to a file on one background consumer. The bounded
/// queue keeps serial callbacks non-blocking while preventing unbounded memory use.
/// </summary>
public sealed class RawReceiveRecordingService : IRawReceiveRecordingService
{
    private const int QueueCapacity = 256;
    private RecordingSession? _activeSession;
    private int _disposed;
    private string? _lastFilePath;
    private Task _lastCompletion = Task.CompletedTask;

    public event EventHandler? StateChanged;

    public event EventHandler<RawReceiveRecordingFailedEventArgs>? RecordingFailed;

    public bool IsRecording => Volatile.Read(ref _activeSession) is not null;

    public string? FilePath => Volatile.Read(ref _activeSession)?.FilePath ?? _lastFilePath;

    public void Start(string filePath)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (IsRecording)
        {
            throw new InvalidOperationException("A raw receive recording is already active.");
        }

        if (!Volatile.Read(ref _lastCompletion).IsCompleted)
        {
            throw new InvalidOperationException("The previous recording is still being finalized.");
        }

        var fullPath = Path.GetFullPath(filePath);
        var stream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        var session = new RecordingSession(fullPath, stream, channel);
        session.Completion = WriteSessionAsync(session);

        if (Interlocked.CompareExchange(ref _activeSession, session, null) is not null)
        {
            session.Writer.TryComplete();
            session.Completion.GetAwaiter().GetResult();
            throw new InvalidOperationException("A raw receive recording is already active.");
        }

        _lastFilePath = fullPath;
        Volatile.Write(ref _lastCompletion, session.Completion);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool TryWrite(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            return true;
        }

        var session = Volatile.Read(ref _activeSession);
        if (session is null)
        {
            return false;
        }

        if (session.Writer.TryWrite(data))
        {
            return true;
        }

        FailSession(
            session,
            new IOException("Raw receive recording stopped because the file writer could not keep up."));
        return false;
    }

    public async Task StopAsync()
    {
        var session = Interlocked.Exchange(ref _activeSession, null);
        if (session is not null)
        {
            session.Writer.TryComplete();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        await Volatile.Read(ref _lastCompletion).ConfigureAwait(false);
    }

    private async Task WriteSessionAsync(RecordingSession session)
    {
        Exception? failure = null;
        try
        {
            while (await session.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (session.Reader.TryRead(out var data))
                {
                    await session.Stream.WriteAsync(data).ConfigureAwait(false);
                }
            }

            await session.Stream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                await session.Stream.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                failure ??= exception;
            }

            Interlocked.CompareExchange(ref _activeSession, null, session);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        if (failure is not null)
        {
            RaiseRecordingFailed(session, failure);
        }
    }

    private void FailSession(RecordingSession session, Exception exception)
    {
        if (Interlocked.CompareExchange(ref _activeSession, null, session) != session)
        {
            return;
        }

        session.Writer.TryComplete();
        StateChanged?.Invoke(this, EventArgs.Empty);
        RaiseRecordingFailed(session, exception);
    }

    private void RaiseRecordingFailed(RecordingSession session, Exception exception)
    {
        if (Interlocked.Exchange(ref session.FailureRaised, 1) == 0)
        {
            RecordingFailed?.Invoke(this, new RawReceiveRecordingFailedEventArgs(exception));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        StopAsync().GetAwaiter().GetResult();
    }

    private sealed class RecordingSession(
        string filePath,
        FileStream stream,
        Channel<byte[]> channel)
    {
        public string FilePath { get; } = filePath;

        public FileStream Stream { get; } = stream;

        public ChannelReader<byte[]> Reader { get; } = channel.Reader;

        public ChannelWriter<byte[]> Writer { get; } = channel.Writer;

        public Task Completion { get; set; } = Task.CompletedTask;

        public int FailureRaised;
    }
}
