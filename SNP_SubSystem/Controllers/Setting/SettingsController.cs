using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SNP_SubSystem.Models;
using Sub_Services.Execute;
using System;
using System.IO;
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
                SystemTitle = settings.GetValueOrDefault("SystemTitle", "SNP SubSystem"),
                SystemLogoUrl = settings.GetValueOrDefault("SystemLogo", "/assets/images/logo.png"),
                LoginBackgroundUrl = settings.GetValueOrDefault("LoginBackground", "/assets/images/bg-login.jpg"),
                HomeBackgroundUrl = settings.GetValueOrDefault("HomeBackground", "")
            };
            return View("~/Views/Setting/Index.cshtml", model);
        }

        [HttpPost]
        public async Task<IActionResult> Save(SettingsViewModel model)
        {
            var userId = HttpContext.Items["UserId"] as Guid?;
            
            if (!string.IsNullOrEmpty(model.SystemTitle))
            {
                await _subSystemService.UpdateSettingAsync("SystemTitle", model.SystemTitle, userId);
            }

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "settings");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            if (model.SystemLogoFile != null)
            {
                var fileName = "logo_" + DateTime.Now.Ticks + Path.GetExtension(model.SystemLogoFile.FileName);
                var filePath = Path.Combine(uploadsFolder, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.SystemLogoFile.CopyToAsync(stream);
                }
                await _subSystemService.UpdateSettingAsync("SystemLogo", "/uploads/settings/" + fileName, userId);
            }

            if (model.LoginBackgroundFile != null)
            {
                var fileName = "login_bg_" + DateTime.Now.Ticks + Path.GetExtension(model.LoginBackgroundFile.FileName);
                var filePath = Path.Combine(uploadsFolder, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.LoginBackgroundFile.CopyToAsync(stream);
                }
                await _subSystemService.UpdateSettingAsync("LoginBackground", "/uploads/settings/" + fileName, userId);
            }

            if (model.HomeBackgroundFile != null)
            {
                var fileName = "home_bg_" + DateTime.Now.Ticks + Path.GetExtension(model.HomeBackgroundFile.FileName);
                var filePath = Path.Combine(uploadsFolder, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.HomeBackgroundFile.CopyToAsync(stream);
                }
                await _subSystemService.UpdateSettingAsync("HomeBackground", "/uploads/settings/" + fileName, userId);
            }

            TempData["SuccessMessage"] = "Cập nhật cài đặt thành công!";
            return RedirectToAction("Index");
        }
    }
}
