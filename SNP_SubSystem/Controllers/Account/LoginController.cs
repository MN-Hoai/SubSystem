using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Sub_Services.Execute;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SNP_SubSystem.Controllers.Account
{
    public class LoginController : Controller
    {
        private readonly SubSystemService _service;

        public LoginController(SubSystemService service)
        {
            _service = service;
        }

        // GET: /Login
        [HttpGet]
        public IActionResult Index(string returnUrl = null)
        {
            // Cờ cấu hình bật/tắt trang đăng nhập (đổi thành false để tắt)
            bool isLogin = true; 
            if (!isLogin)
            {
                return Redirect("/");
            }

            // Nếu đã đăng nhập thì về trang chủ
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);
                return Redirect("/");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View("~/Views/Account/Login/Login.cshtml");
        }

        // POST: /Login
        [HttpPost]
        public async Task<IActionResult> Index(string username, string password, string returnUrl = null)
        {
            // Cờ cấu hình bật/tắt trang đăng nhập
            bool isLogin = true; 
            if (!isLogin)
            {
                return Redirect("/");
            }

            ViewData["ReturnUrl"] = returnUrl;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "Vui lòng nhập tài khoản và mật khẩu.";
                return View("~/Views/Account/Login/Login.cshtml");
            }

            var (ok, msg, user) = await _service.VerifyLogin(username, password);
            if (!ok || user == null)
            {
                ViewBag.Error = msg;
                return View("~/Views/Account/Login/Login.cshtml");
            }

            // Tạo claims cho Cookie Auth
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim("FullName", user.FullName ?? ""),
                new Claim("Msnv", user.Msnv ?? ""),
                new Claim("Email", user.Email ?? "")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            // Ghi session/cookie
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = true } // default expire time 8h cấu hình trong Program.cs
            );

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            
            return Redirect("/");
        }

        // GET: /Login/CheckSession
        [HttpGet]
        public async Task<IActionResult> CheckSession()
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated || !Request.Cookies.ContainsKey(SNP_SubSystem.Middleware.PermissionMiddleware.CookieName))
            {
                return Unauthorized(new { authenticated = false, message = "Phiên làm việc đã hết hạn hoặc cookie không tồn tại." });
            }

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userIdStr, out var userId))
            {
                bool isExpired = await _service.IsUserExpiredOrLocked(userId);
                if (isExpired)
                {
                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    Response.Cookies.Delete(SNP_SubSystem.Middleware.PermissionMiddleware.CookieName);
                    return Unauthorized(new { authenticated = false, message = "Tài khoản đã hết hạn hoặc bị khoá." });
                }
            }

            return Ok(new { authenticated = true });
        }

        // GET: /Login/Logout
        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            Response.Cookies.Delete(SNP_SubSystem.Middleware.PermissionMiddleware.CookieName);
            return RedirectToAction("Index");
        }
    }
}
