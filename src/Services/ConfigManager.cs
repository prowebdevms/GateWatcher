using GateWatcher.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GateWatcher.Services
{
    public sealed class ConfigManager : IDisposable
    {
        public AppConfig Current { get; private set; }
        public event Action<AppConfig>? OnChanged;

        private readonly string _path;
        private readonly FileSystemWatcher _watcher;
        private readonly System.Timers.Timer _debounce;
        private readonly SemaphoreSlim _ioGate = new(1, 1);

        private volatile bool _disposed;
        private volatile bool _suppressNextReload;
        private long _lastWriteTicks;           // when we last saved
        private byte[]? _lastSavedHash;         // content hash to ignore our own changes

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public ConfigManager(string path)
        {
            _path = path;

            // initial load (create default if not present)
            if (!File.Exists(_path))
            {
                Current = AppConfig.Default();
                WriteAtomic(Current);
            }
            else
            {
                Current = ReadWithRetry(_path) ?? AppConfig.Default();
            }

            // debounce timer (fires once 250ms after last FS event)
            _debounce = new System.Timers.Timer(250) { AutoReset = false };
            _debounce.Elapsed += (_, __) => SafeReload();

            // watcher
            var dir = Path.GetDirectoryName(_path)!;
            var name = Path.GetFileName(_path);
            _watcher = new FileSystemWatcher(dir, name)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            _watcher.Changed += OnFsEvent;
            _watcher.Created += OnFsEvent;
            _watcher.Renamed += OnFsEvent;
            _watcher.EnableRaisingEvents = true;
        }

        public void Save(Action<AppConfig> mutate)
        {
            if (_disposed) return;

            _ioGate.Wait();
            try
            {
                mutate(Current);
                WriteAtomic(Current);
                // We just wrote: ignore the next reload (our own change)
                _suppressNextReload = true;
            }
            finally
            {
                _ioGate.Release();
            }
        }

        private void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            if (_disposed) return;

            // debounce rapid events
            _debounce.Stop();
            _debounce.Start();
        }

        private void SafeReload()
        {
            if (_disposed) return;

            _ioGate.Wait();
            try
            {
                // If we just saved within the last ~1s and content matches, skip reload
                if (_suppressNextReload && (DateTime.UtcNow.Ticks - _lastWriteTicks) < TimeSpan.FromSeconds(1).Ticks)
                {
                    var currentBytes = TryReadAllBytes(_path);
                    if (currentBytes != null && _lastSavedHash != null && ByteArrayEquals(currentBytes, _lastSavedHash))
                    {
                        _suppressNextReload = false; // consume suppression
                        return;
                    }
                }

                var cfg = ReadWithRetry(_path);
                if (cfg is null) return; // still locked; skip this round

                Current = cfg;
                _suppressNextReload = false;
            }
            finally
            {
                _ioGate.Release();
            }

            try { OnChanged?.Invoke(Current); } catch { /* ignore */ }
        }

        private void WriteAtomic(AppConfig cfg)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            string tmp = _path + ".tmp";
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(cfg, JsonOpts);

            // write temp with exclusive lock
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }

            // replace target (atomic on Windows for same volume)
            File.Copy(tmp, _path, overwrite: true);
            File.Delete(tmp);

            // record for suppression
            _lastWriteTicks = DateTime.UtcNow.Ticks;
            _lastSavedHash = SHA256.HashData(data);
        }

        private static AppConfig? ReadWithRetry(string path)
        {
            // Try up to 8 times with small backoff (total ~600–700ms)
            const int attempts = 8;
            int delayMs = 50;

            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    // open with shared read so we can read while an editor holds it
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    string json = sr.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(json)) throw new IOException("Empty config file");
                    return JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
                }
                catch (IOException)
                {
                    // another process is still writing; back off and retry
                }
                catch (UnauthorizedAccessException)
                {
                    // can happen transiently on rename/replace; back off and retry
                }
                Thread.Sleep(delayMs);
                delayMs = Math.Min(200, delayMs + 25);
            }

            // final fail: return null and keep previous config
            return null;
        }

        private static byte[]? TryReadAllBytes(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private static bool ByteArrayEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _watcher.EnableRaisingEvents = false; } catch { }
            try
            {
                _watcher.Changed -= OnFsEvent;
                _watcher.Created -= OnFsEvent;
                _watcher.Renamed -= OnFsEvent;
                _watcher.Dispose();
            }
            catch { }

            try { _debounce.Stop(); _debounce.Dispose(); } catch { }
            try { _ioGate.Dispose(); } catch { }
        }
    }
}
