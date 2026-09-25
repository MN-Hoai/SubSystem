using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sub_Entities.Entities;
using Sub_Services.Execute.Excel;

namespace Sub_Services.Execute.Background;

/// <summary>
/// Cấu hình cho ExcelMonitorWorker
/// </summary>
public class ExcelMonitorOptions
{
    public bool Enabled                  { get; set; } = true;
    public int DebounceSeconds           { get; set; } = 2;
    public int RetryCount                { get; set; } = 3;
    public int RetryDelayMilliseconds    { get; set; } = 1000;
}

/// <summary>
/// Background Worker: Quản lý FileSystemWatcher cho từng file Excel.
/// 
/// Flow:
/// 1. Startup: Load danh sách file từ DB → tạo FileSystemWatcher cho mỗi file
/// 2. Khi file thay đổi: Nhận event → Debounce 2s → gọi ExcelMonitorService
/// 3. Mỗi lần Save Excel chỉ tạo MỘT lần Compare (không duplicate)
/// 4. Xử lý retry khi file đang bị lock
/// </summary>
public class ExcelMonitorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExcelMonitorWorker> _logger;
    private readonly ExcelMonitorOptions _options;

    // FileID → FileSystemWatcher
    private readonly Dictionary<Guid, FileSystemWatcher> _watchers = new();

    // FileID → CancellationTokenSource của debounce timer
    private readonly Dictionary<Guid, CancellationTokenSource> _debounceCts = new();

    private readonly SemaphoreSlim _lock = new(1, 1);

    public ExcelMonitorWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ExcelMonitorWorker> logger,
        IOptions<ExcelMonitorOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _options      = options.Value;
    }

    // ──────────────────────────────────────────────────────────────────────
    // ExecuteAsync: vòng lặp chính
    // ──────────────────────────────────────────────────────────────────────
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("ExcelMonitorWorker bị tắt (Enabled=false)");
            return;
        }

        _logger.LogInformation("ExcelMonitorWorker đang khởi động...");

        // Load và thiết lập watcher cho tất cả file đang monitor
        await SetupAllWatchersAsync(stoppingToken);

        // Vòng lặp: mỗi 30 giây kiểm tra lại DB để phát hiện file mới được thêm vào
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                await SyncWatchersAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong vòng lặp ExcelMonitorWorker");
            }
        }

        // Cleanup
        CleanupAllWatchers();
        _logger.LogInformation("ExcelMonitorWorker đã dừng");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Setup watchers
    // ──────────────────────────────────────────────────────────────────────
    private async Task SetupAllWatchersAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SNP_SubSystemDBContext>();

        var files = await db.ExcelFiles
            .Where(f => f.Status == 1)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var file in files)
            AddWatcher(file.Id, file.FilePath, file.FileName);

        _logger.LogInformation("Đã thiết lập {Count} FileSystemWatcher(s)", _watchers.Count);
    }

    private async Task SyncWatchersAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SNP_SubSystemDBContext>();

        var files = await db.ExcelFiles
            .Where(f => f.Status == 1)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var activeIds = files.Select(f => f.Id).ToHashSet();

            // Xóa watcher cho file đã bị disable
            var toRemove = _watchers.Keys.Where(id => !activeIds.Contains(id)).ToList();
            foreach (var id in toRemove)
                RemoveWatcher(id);

            // Thêm watcher cho file mới
            foreach (var file in files)
            {
                if (!_watchers.ContainsKey(file.Id))
                    AddWatcher(file.Id, file.FilePath, file.FileName);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Watcher management
    // ──────────────────────────────────────────────────────────────────────
    private void AddWatcher(Guid fileId, string filePath, string fileName)
    {
        if (_watchers.ContainsKey(fileId)) return;

        var dir  = Path.GetDirectoryName(filePath);
        var name = Path.GetFileName(filePath);

        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            _logger.LogWarning("Thư mục không tồn tại: {Dir} (file: {FileName})", dir, fileName);
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(dir, name)
            {
                NotifyFilter         = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents  = true,
                IncludeSubdirectories = false,
            };

            // Bind events — dùng closure để capture fileId
            var capturedFileId = fileId;
            watcher.Changed += (_, e) => OnFileEvent(capturedFileId, e.FullPath, "Changed");
            watcher.Created += (_, e) => OnFileEvent(capturedFileId, e.FullPath, "Created");
            watcher.Renamed += (_, e) => OnFileEvent(capturedFileId, e.FullPath, "Renamed");
            watcher.Error   += (_, e) => OnWatcherError(capturedFileId, e.GetException());

            _watchers[fileId] = watcher;
            _logger.LogInformation("FileSystemWatcher đang theo dõi: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không thể tạo FileSystemWatcher cho {FilePath}", filePath);
        }
    }

    private void RemoveWatcher(Guid fileId)
    {
        if (_watchers.TryGetValue(fileId, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _watchers.Remove(fileId);
            _logger.LogInformation("Đã xóa FileSystemWatcher cho file {FileId}", fileId);
        }

        if (_debounceCts.TryGetValue(fileId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _debounceCts.Remove(fileId);
        }
    }

    private void CleanupAllWatchers()
    {
        foreach (var id in _watchers.Keys.ToList())
            RemoveWatcher(id);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Event handling với Debounce
    // ──────────────────────────────────────────────────────────────────────
    private void OnFileEvent(Guid fileId, string filePath, string eventType)
    {
        _logger.LogDebug("FileSystemWatcher event [{EventType}]: {FilePath}", eventType, filePath);

        // Debounce: hủy timer cũ, tạo timer mới
        lock (_debounceCts)
        {
            if (_debounceCts.TryGetValue(fileId, out var oldCts))
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }

            var cts = new CancellationTokenSource();
            _debounceCts[fileId] = cts;

            // Chạy async debounce task
            _ = DebounceAndProcessAsync(fileId, cts.Token);
        }
    }

    private async Task DebounceAndProcessAsync(Guid fileId, CancellationToken debounceToken)
    {
        try
        {
            // Chờ debounce: nếu có event mới → task này bị cancel
            await Task.Delay(TimeSpan.FromSeconds(_options.DebounceSeconds), debounceToken);
        }
        catch (OperationCanceledException)
        {
            // Bị debounce bởi event tiếp theo → không làm gì
            return;
        }

        // Sau debounce: xử lý với retry
        for (int attempt = 1; attempt <= _options.RetryCount; attempt++)
        {
            try
            {
                using var scope   = _scopeFactory.CreateScope();
                var monitorService = scope.ServiceProvider.GetRequiredService<IExcelMonitorService>();

                await monitorService.ProcessFileChangedAsync(fileId);
                _logger.LogDebug("Xử lý thành công file {FileId} (attempt {Attempt})", fileId, attempt);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi xử lý file {FileId} (attempt {Attempt}/{Max})",
                    fileId, attempt, _options.RetryCount);

                if (attempt < _options.RetryCount)
                    await Task.Delay(_options.RetryDelayMilliseconds);
            }
        }

        _logger.LogError("Không thể xử lý file {FileId} sau {Max} lần thử", fileId, _options.RetryCount);
    }

    private void OnWatcherError(Guid fileId, Exception ex)
    {
        _logger.LogError(ex, "FileSystemWatcher lỗi cho file {FileId}", fileId);

        // Reset watcher
        lock (_debounceCts)
        {
            if (_watchers.TryGetValue(fileId, out var watcher))
            {
                try { watcher.EnableRaisingEvents = false; watcher.Dispose(); } catch { }
                _watchers.Remove(fileId);
            }
        }

        // Thử tạo lại watcher sau 5 giây
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SNP_SubSystemDBContext>();
            var file = await db.ExcelFiles.FindAsync(fileId);
            if (file != null && file.Status == 1)
                AddWatcher(fileId, file.FilePath, file.FileName);
        });
    }

    // ──────────────────────────────────────────────────────────────────────
    // Public API: thêm/xóa watcher động (được gọi từ Controller sau khi thêm file mới)
    // ──────────────────────────────────────────────────────────────────────
    public void RegisterFile(Guid fileId, string filePath, string fileName)
    {
        lock (_debounceCts)
        {
            AddWatcher(fileId, filePath, fileName);
        }
    }

    public void UnregisterFile(Guid fileId)
    {
        lock (_debounceCts)
        {
            RemoveWatcher(fileId);
        }
    }
}
