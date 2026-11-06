using System.Collections.Concurrent;
using SkiaSharp;

namespace GpxToFusion.Map;

public readonly record struct TileKey(string Source, int Z, int X, int Y);

/// <summary>Disk + in-memory tile cache with throttled downloading (max 2 concurrent requests).</summary>
public sealed class TileCache
{
    private static readonly HttpClient Http = CreateClient();
    private readonly SemaphoreSlim _downloadGate = new(2);
    private readonly ConcurrentDictionary<TileKey, Task<SKImage?>> _pending = new();
    private readonly Dictionary<TileKey, LinkedListNode<(TileKey Key, SKImage Image)>> _mem = new();
    private readonly LinkedList<(TileKey Key, SKImage Image)> _lru = new();
    private readonly HashSet<TileKey> _failed = new();
    private readonly HashSet<TileKey> _notFound = new(); // server answered 404 (no data at this zoom)
    private readonly object _lock = new();
    private const int MaxInMemory = 600;

    public string CacheRoot { get; }

    /// <summary>Raised (on a thread-pool thread) when a tile requested via TryGet has been loaded.</summary>
    public event Action<TileKey>? TileLoaded;

    public TileCache(string? cacheRoot = null)
    {
        CacheRoot = cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GpxToFusion", "tiles");
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("GpxToFusion/1.0 (personal tool)");
        return c;
    }

    private string DiskPath(TileKey k) =>
        Path.Combine(CacheRoot, k.Source, k.Z.ToString(), k.X.ToString(), k.Y + ".png");

    /// <summary>Returns the tile if it is in memory; otherwise starts loading it (disk/network) and returns null.</summary>
    public SKImage? TryGet(TileSource src, int z, int x, int y)
    {
        if (!Normalize(src, z, x, y, out var key)) return null;
        lock (_lock)
        {
            if (_mem.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Image;
            }
            if (_failed.Contains(key)) return null;
        }
        LoadAsync(src, key, CancellationToken.None).ContinueWith(t =>
        {
            if (t.Status == TaskStatus.RanToCompletion && t.Result != null) TileLoaded?.Invoke(key);
        }, TaskScheduler.Default);
        return null;
    }

    /// <summary>Gets a tile, downloading if needed. Returns null if unavailable.</summary>
    public Task<SKImage?> GetAsync(TileSource src, int z, int x, int y, CancellationToken ct)
    {
        if (!Normalize(src, z, x, y, out var key)) return Task.FromResult<SKImage?>(null);
        lock (_lock)
        {
            if (_mem.TryGetValue(key, out var node)) return Task.FromResult<SKImage?>(node.Value.Image);
            if (_notFound.Contains(key)) return Task.FromResult<SKImage?>(null);
            _failed.Remove(key); // explicit request: retry failed tiles
        }
        return LoadAsync(src, key, ct);
    }

    /// <summary>True if the server reported that this tile does not exist (e.g. no imagery at this zoom).</summary>
    public bool IsNotFound(TileSource src, int z, int x, int y)
    {
        if (!Normalize(src, z, x, y, out var key)) return true;
        lock (_lock) return _notFound.Contains(key);
    }

    /// <summary>
    /// Prefetches <paramref name="tiles"/>; for tiles the server does not have, prefetches their parents
    /// (lower zoom) so <see cref="MapComposer"/> can upscale them instead.
    /// </summary>
    public async Task PrefetchWithFallbackAsync(TileSource src, IReadOnlyCollection<(int Z, int X, int Y)> tiles,
        IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        await PrefetchAsync(src, tiles, progress, ct).ConfigureAwait(false);
        var level = tiles;
        for (int i = 0; i < MapComposer.MaxFallbackLevels; i++)
        {
            var parents = level.Where(t => t.Z > 0 && IsNotFound(src, t.Z, t.X, t.Y))
                .Select(t => (Z: t.Z - 1, X: t.X >> 1, Y: t.Y >> 1)).Distinct().ToList();
            if (parents.Count == 0) break;
            await PrefetchAsync(src, parents, progress, ct).ConfigureAwait(false);
            level = parents;
        }
    }

    public async Task PrefetchAsync(TileSource src, IReadOnlyCollection<(int Z, int X, int Y)> tiles,
        IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        int done = 0, total = tiles.Count;
        progress?.Report((0, total));
        var tasks = tiles.Select(async t =>
        {
            await GetAsync(src, t.Z, t.X, t.Y, ct).ConfigureAwait(false);
            progress?.Report((Interlocked.Increment(ref done), total));
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static bool Normalize(TileSource src, int z, int x, int y, out TileKey key)
    {
        int n = 1 << z;
        x = ((x % n) + n) % n; // wrap horizontally
        key = new TileKey(src.Key, z, x, y);
        return z >= 0 && y >= 0 && y < n;
    }

    private Task<SKImage?> LoadAsync(TileSource src, TileKey key, CancellationToken ct)
    {
        return _pending.GetOrAdd(key, k => LoadCoreAsync(src, k, ct).ContinueWith(t =>
        {
            _pending.TryRemove(k, out _);
            if (t.Status != TaskStatus.RanToCompletion || t.Result == null)
            {
                if (!t.IsCanceled) lock (_lock) _failed.Add(k);
                return null;
            }
            return t.Result;
        }, TaskScheduler.Default));
    }

    private async Task<SKImage?> LoadCoreAsync(TileSource src, TileKey key, CancellationToken ct)
    {
        string path = DiskPath(key);
        byte[]? data = null;
        bool notFound = false;
        if (File.Exists(path))
        {
            data = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            if (data.Length == 0) notFound = true; // cached 404 marker
        }
        else
        {
            await _downloadGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string url = src.GetUrl(key.Z, key.X, key.Y);
                for (int attempt = 0; attempt < 3 && data == null; attempt++)
                {
                    try
                    {
                        using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
                        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) { notFound = true; data = []; break; }
                        resp.EnsureSuccessStatusCode();
                        data = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                    }
                    catch (Exception) when (!ct.IsCancellationRequested && attempt < 2)
                    {
                        await Task.Delay(500 * (attempt + 1), ct).ConfigureAwait(false);
                    }
                }
            }
            finally { _downloadGate.Release(); }

            if (data != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, data, ct).ConfigureAwait(false);
            }
        }
        if (notFound)
        {
            lock (_lock) _notFound.Add(key);
            return null;
        }
        if (data == null) return null;

        using var encoded = SKImage.FromEncodedData(data);
        if (encoded == null) return null;
        var raster = encoded.ToRasterImage(true); // decode now: cheap & thread-safe drawing later
        if (raster == null) return null;
        AddToMemory(key, raster);
        return raster;
    }

    private void AddToMemory(TileKey key, SKImage image)
    {
        lock (_lock)
        {
            if (_mem.ContainsKey(key)) return;
            _mem[key] = _lru.AddFirst((key, image));
            while (_lru.Count > MaxInMemory)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _mem.Remove(last.Value.Key);
                // Not disposed explicitly: a renderer may still hold it; the GC finalizes it.
            }
        }
    }
}
