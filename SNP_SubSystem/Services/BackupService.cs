using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Sub_Services.Execute;

namespace SNP_SubSystem.Services
{
    // Keys đồng bộ với BackupController
    internal static class BkKey
    {
        public const string Folder   = "Backup_FolderPath";
        public const string Enabled  = "Backup_AutoEnabled";
        public const string Interval = "Backup_IntervalHours";
        public const string MaxKeep  = "Backup_MaxKeepFiles";
    }

    /// <summary>
    /// Background service: tự động backup SQL Server theo lịch cấu hình trong DB.
    /// Cũng cung cấp các method backup/restore/list dùng chung cho BackupController.
    /// </summary>
    public class BackupService : BackgroundService
    {
        private readonly IServiceScopeFactory   _scopeFactory;
        private readonly IConfiguration         _configuration;
        private readonly ILogger<BackupService> _logger;
        private readonly IWebHostEnvironment    _env;

        public BackupService(
            IServiceScopeFactory   scopeFactory,
            IConfiguration         configuration,
            ILogger<BackupService> logger,
            IWebHostEnvironment    env)
        {
            _scopeFactory  = scopeFactory;
            _configuration = configuration;
            _logger        = logger;
            _env           = env;
        }

        // ── Background loop ────────────────────────────────────────────────
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[BackupService] Started.");
            while (!stoppingToken.IsCancellationRequested)
            {
                var cfg = await ReadConfigFromDb();
                if (cfg.AutoEnabled && cfg.IntervalHours > 0)
                {
                    try
                    {
                        var (ok, msg, _) = await RunBackupAsync();
                        if (ok) _logger.LogInformation("[BackupService] Auto-backup OK: {msg}", msg);
                        else    _logger.LogWarning("[BackupService] Auto-backup FAILED: {msg}", msg);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[BackupService] Auto-backup exception.");
                    }
                    await Task.Delay(TimeSpan.FromHours(cfg.IntervalHours), stoppingToken);
                }
                else
                {
                    // Auto-backup off hoặc chưa cấu hình: kiểm tra lại sau 5 phút
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }
        }

        // ── Config từ DB ───────────────────────────────────────────────────
        private async Task<(bool AutoEnabled, int IntervalHours, int MaxKeep, string FolderPath)> ReadConfigFromDb()
        {
            try
            {
                using var scope    = _scopeFactory.CreateScope();
                var svc            = scope.ServiceProvider.GetRequiredService<SubSystemService>();
                var settings       = await svc.GetSystemSettingsAsync() ?? new Dictionary<string, string>();

                var folder   = settings.GetValueOrDefault(BkKey.Folder,   "wwwroot/backups");
                var enabled  = settings.GetValueOrDefault(BkKey.Enabled,  "false") == "true";
                var interval = int.TryParse(settings.GetValueOrDefault(BkKey.Interval, "24"), out var h) ? h : 24;
                var maxKeep  = int.TryParse(settings.GetValueOrDefault(BkKey.MaxKeep,  "30"), out var m) ? m : 30;
                return (enabled, interval, maxKeep, folder);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[BackupService] Cannot read config from DB, using defaults.");
                return (false, 24, 30, "wwwroot/backups");
            }
        }

        // ── Public methods (dùng từ Controller) ────────────────────────────

        /// <summary>Thực hiện backup ngay lập tức. Trả về (success, message, fileName).</summary>
        public async Task<(bool Ok, string Message, string FileName)> RunBackupAsync()
        {
            var cfg      = await ReadConfigFromDb();
            var folder   = GetAbsoluteFolder(cfg.FolderPath);
            Directory.CreateDirectory(folder);

            var cs       = _configuration.GetConnectionString("DefaultConnection")!;
            var dbName   = ExtractDatabase(cs);
            var fileName = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
            var filePath = Path.Combine(folder, fileName);

            var sql = $"BACKUP DATABASE [{dbName}] TO DISK = N'{filePath}' WITH FORMAT, INIT, STATS = 10;";
            try
            {
                await using var conn = new SqlConnection(cs);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 600 };
                await cmd.ExecuteNonQueryAsync();

                // Dọn file cũ nếu vượt MaxKeepFiles
                CleanOldBackups(folder, cfg.MaxKeep);

                return (true, $"Sao lưu thành công: {fileName}", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BackupService] Backup failed.");
                return (false, $"Lỗi sao lưu: {ex.Message}", string.Empty);
            }
        }

        /// <summary>Khôi phục database từ file backup đã chọn.</summary>
        public async Task<(bool Ok, string Message)> RestoreAsync(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..") || !fileName.EndsWith(".bak"))
                return (false, "Tên file không hợp lệ.");

            var cfg      = await ReadConfigFromDb();
            var folder   = GetAbsoluteFolder(cfg.FolderPath);
            var filePath = Path.Combine(folder, fileName);
            if (!File.Exists(filePath)) return (false, "File backup không tồn tại.");

            var cs     = _configuration.GetConnectionString("DefaultConnection")!;
            var dbName = ExtractDatabase(cs);
            var masterCs = ReplaceDatabase(cs, "master");

            var sql = $@"
                ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                RESTORE DATABASE [{dbName}] FROM DISK = N'{filePath}' WITH REPLACE, RECOVERY;
                ALTER DATABASE [{dbName}] SET MULTI_USER;";
            try
            {
                await using var conn = new SqlConnection(masterCs);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 900 };
                await cmd.ExecuteNonQueryAsync();
                return (true, $"Khôi phục thành công từ: {fileName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BackupService] Restore failed.");
                return (false, $"Lỗi khôi phục: {ex.Message}");
            }
        }

        /// <summary>Trả về danh sách file backup, mới nhất trước.</summary>
        public async Task<List<BackupFileInfo>> GetBackupListAsync()
        {
            var cfg    = await ReadConfigFromDb();
            var folder = GetAbsoluteFolder(cfg.FolderPath);
            if (!Directory.Exists(folder)) return new();
            return BuildFileList(folder);
        }

        /// <summary>Overload đồng bộ — dùng khi không có folder từ DB (fallback appsettings).</summary>
        public List<BackupFileInfo> GetBackupList()
        {
            var folder = GetAbsoluteFolder("wwwroot/backups");
            if (!Directory.Exists(folder)) return new();
            return BuildFileList(folder);
        }

        /// <summary>Xóa một file backup.</summary>
        public (bool Ok, string Message) DeleteBackupFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..") || !fileName.EndsWith(".bak"))
                return (false, "Tên file không hợp lệ.");

            // Tìm file trong tất cả subfolder của wwwroot/backups
            var root = GetAbsoluteFolder("wwwroot/backups");
            var path = Path.Combine(root, fileName);
            if (!File.Exists(path)) return (false, "File không tồn tại.");
            File.Delete(path);
            return (true, "Đã xóa file backup.");
        }

        // ── Helpers ────────────────────────────────────────────────────────
        private string GetAbsoluteFolder(string folderPath)
        {
            if (Path.IsPathRooted(folderPath)) return folderPath;
            return Path.Combine(_env.ContentRootPath, folderPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static List<BackupFileInfo> BuildFileList(string folder) =>
            Directory.GetFiles(folder, "*.bak")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => new BackupFileInfo
                {
                    FileName    = f.Name,
                    SizeBytes   = f.Length,
                    CreatedAt   = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    SizeDisplay = FormatSize(f.Length)
                })
                .ToList();

        private static string ExtractDatabase(string cs)
        {
            var b = new SqlConnectionStringBuilder(cs);
            return b.InitialCatalog;
        }

        private static string ReplaceDatabase(string cs, string db)
        {
            var b = new SqlConnectionStringBuilder(cs) { InitialCatalog = db };
            return b.ConnectionString;
        }

        private static void CleanOldBackups(string folder, int keep)
        {
            if (keep <= 0) return;
            var files = Directory.GetFiles(folder, "*.bak")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();
            foreach (var f in files.Skip(keep)) f.Delete();
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F2} GB";
            if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F2} MB";
            if (bytes >= 1_024)         return $"{bytes / 1_024.0:F1} KB";
            return $"{bytes} B";
        }
    }

    public class BackupFileInfo
    {
        public string FileName    { get; set; }
        public long   SizeBytes   { get; set; }
        public string SizeDisplay { get; set; }
        public string CreatedAt   { get; set; }
    }
}
