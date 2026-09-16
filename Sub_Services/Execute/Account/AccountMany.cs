using Microsoft.EntityFrameworkCore;
using Sub_Entities.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Sub_Services.Execute
{
    public partial class SubSystemService
    {
        // =====================================================================
        //  ACCOUNT MANAGEMENT
        // =====================================================================

        public class Account_QueryRequest
        {
            public string Keyword    { get; set; }
            public int?   Status     { get; set; }
            public int    Page       { get; set; } = 1;
            public int    PageSize   { get; set; } = 20;
        }

        public class Account_ListResult
        {
            public int  TotalCount { get; set; }
            public int  Page       { get; set; }
            public int  PageSize   { get; set; }
            public List<Account_Item> Items { get; set; } = new();
        }

        public class Account_Item
        {
            public Guid     Id           { get; set; }
            public string   Username     { get; set; }
            public string   Msnv         { get; set; }
            public string   FullName     { get; set; }
            public string   Email        { get; set; }
            public string   Keyword      { get; set; }
            public int      Status       { get; set; }
            public DateTime ExpiryDate   { get; set; }
            public DateTime CreateDate   { get; set; }
            public DateTime UpdateDate   { get; set; }
        }

        public class Account_UpsertRequest
        {
            public Guid?    Id         { get; set; }
            public string   Username   { get; set; }
            public string   Msnv       { get; set; }
            public string   FullName   { get; set; }
            public string   Email      { get; set; }
            public string   Password   { get; set; }   // null/empty = giữ nguyên khi update
            public DateTime ExpiryDate { get; set; }
            public int      Status     { get; set; } = 1;
        }

        /// <summary>Lấy danh sách tài khoản có phân trang, tìm kiếm, lọc trạng thái.</summary>
        public async Task<Account_ListResult> GetAccountList(Account_QueryRequest req)
        {
            var q = _context.Users.AsQueryable();

            // Lọc tìm kiếm
            if (!string.IsNullOrWhiteSpace(req.Keyword))
            {
                var kw = req.Keyword.Trim().ToLower();
                q = q.Where(u =>
                    u.Username.ToLower().Contains(kw) ||
                    (u.FullName != null && u.FullName.ToLower().Contains(kw)) ||
                    (u.Msnv     != null && u.Msnv.ToLower().Contains(kw)) ||
                    (u.Email    != null && u.Email.ToLower().Contains(kw)));
            }

            // Lọc trạng thái
            if (req.Status.HasValue)
                q = q.Where(u => u.Status == req.Status.Value);
            else
                q = q.Where(u => u.Status >= -2); // hiển thị tất cả bao gồm đã xoá mềm (-2)

            var total = await q.CountAsync();

            var page     = Math.Max(1, req.Page);
            var pageSize = Math.Clamp(req.PageSize, 5, 100);

            var items = await q
                .OrderByDescending(u => u.CreateDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new Account_Item
                {
                    Id         = u.Id,
                    Username   = u.Username,
                    Msnv       = u.Msnv,
                    FullName   = u.FullName,
                    Email      = u.Email,
                    Keyword    = u.Keyword,
                    Status     = u.Status,
                    ExpiryDate = u.ExpiryDate,
                    CreateDate = u.CreateDate,
                    UpdateDate = u.UpdateDate,
                })
                .ToListAsync();

            return new Account_ListResult
            {
                TotalCount = total,
                Page       = page,
                PageSize   = pageSize,
                Items      = items,
            };
        }

        /// <summary>Lấy chi tiết một tài khoản.</summary>
        public async Task<Account_Item> GetAccountById(Guid id)
        {
            return await _context.Users
                .Where(u => u.Id == id)
                .Select(u => new Account_Item
                {
                    Id         = u.Id,
                    Username   = u.Username,
                    Msnv       = u.Msnv,
                    FullName   = u.FullName,
                    Email      = u.Email,
                    Keyword    = u.Keyword,
                    Status     = u.Status,
                    ExpiryDate = u.ExpiryDate,
                    CreateDate = u.CreateDate,
                    UpdateDate = u.UpdateDate,
                })
                .FirstOrDefaultAsync();
        }

        /// <summary>Tạo mới hoặc cập nhật tài khoản.</summary>
        public async Task<(bool Ok, string Message, Guid? NewId)> UpsertAccount(Account_UpsertRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.Username))
                    return (false, "Username không được để trống.", null);

                var now = DateTime.Now;

                if (req.Id.HasValue && req.Id.Value != Guid.Empty)
                {
                    // UPDATE
                    var entity = await _context.Users.FirstOrDefaultAsync(u => u.Id == req.Id.Value);
                    if (entity == null)
                        return (false, "Không tìm thấy tài khoản.", null);

                    // Kiểm tra username trùng (ngoại trừ chính nó)
                    var dup = await _context.Users
                        .AnyAsync(u => u.Username == req.Username.Trim() && u.Id != req.Id.Value);
                    if (dup)
                        return (false, $"Username '{req.Username}' đã tồn tại.", null);

                    entity.Username   = req.Username.Trim();
                    entity.Msnv       = req.Msnv?.Trim();
                    entity.FullName   = req.FullName?.Trim();
                    entity.Email      = req.Email?.Trim();
                    entity.ExpiryDate = req.ExpiryDate;
                    entity.Status     = req.Status;
                    entity.UpdateDate = now;

                    if (!string.IsNullOrWhiteSpace(req.Password))
                    {
                        var newSalt = GenerateHashCode();
                        entity.HashCode     = newSalt;
                        entity.PasswordHash = HashPasswordWithSalt(req.Password, newSalt);
                    }

                    await _context.SaveChangesAsync();
                    return (true, "Cập nhật tài khoản thành công.", entity.Id);
                }
                else
                {
                    // CREATE
                    if (string.IsNullOrWhiteSpace(req.Password))
                        return (false, "Mật khẩu không được để trống khi tạo mới.", null);

                    var dup = await _context.Users.AnyAsync(u => u.Username == req.Username.Trim());
                    if (dup)
                        return (false, $"Username '{req.Username}' đã tồn tại.", null);

                    var salt = GenerateHashCode();

                    var entity = new User
                    {
                        Id           = Guid.NewGuid(),
                        Username     = req.Username.Trim(),
                        Msnv         = req.Msnv?.Trim() ?? string.Empty,
                        FullName     = req.FullName?.Trim() ?? string.Empty,
                        Email        = req.Email?.Trim() ?? string.Empty,
                        HashCode     = salt,
                        PasswordHash = HashPasswordWithSalt(req.Password, salt),
                        Keyword      = string.Empty,
                        ExpiryDate   = req.ExpiryDate,
                        Status       = req.Status,
                        CreateDate   = now,
                        UpdateDate   = now,
                    };

                    _context.Users.Add(entity);
                    await _context.SaveChangesAsync();
                    return (true, "Tạo tài khoản thành công.", entity.Id);
                }
            }
            catch (Exception ex)
            {
                return (false, "Lỗi hệ thống: " + ex.Message, null);
            }
        }

        /// <summary>Cập nhật trạng thái một hoặc nhiều tài khoản.</summary>
        public async Task<(bool Ok, string Message)> SetAccountStatus(List<Guid> ids, int targetStatus)
        {
            try
            {
                var list = await _context.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
                if (!list.Any())
                    return (false, "Không tìm thấy tài khoản nào.");

                foreach (var u in list)
                {
                    u.Status     = targetStatus;
                    u.UpdateDate = DateTime.Now;
                }

                await _context.SaveChangesAsync();
                return (true, $"Đã cập nhật {list.Count} tài khoản.");
            }
            catch (Exception ex)
            {
                return (false, "Lỗi hệ thống: " + ex.Message);
            }
        }

        /// <summary>Gia hạn thời gian sử dụng tài khoản.</summary>
        public async Task<(bool Ok, string Message)> ExtendAccountExpiry(List<Guid> ids, int years = 1)
        {
            try
            {
                var list = await _context.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
                if (!list.Any())
                    return (false, "Không tìm thấy tài khoản nào.");

                var now = DateTime.Now;
                foreach (var u in list)
                {
                    // Nếu ngày hết hạn cũ đã qua thì cộng từ hôm nay, nếu còn hạn thì cộng tiếp từ ngày hết hạn đó
                    if (u.ExpiryDate < now)
                    {
                        u.ExpiryDate = now.AddYears(years);
                    }
                    else
                    {
                        u.ExpiryDate = u.ExpiryDate.AddYears(years);
                    }
                    
                    // Nếu tài khoản đang hết hạn (-1), tự động mở khoá (1)
                    if (u.Status == -1)
                        u.Status = 1;

                    u.UpdateDate = now;
                }

                await _context.SaveChangesAsync();
                return (true, $"Đã gia hạn {list.Count} tài khoản thêm {years} năm.");
            }
            catch (Exception ex)
            {
                return (false, "Lỗi hệ thống: " + ex.Message);
            }
        }

        /// <summary>Đếm tổng số Page đang hoạt động trong DB (dùng để phát hiện chế độ mở).</summary>
        public async Task<int> GetTotalActivePagesCount()
            => await _context.Pages.CountAsync(p => p.Status == 1 && p.Path != null && p.Path != "");

        /// <summary>Lấy tất cả Path của trang mà user được phép truy cập (qua Role).</summary>
        public async Task<HashSet<string>> GetUserAllowedPaths(Guid userId)
        {
            // Lấy tất cả RoleId user đang có (status=1)
            var roleIds = await _context.UserRoles
                .Where(ur => ur.UserId == userId && ur.Status == 1)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            if (!roleIds.Any())
            {
                // Thử lấy qua UserPagePermission trực tiếp
                var directPaths = await _context.UserPagePermissions
                    .Where(up => up.UserId == userId && up.IsGranted == 1 && up.Status == 1)
                    .Join(_context.Pages.Where(p => p.Status == 1 && p.Path != null && p.Path != ""),
                        up => up.PageId, p => p.Id, (up, p) => p.Path)
                    .Distinct().ToListAsync();
                return new HashSet<string>(directPaths, StringComparer.OrdinalIgnoreCase);
            }

            // Lấy PageId từ RolePagePermission (chỉ cần tồn tại record = có quyền xem trang)
            var pageIds = await _context.RolePagePermissions
                .Where(rp => roleIds.Contains(rp.RoleId) && rp.Status == 1)
                .Select(rp => rp.PageId)
                .Distinct()
                .ToListAsync();

            // Map PageId → Path
            var pathsFromRoles = await _context.Pages
                .Where(p => pageIds.Contains(p.Id) && p.Status == 1 && p.Path != null && p.Path != "")
                .Select(p => p.Path)
                .ToListAsync();

            // Lấy thêm qua UserPagePermission trực tiếp (IsGranted=1)
            var pathsFromUser = await _context.UserPagePermissions
                .Where(up => up.UserId == userId && up.IsGranted == 1 && up.Status == 1)
                .Join(_context.Pages.Where(p => p.Status == 1 && p.Path != null && p.Path != ""),
                    up => up.PageId, p => p.Id, (up, p) => p.Path)
                .Distinct().ToListAsync();

            var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in pathsFromRoles) all.Add(p);
            foreach (var p in pathsFromUser) all.Add(p);
            return all;
        }

        /// <summary>Kiểm tra thông tin đăng nhập.</summary>
        public async Task<(bool Ok, string Message, Account_Item User)> VerifyLogin(string username, string password)
        {
            try
            {
                var u = await _context.Users.FirstOrDefaultAsync(x => x.Username == username.Trim());
                if (u == null)
                    return (false, "Tài khoản không tồn tại.", null);

                // Kiểm tra trạng thái
                if (u.Status != 1)
                    return (false, "Tài khoản của bạn đang bị khoá.", null);

                // Kiểm tra hạn sử dụng
                if (u.ExpiryDate < DateTime.Now)
                    return (false, "Tài khoản của bạn đã hết hạn sử dụng.", null);

                // Kiểm tra mật khẩu
                var hashedInput = HashPasswordWithSalt(password, u.HashCode);
                if (hashedInput != u.PasswordHash)
                    return (false, "Mật khẩu không chính xác.", null);

                var accountItem = new Account_Item
                {
                    Id         = u.Id,
                    Username   = u.Username,
                    Msnv       = u.Msnv,
                    FullName   = u.FullName,
                    Email      = u.Email,
                    Status     = u.Status,
                    ExpiryDate = u.ExpiryDate,
                };

                return (true, "Đăng nhập thành công.", accountItem);
            }
            catch (Exception ex)
            {
                return (false, "Lỗi hệ thống: " + ex.Message, null);
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        /// <summary>Sinh chuỗi salt ngẫu nhiên dạng hex 32 ký tự.</summary>
        private static string GenerateHashCode()
            => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLower();

        /// <summary>Hash mật khẩu có salt: SHA256(password + hashCode).</summary>
        private static string HashPasswordWithSalt(string password, string hashCode)
        {
            using var sha256 = SHA256.Create();
            var combined = password + hashCode;
            var bytes    = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
            return Convert.ToHexString(bytes).ToLower();
        }
    }
}
