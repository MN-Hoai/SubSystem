using Microsoft.AspNetCore.Http;
using Sub_Services.Execute;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SNP_SubSystem.Middleware
{
    /// <summary>
    /// Kiểm tra quyền truy cập trang.
    /// Chỉ áp dụng cho các request điều hướng trang (browser GET + Accept: text/html).
    /// AJAX / API call đều được bỏ qua tự động.
    /// </summary>
    public class PermissionMiddleware
    {
        private readonly RequestDelegate _next;

        public const string CookieName = "SNP.Auth.Session";

        // Path luôn được phép qua (public)
        private static readonly string[] _publicPrefixes = new[]
        {
            "/Login",
            "/css",
            "/js",
            "/lib",
            "/images",
            "/fonts",
            "/assets",
            "/uploads",
            "/favicon",
            "/_framework",
            "/Home/Error",
            "/Error",
        };

        public PermissionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, SubSystemService service)
        {
            var req = context.Request;
            var path = req.Path.Value ?? "/";

            // ── 1. Public path → cho qua không cần kiểm tra đăng nhập ─────────────
            foreach (var prefix in _publicPrefixes)
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    await _next(context);
                    return;
                }
            }

            // ── 2. Kiểm tra đăng nhập & cookie xác thực ───────────────────────────
            var user = context.User;
            bool isAuthenticated = user?.Identity != null && user.Identity.IsAuthenticated;
            bool hasAuthCookie = req.Cookies.ContainsKey(CookieName);

            // Nếu chưa đăng nhập hoặc cookie không còn tồn tại / hết hạn
            if (!isAuthenticated || !hasAuthCookie)
            {
                // Dọn dẹp cookie nếu cookie cũ đã hết hạn hoặc không hợp lệ
                if (hasAuthCookie && !isAuthenticated)
                {
                    context.Response.Cookies.Delete(CookieName);
                }

                bool isPageNavigation = req.Method == HttpMethods.Get
                                        && req.Headers["Accept"].ToString()
                                           .Contains("text/html", StringComparison.OrdinalIgnoreCase);

                if (isPageNavigation)
                {
                    var returnUrl = req.Path + req.QueryString;
                    var loginUrl = "/Login";
                    if (!string.IsNullOrEmpty(returnUrl) && returnUrl != "/")
                    {
                        loginUrl += $"?returnUrl={System.Net.WebUtility.UrlEncode(returnUrl)}";
                    }
                    context.Response.Redirect(loginUrl);
                }
                else
                {
                    // Request AJAX / API: trả về 401 Unauthorized để client xử lý đá ra
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsync("{\"success\":false,\"message\":\"Phiên đăng nhập đã hết hạn hoặc chưa đăng nhập. Vui lòng đăng nhập lại.\",\"redirect\":\"/Login\"}");
                }
                return;
            }

            // ── 3. Admin bypass ──────────────────────────────────────────────────
            var username = user?.Identity?.Name ?? "";
            if (string.Equals(username, "admin", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // ── 4. Request AJAX / API call của user đã đăng nhập ─────────────────
            // Chỉ áp dụng kiểm tra phân quyền trang cho browser GET HTML
            bool isPageNav = req.Method == HttpMethods.Get
                             && req.Headers["Accept"].ToString()
                                .Contains("text/html", StringComparison.OrdinalIgnoreCase);

            if (!isPageNav)
            {
                await _next(context);
                return;
            }

            // Chống lưu cache HTML để khi back trình duyệt sau đăng xuất không hiện lại trang cũ
            context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";

            // ── 5. Trang chủ luôn được vào đối với user đã đăng nhập ─────────────
            if (path == "/" || path.Equals("/Home", StringComparison.OrdinalIgnoreCase)
                            || path.Equals("/Home/Index", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // ── 6. Lấy userId từ claims ──────────────────────────────────────────
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId))
            {
                // Claim userId không hợp lệ → đá ra đăng nhập lại
                context.Response.Redirect("/Login");
                return;
            }

            // ── 7. Chế độ mở: DB chưa cấu hình trang nào → cho qua tất cả ───────
            var totalPages = await service.GetTotalActivePagesCount();
            if (totalPages == 0)
            {
                await _next(context);
                return;
            }

            // ── 8. Kiểm tra path với danh sách được phép ────────────────────────
            var allowedPaths = await service.GetUserAllowedPaths(userId);

            bool allowed = false;
            foreach (var allowedPath in allowedPaths)
            {
                if (string.IsNullOrWhiteSpace(allowedPath)) continue;
                if (path.StartsWith(allowedPath, StringComparison.OrdinalIgnoreCase)
                    || allowedPath.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                {
                    allowed = true;
                    break;
                }
            }

            if (allowed)
            {
                await _next(context);
            }
            else
            {
                context.Response.StatusCode = 403;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(Build403Html(path,
                    user.FindFirstValue("FullName") ?? username));
            }
        }

        private static string Build403Html(string path, string username)
        {
            return $@"<!DOCTYPE html>
<html lang=""vi"">
<head>
    <meta charset=""utf-8"" />
    <title>Không có quyền truy cập</title>
    <link href=""https://fonts.googleapis.com/css2?family=Inter:wght@400;600;700;800&display=swap"" rel=""stylesheet"">
    <link rel=""stylesheet"" href=""https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.4.0/css/all.min.css"">
    <style>
        * {{ box-sizing:border-box; margin:0; padding:0; }}
        body {{
            font-family:'Inter',sans-serif;
            background: linear-gradient(135deg, #f0f4ff 0%, #e8edf8 100%);
            display:flex; align-items:center; justify-content:center;
            min-height:100vh; color:#1e293b;
        }}
        .card {{
            background:white; border-radius:24px; padding:56px 48px;
            max-width:480px; width:100%; text-align:center;
            box-shadow: 0 25px 50px -12px rgba(0,0,0,0.1);
        }}
        .icon-wrap {{
            width:80px; height:80px; border-radius:20px; margin:0 auto 24px;
            background:linear-gradient(135deg,#fee2e2,#fecaca);
            display:flex; align-items:center; justify-content:center;
            font-size:36px; color:#ef4444;
        }}
        .code {{ font-size:14px; font-weight:700; color:#ef4444; letter-spacing:2px; margin-bottom:12px; }}
        h1 {{ font-size:26px; font-weight:800; margin-bottom:12px; color:#1e293b; }}
        p {{ font-size:14px; color:#64748b; line-height:1.7; margin-bottom:8px; }}
        .path-tag {{
            display:inline-block; margin-top:12px; background:#f1f5f9;
            color:#475569; padding:6px 14px; border-radius:8px;
            font-family:'Courier New',monospace; font-size:12px; font-weight:600;
        }}
        .actions {{ margin-top:32px; display:flex; gap:12px; justify-content:center; flex-wrap:wrap; }}
        .btn {{ padding:11px 22px; border-radius:10px; text-decoration:none;
            font-size:13px; font-weight:700; transition:all .2s; }}
        .btn-primary {{ background:linear-gradient(135deg,#1f3a67,#3a5fa8); color:white;
            box-shadow:0 4px 12px rgba(31,58,103,.2); }}
        .btn-primary:hover {{ transform:translateY(-2px); box-shadow:0 6px 16px rgba(31,58,103,.3); }}
        .btn-ghost {{ background:#f1f5f9; color:#475569; }}
        .btn-ghost:hover {{ background:#e2e8f0; }}
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""icon-wrap""><i class=""fa-solid fa-ban""></i></div>
        <div class=""code"">LỖI 403</div>
        <h1>Bạn không có quyền truy cập</h1>
        <p>Xin chào <strong>{System.Net.WebUtility.HtmlEncode(username)}</strong>, tài khoản của bạn chưa được cấp quyền truy cập vào trang này.</p>
        <p>Vui lòng liên hệ quản trị viên để được cấp quyền.</p>
        <div class=""path-tag"">{System.Net.WebUtility.HtmlEncode(path)}</div>
        <div class=""actions"">
            <a href=""/"" class=""btn btn-primary""><i class=""fa-solid fa-house"" style=""margin-right:6px""></i>Về trang chủ</a>
            <a href=""javascript:history.back()"" class=""btn btn-ghost""><i class=""fa-solid fa-arrow-left"" style=""margin-right:6px""></i>Quay lại</a>
        </div>
    </div>
</body>
</html>";
        }
    }
}
