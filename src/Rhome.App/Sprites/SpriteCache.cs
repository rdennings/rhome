using System.Collections.Concurrent;

namespace Rhome.App.Sprites;

/// <summary>How a download of many sprites went.</summary>
public sealed record SpriteDownload(int Total, int AlreadyHad, int Downloaded, int Failed);

/// <summary>
/// The sprites rhome has loaded, kept on the device for good (issue #48): a file at a pinned commit never changes, so
/// once a sprite is here rhome never asks for it again, and works offline. Files live in
/// <c>&lt;folder&gt;/&lt;commit&gt;/</c>; bumping the commit starts a new cache.
/// </summary>
public sealed class SpriteCache(SpriteTable table, string folder, HttpClient http)
{
    static readonly byte[] PngStart = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    readonly string root = Path.Combine(folder, table.Commit);
    readonly ConcurrentDictionary<string, Task<byte[]?>> loading = new();
    readonly ConcurrentDictionary<string, DateTimeOffset> failedAt = new();

    public SpriteTable Table => table;

    public bool Has(string path) => System.IO.File.Exists(LocalPath(path));

    /// <summary>
    /// A sprite's bytes, from the device, or downloaded and kept. Null when it can't be had: offline, say. A file that
    /// failed isn't asked for again for a minute, so an offline device doesn't keep trying on every redraw.
    /// </summary>
    public Task<byte[]?> GetAsync(string path, CancellationToken cancel = default)
    {
        var local = LocalPath(path);
        if (System.IO.File.Exists(local))
            return ReadAsync(local, cancel);
        if (failedAt.TryGetValue(path, out var when) && DateTimeOffset.UtcNow - when < RetryAfter)
            return Task.FromResult<byte[]?>(null);

        // One download per file, however many slots show it at once.
        var mine = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = loading.GetOrAdd(path, mine.Task);
        if (task == mine.Task)
            _ = LoadAsync(path, mine, cancel);
        return task;
    }

    async Task LoadAsync(string path, TaskCompletionSource<byte[]?> result, CancellationToken cancel)
    {
        try
        {
            var bytes = await DownloadAsync(path, cancel);
            loading.TryRemove(KeyValuePair.Create(path, result.Task));
            result.SetResult(bytes);
        }
        catch (OperationCanceledException ex)
        {
            loading.TryRemove(KeyValuePair.Create(path, result.Task));
            result.SetCanceled(ex.CancellationToken);
        }
    }

    /// <summary>Fetches every file not on the device yet, a few at a time. Reports (done, total) as it goes.</summary>
    public async Task<SpriteDownload> DownloadAllAsync(IReadOnlyList<string> paths, IProgress<(int Done, int Total)>? progress,
        CancellationToken cancel = default)
    {
        var missing = paths.Where(p => !Has(p)).ToList();
        int done = 0, failed = 0;
        progress?.Report((0, missing.Count));
        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = cancel }, async (path, ct) =>
        {
            failedAt.TryRemove(path, out _);
            if (await GetAsync(path, ct) is null)
                Interlocked.Increment(ref failed);
            progress?.Report((Interlocked.Increment(ref done), missing.Count));
        });
        return new SpriteDownload(paths.Count, paths.Count - missing.Count, missing.Count - failed, failed);
    }

    string LocalPath(string path) => Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));

    static async Task<byte[]?> ReadAsync(string local, CancellationToken cancel)
    {
        try
        {
            return await System.IO.File.ReadAllBytesAsync(local, cancel);
        }
        catch (IOException)
        {
            return null;
        }
    }

    async Task<byte[]?> DownloadAsync(string path, CancellationToken cancel)
    {
        try
        {
            using var response = await http.GetAsync(table.UrlFor(path), cancel);
            if (!response.IsSuccessStatusCode)
                return Failed(path, $"HTTP {(int)response.StatusCode}");
            var bytes = await response.Content.ReadAsByteArrayAsync(cancel);
            if (!bytes.AsSpan().StartsWith(PngStart))
                return Failed(path, "not a PNG");

            // Written next to its place, then moved there, so a half-written file is never taken for a sprite.
            var local = LocalPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            var partial = local + ".part";
            await System.IO.File.WriteAllBytesAsync(partial, bytes, cancel);
            System.IO.File.Move(partial, local, overwrite: true);
            return bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
                                       or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            return Failed(path, ex.Message);
        }
    }

    byte[]? Failed(string path, string why)
    {
        if (failedAt.IsEmpty)
            AppLog.Write($"sprites: couldn't load {path} ({why}); showing names instead");
        failedAt[path] = DateTimeOffset.UtcNow;
        return null;
    }
}
