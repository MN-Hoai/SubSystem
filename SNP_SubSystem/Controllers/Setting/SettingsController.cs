using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SNP_SubSystem.Models;
using Sub_Services.Execute;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace SNP_SubSystem.Controllers.Setting
{
    public class SettingsController : Controller
    {
        private readonly SubSystemService _subSystemService;

        public SettingsController(SubSystemService subSystemService)
        {
            _subSystemService = subSystemService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var settings = await _subSystemService.GetSystemSettingsAsync();
            var model = new SettingsViewModel
            {
                SystemTitle        = settings?.GetValueOrDefault("SystemTitle", "SNP SubSystem") ?? "SNP SubSystem",
                SystemLogoUrl      = settings?.GetValueOrDefault("SystemLogo", "/assets/images/logo.png") ?? "/assets/images/logo.png",
                LoginBackgroundUrl = settings?.GetValueOrDefault("LoginBackground", "/assets/images/bg-login.jpg") ?? "/assets/images/bg-login.jpg",
                HomeBackgroundUrl  = settings?.GetValueOrDefault("HomeBackground", "") ?? ""
            };
            return View("~/Views/Setting/Index.cshtml", model);
        }

        [HttpPost]
        public async Task<IActionResult> Save(SettingsViewModel model)
        {
            var userId = HttpContext.Items["UserId"] as Guid?;

            if (!string.IsNullOrEmpty(model.SystemTitle))
                await _subSystemService.UpdateSettingAsync("SystemTitle", model.SystemTitle, userId);

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "settings");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            if (model.SystemLogoFile != null)
            {
                var fileName = "logo_" + DateTime.Now.Ticks + Path.GetExtension(model.SystemLogoFile.FileName);
                var filePath = Path.Combine(uploadsFolder, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                    await model.SystemLogoFile.CopyToAsync(stream);
                await _subSystemService.UpdateSettingAsync("SystemLogo", "/uploads/settings/" + fileName, userId);
            }

            if (model.LoginBackgroundFile != null)
            {
                var fileName = "login_bg_" + DateTime.Now.Ticks + Path.GetExtension(model.LoginBackgroundFile.FileName);
                var filePath = Path.Combine(uploadsFolder, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                    await model.LoginBackgroundFile.CopyToAsync(stream);
                await _subSystemService.UpdateSettingAsync("LoginBackground", "/uploads/settings/" + fileName, userId);
            }

            if (model.HomeBackgroundFile != null)
            {
                var fileName = "home_bg_" + DateTime.Now.Ticks + Path.GetExtension(model.HomeBackgroundFile.FileName);
                var filePath = Path.Combine(uploadsFolder, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                    await model.HomeBackgroundFile.CopyToAsync(stream);
                await _subSystemService.UpdateSettingAsync("HomeBackground", "/uploads/settings/" + fileName, userId);
            }

            TempData["SuccessMessage"] = "Cập nhật cài đặt thành công!";
            return RedirectToAction("Index");
        }

        // ═══════════════════════════════════════════════════════════════════
        //  DATA CLEANUP
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Trang dọn dẹp dữ liệu — trả về danh sách bảng kèm số lượng đã xóa mềm.</summary>
        [HttpGet]
        public async Task<IActionResult> DataCleanup()
        {
            var tables = await _subSystemService.GetCleanupTableSummariesAsync();
            return View("~/Views/Setting/DataCleanup.cshtml", tables);
        }

        /// <summary>API: Lấy dữ liệu đã xóa mềm của một bảng, có phân trang.</summary>
        [HttpGet]
        public async Task<IActionResult> GetDeletedData(string table, int page = 1, int pageSize = 20)
        {
            if (string.IsNullOrWhiteSpace(table))
                return BadRequest(new { success = false, message = "Thiếu tên bảng." });

            var result = await _subSystemService.GetDeletedRowsAsync(table, page, pageSize);

            // Serialize Dictionary<string, object> đúng cách
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            return Content(json, "application/json");
        }

        /// <summary>API: Khôi phục nhiều row (Status -2 → 1).</summary>
        [HttpPost]
        public async Task<IActionResult> RestoreRows([FromBody] CleanupActionRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.Table) || req.Ids == null || req.Ids.Count == 0)
                return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ." });

            var (restored, msg) = await _subSystemService.RestoreRowsAsync(req.Table, req.Ids);
            return Ok(new { success = true, restored, message = msg });
        }

        /// <summary>API: Xóa cứng (DELETE) nhiều row.</summary>
        [HttpPost]
        public async Task<IActionResult> HardDeleteRows([FromBody] CleanupActionRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.Table) || req.Ids == null || req.Ids.Count == 0)
                return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ." });

            var (deleted, msg) = await _subSystemService.HardDeleteRowsAsync(req.Table, req.Ids);
            return Ok(new { success = true, deleted, message = msg });
        }

        /// <summary>API: Xóa cứng toàn bộ soft-deleted của một bảng.</summary>
        [HttpPost]
        public async Task<IActionResult> PurgeTable([FromBody] PurgeTableRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.Table))
                return BadRequest(new { success = false, message = "Thiếu tên bảng." });

            var (deleted, msg) = await _subSystemService.PurgeTableAsync(req.Table);
            return Ok(new { success = true, deleted, message = msg });
        }
    }

    // ─── Request DTOs ────────────────────────────────────────────────────────
    public class CleanupActionRequest
    {
        public string       Table { get; set; }
        public List<string> Ids   { get; set; }
    }

    public class PurgeTableRequest
    {
        public string Table { get; set; }
    }
}
