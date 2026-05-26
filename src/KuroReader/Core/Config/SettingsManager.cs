using System.Text.Json;
using System.Text.Json.Serialization;

namespace KuroReader.Core.Config;

/// <summary>
/// Persists <see cref="AppSettings"/> to a JSON file in <c>%APPDATA%\KuroReader\config.json</c>.
/// Implements debounced saving to batch rapid setting changes into a single write.
/// </summary>
public sealed class SettingsManager : IDisposable
{
    private static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KuroReader");

    private static readonly string SettingsFilePath =
        Path.Combine(SettingsDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null // PascalCase to match property names
    };

    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly object _debounceLock = new();
    private CancellationTokenSource? _debounceCts;
    private bool _disposed;

    /// <summary>
    /// The current application settings. Modify properties directly, then call <see cref="QueueSave"/>.
    /// </summary>
    public AppSettings Settings { get; private set; } = new();

    /// <summary>
    /// Duration to wait before flushing settings to disk after the last change.
    /// </summary>
    public TimeSpan DebounceInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Loads settings from disk. If the file doesn't exist or is corrupt, returns defaults.
    /// </summary>
    public async Task LoadAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!File.Exists(SettingsFilePath))
        {
            Settings = new AppSettings();
            return;
        }

        try
        {
            await using var stream = new FileStream(
                SettingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions)
                .ConfigureAwait(false);

            Settings = loaded ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt or inaccessible config — use defaults silently.
            Settings = new AppSettings();
        }
    }

    /// <summary>
    /// Queues a debounced save. Multiple rapid calls will coalesce into a single disk write
    /// after <see cref="DebounceInterval"/> of inactivity.
    /// </summary>
    public void QueueSave()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_debounceLock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = DebouncedSaveAsync(token);
        }
    }

    /// <summary>
    /// Immediately saves settings to disk, bypassing the debounce timer.
    /// </summary>
    public async Task SaveNowAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Cancel any pending debounced save.
        lock (_debounceLock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }

        await WriteToDiskAsync().ConfigureAwait(false);
    }

    private async Task DebouncedSaveAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(DebounceInterval, token).ConfigureAwait(false);
            await WriteToDiskAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Debounce was reset — another save is coming.
        }
    }

    private async Task WriteToDiskAsync()
    {
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(SettingsDirectory);

            // Write to a temp file then atomically rename to prevent corruption on crash.
            string tempPath = SettingsFilePath + ".tmp";

            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, Settings, JsonOptions).ConfigureAwait(false);
            }

            File.Move(tempPath, SettingsFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Log in a real app; swallow here to avoid crashing the application.
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_debounceLock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }

        _saveLock.Dispose();
    }
}
