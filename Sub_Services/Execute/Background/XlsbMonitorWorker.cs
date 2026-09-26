using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sub_Entities.Entities;
using Sub_Services.Execute.Excel;

namespace Sub_Services.Execute.Background;

/// <summary>
/// Cấu hình riêng cho XlsbMonitorWorker.
/// </summary>
public class XlsbMonitorOptions
{
    /// <summary>Bật/tắt worker này.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Chu kỳ polling (giây).
    /// Mỗi chu kỳ worker duyệt toàn bộ file .xlsb đang active,
    /// tính SHA256 và gọi ProcessFileChangedAsync nếu hash thay đổi.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 10;

    /// <summary>Số lần retry khi đọc file thất bại.</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>Khoảng cách giữa các lần retry (ms).</summary>
    public int RetryDelayMilliseconds { get; set; } = 1000;
}

/// <summary>
/// Background Worker theo dõi file Excel dạng .xlsb bằng cơ chế POLLING.
///
/// Lý do tách riêng:
/// - File .xlsb khi Excel save không ghi thẳng vào file gốc mà dùng cơ chế
///   "ghi temp → rename" khiến FileSystemWatcher (dùng bởi ExcelMonitorWorker)
///   không nhận được event Changed một cách đáng tin cậy.
/// - Worker này thay thế bằng cách poll định kỳ: tính SHA256 mỗi N giây và
///   gọi ProcessFileChangedAsync nếu hash thay đổi.
///
/// Điểm quan trọng:
/// - KHÔNG xung đột với ExcelMonitorWorker: ProcessFileChangedAsync có hash check
///   nội tại — nếu 2 worker cùng xử lý 1 file, lần thứ 2 sẽ thấy hash không đổi
///   và tự động bỏ qua, không ghi log trùng.
/// - Chỉ xử lý file có FileType = "xlsb" và Status = 1.
/// - Tất cả tính năng (ChangeLog, Snapshot, Compare) giữ nguyên hệ thống cũ.
/// </summary>
public class XlsbMonitorWorker : BackgroundService
{
    private readonly IServiceScopeFactory               _scopeFactory;
    private readonly ILogger<XlsbMonitorWorker>         _logger;
    private readonly XlsbMonitorOptions                 _options;

    // Semaphore để tránh 2 chu kỳ poll chạy đồng thời (nếu chu kỳ ngắn hơn thời gian xử lý)
    private readonly SemaphoreSlim _pollLock = new(1, 1);

    public XlsbMonitorWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<XlsbMonitorWorker> logger,
        IOptions<XlsbMonitorOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _options      = options.Value;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Vòng lặp chính
    // ──────────────────────────────────────────────────────────────────────
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("XlsbMonitorWorker bị tắt (Enabled=false).");
            return;
        }

        _logger.LogInformation(
            "XlsbMonitorWorker đang khởi động. Chu kỳ poll: {Interval}s",
            _options.PollingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.PollingIntervalSeconds),
                    stoppingToken);

                await PollAllXlsbFilesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong vòng lặp XlsbMonitorWorker");
            }
        }

        _logger.LogInformation("XlsbMonitorWorker đã dừng.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Poll toàn bộ file .xlsb đang active
    // ──────────────────────────────────────────────────────────────────────
    private async Task PollAllXlsbFilesAsync(CancellationToken cancellationToken)
    {
        // Tránh 2 chu kỳ chạy chồng nhau nếu xử lý lâu hơn PollingInterval
        if (!await _pollLock.WaitAsync(0, cancellationToken))
        {
            _logger.LogDebug("XlsbMonitorWorker: chu kỳ trước chưa xong, bỏ qua chu kỳ này.");
            return;
        }

        try
        {
            List<ExcelFile> xlsbFiles;

            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SNP_SubSystemDBContext>();
                xlsbFiles = await db.ExcelFiles
                    .Where(f => f.Status == 1 && f.FileType == "xlsb")
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
            }

            if (xlsbFiles.Count == 0) return;

            _logger.LogDebug("XlsbMonitorWorker: đang poll {Count} file .xlsb", xlsbFiles.Count);

            // Xử lý song song tối đa 4 file cùng lúc để không block quá lâu
            var semaphore = new SemaphoreSlim(4);
            var tasks = xlsbFiles.Select(file => ProcessWithRetryAsync(file, semaphore, cancellationToken));
            await Task.WhenAll(tasks);
        }
        finally
        {
            _pollLock.Release();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Xử lý 1 file với retry
    // ──────────────────────────────────────────────────────────────────────
    private async Task ProcessWithRetryAsync(
        ExcelFile file,
        SemaphoreSlim concurrencyLimiter,
        CancellationToken cancellationToken)
    {
        await concurrencyLimiter.WaitAsync(cancellationToken);
        try
        {
            for (int attempt = 1; attempt <= _options.RetryCount; attempt++)
            {
                try
                {
                    using var scope        = _scopeFactory.CreateScope();
                    var monitorService     = scope.ServiceProvider.GetRequiredService<IExcelMonitorService>();

                    // ProcessFileChangedAsync đã có hash check nội tại:
                    // nếu hash không đổi so với lần poll trước → tự động bỏ qua
                    await monitorService.ProcessFileChangedAsync(file.Id, cancellationToken);

                    _logger.LogDebug(
                        "XlsbMonitorWorker: poll thành công [{FileName}] (attempt {Attempt})",
                        file.FileName, attempt);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw; // Không retry khi bị cancel
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "XlsbMonitorWorker: lỗi poll [{FileName}] (attempt {Attempt}/{Max})",
                        file.FileName, attempt, _options.RetryCount);

                    if (attempt < _options.RetryCount)
                        await Task.Delay(_options.RetryDelayMilliseconds, cancellationToken);
                }
            }

            _logger.LogError(
                "XlsbMonitorWorker: không thể xử lý [{FileName}] sau {Max} lần thử",
                file.FileName, _options.RetryCount);
        }
        finally
        {
            concurrencyLimiter.Release();
        }
    }
}
