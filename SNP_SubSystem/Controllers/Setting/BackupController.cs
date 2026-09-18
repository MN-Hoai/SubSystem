using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SNP_SubSystem.Services;
using Sub_Services.Execute;

namespace SNP_SubSystem.Controllers.Setting
{
    public class BackupController : Controller
    {
        private readonly BackupService        _backup;
        private readonly SubSystemService     _svc;
        private readonly IWebHostEnvironment  _env;

        // Keys dùng trong bảng Setting
        private const string K_Folder   = "Backup_FolderPath";
        private const string K_Enabled  = "Backup_AutoEnabled";
        private const string K_Interval = "Backup_IntervalHours";
        private const string K_MaxKeep  = "Backup_MaxKeepFiles";

        public BackupController(
            BackupService       backup,
            SubSystemService    svc,
            IWebHostEnvironment env)
        {
            _backup = backup;
            _svc    = svc;
            _env    = env;
        }

        // GET /Backup/GetStatus — đọc từ DB Setting
        [HttpGet]
        public async Task<IActionResult> GetStatus()
        {
            var settings = await _svc.GetSystemSettingsAsync() ?? new Dictionary<string, string>();
            return Ok(new
            {
                folderPath              = settings.GetValueOrDefault(K_Folder,   "wwwroot/backups"),
                autoBackupEnabled       = settings.GetValueOrDefault(K_Enabled,  "false") == "true",
                autoBackupIntervalHours = int.TryParse(settings.GetValueOrDefault(K_Interval, "24"),  out var h) ? h : 24,
                maxKeepFiles            = int.TryParse(settings.GetValueOrDefault(K_MaxKeep,  "30"),  out var m) ? m : 30,
            });
        }

        // GET /Backup/GetList
        [HttpGet]
        public async Task<IActionResult> GetList()
        {
            var list = await _backup.GetBackupListAsync();
            return Ok(list);
        }

        // POST /Backup/RunNow
        [HttpPost]
        public async Task<IActionResult> RunNow()
        {
            var (ok, msg, fileName) = await _backup.RunBackupAsync();
            return ok
                ? Ok(new { success = true, message = msg, fileName })
                : BadRequest(new { success = false, message = msg });
        }

        // POST /Backup/UpdateConfig — lưu vào bảng Setting
        [HttpPost]
        public async Task<IActionResult> UpdateConfig([FromBody] BackupConfigRequest req)
        {
            var userId = HttpContext.Items["UserId"] as Guid?;
            var folder = req.FolderPath?.Trim() ?? "wwwroot/backups";

            await _svc.UpdateSettingAsync(K_Folder,   folder,                                        userId);
            await _svc.UpdateSettingAsync(K_Enabled,  req.AutoBackupEnabled ? "true" : "false",      userId);
            await _svc.UpdateSettingAsync(K_Interval, req.AutoBackupIntervalHours.ToString(),         userId);
            await _svc.UpdateSettingAsync(K_MaxKeep,  req.MaxKeepFiles.ToString(),                    userId);

            return Ok(new { success = true, message = "Đã lưu cấu hình sao lưu." });
        }

        // POST /Backup/Restore
        [HttpPost]
        public async Task<IActionResult> Restore([FromBody] FileNameRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.FileName))
                return BadRequest(new { success = false, message = "Tên file không hợp lệ." });

            var (ok, msg) = await _backup.RestoreAsync(req.FileName);
            return ok
                ? Ok(new { success = true, message = msg })
                : BadRequest(new { success = false, message = msg });
        }

        // GET /Backup/Download?file=xxx.bak
        [HttpGet]
        public async Task<IActionResult> Download(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || file.Contains("..") || !file.EndsWith(".bak"))
                return BadRequest("Tên file không hợp lệ.");

            var folder = await GetFolderFromDb();
            var path   = Path.Combine(folder, file);
            if (!System.IO.File.Exists(path)) return NotFound("File không tồn tại.");

            var bytes = System.IO.File.ReadAllBytes(path);
            return File(bytes, "application/octet-stream", file);
        }

        // DELETE /Backup/Delete?file=xxx.bak
        [HttpDelete]
        public IActionResult Delete(string file)
        {
            var (ok, msg) = _backup.DeleteBackupFile(file);
            return ok ? Ok(new { success = true, message = msg })
                      : BadRequest(new { success = false, message = msg });
        }

        // ── Helper ────────────────────────────────────────────────────────
        private async Task<string> GetFolderFromDb()
        {
            var settings = await _svc.GetSystemSettingsAsync() ?? new Dictionary<string, string>();
            var rel      = settings.GetValueOrDefault(K_Folder, "wwwroot/backups");
            if (Path.IsPathRooted(rel)) return rel;
            return Path.Combine(_env.ContentRootPath, rel.Replace('/', Path.DirectorySeparatorChar));
        }

        public class FileNameRequest  { public string FileName { get; set; } }
        public class BackupConfigRequest
        {
            public string FolderPath              { get; set; }
            public bool   AutoBackupEnabled       { get; set; }
            public int    AutoBackupIntervalHours { get; set; }
            public int    MaxKeepFiles            { get; set; }
        }
    }
}
