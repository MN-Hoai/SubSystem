using Microsoft.EntityFrameworkCore;
using Sub_Entities.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sub_Services.Execute
{
    public partial class SubSystemService
    {
        // =====================================================================
        //  ROLE MANAGEMENT
        // =====================================================================

        public class Role_ListItem
        {
            public Guid     Id          { get; set; }
            public string   Code        { get; set; }
            public string   Name        { get; set; }
            public string   Description { get; set; }
            public int      Status      { get; set; }
            public int      UserCount   { get; set; }
            public DateTime CreateDate  { get; set; }
            public DateTime UpdateDate  { get; set; }
        }

        public class Role_Detail
        {
            public Guid     Id          { get; set; }
            public string   Code        { get; set; }
            public string   Name        { get; set; }
            public string   Description { get; set; }
            public string   Keyword     { get; set; }
            public int      Status      { get; set; }
            public List<Role_PagePermission> PagePermissions { get; set; } = new();
        }

        public class Role_PagePermission
        {
            public Guid   PageId         { get; set; }
            public string PageName       { get; set; }
            public string PageCode       { get; set; }
            public string Path           { get; set; }
            public Guid?  ParentId       { get; set; }
            public string ParentName     { get; set; }
            public int    SortOrder      { get; set; }
            public List<Role_PermissionItem> Permissions { get; set; } = new();
        }

        public class Role_PermissionItem
        {
            public Guid   Id     { get; set; }
            public string Code   { get; set; }
            public string Name   { get; set; }
            public bool   Granted { get; set; }
        }

        public class Role_UpsertRequest
        {
            public Guid?   Id          { get; set; }
            public string  Code        { get; set; }
            public string  Name        { get; set; }
            public string  Description { get; set; }
            public string  Keyword     { get; set; }
            public int     Status      { get; set; } = 1;
            /// <summary>PageId → list PermissionId được cấp</summary>
            public List<Role_PagePermissionSet> PagePermissions { get; set; } = new();
        }

        public class Role_PagePermissionSet
        {
            public Guid       PageId        { get; set; }
            public List<Guid> PermissionIds { get; set; } = new();
        }

        public class Role_AccountItem
        {
            public Guid   UserId   { get; set; }
            public string Username { get; set; }
            public string FullName { get; set; }
            public string Msnv     { get; set; }
            public int    Status   { get; set; }
        }

        // ---------------------------------------------------------------
        // GET: Danh sách Role (tất cả, kể cả đã khóa)
        // ---------------------------------------------------------------
        public async Task<List<Role_ListItem>> GetRoleList(string keyword = null)
        {
            var query = _context.Roles.AsQueryable();
            if (!string.IsNullOrWhiteSpace(keyword))
                query = query.Where(r => r.Name.Contains(keyword) || r.Code.Contains(keyword));

            var roles = await query
                .Where(r => r.Status >= 0)
                .OrderBy(r => r.Name)
                .AsNoTracking()
                .ToListAsync();

            // Đếm số user mỗi role
            var userCounts = await _context.UserRoles
                .GroupBy(ur => ur.RoleId)
                .Select(g => new { RoleId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RoleId, x => x.Count);

            return roles.Select(r => new Role_ListItem
            {
                Id          = r.Id,
                Code        = r.Code,
                Name        = r.Name,
                Description = r.Description,
                Status      = r.Status,
                UserCount   = userCounts.TryGetValue(r.Id, out var c) ? c : 0,
                CreateDate  = r.CreateDate,
                UpdateDate  = r.UpdateDate
            }).ToList();
        }

        // ---------------------------------------------------------------
        // GET: Chi tiết Role kèm danh sách Page + Permission
        // ---------------------------------------------------------------
        public async Task<Role_Detail> GetRoleDetail(Guid roleId)
        {
            var role = await _context.Roles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == roleId);

            if (role == null) return null;

            // Quyền đang được cấp
            var granted = await _context.RolePagePermissions
                .Where(rp => rp.RoleId == roleId && rp.Status == 1)
                .Select(rp => new { rp.PageId, rp.PermissionId })
                .AsNoTracking()
                .ToListAsync();

            var grantedSet = granted
                .GroupBy(x => x.PageId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.PermissionId).ToHashSet());

            // Tất cả Pages (đang hoạt động)
            var pages = await _context.Pages
                .Where(p => p.Status == 1)
                .OrderBy(p => p.SortOrder)
                .AsNoTracking()
                .ToListAsync();

            // Tất cả Permissions (đang hoạt động)
            var permissions = await _context.Permissions
                .Where(p => p.Status == 1)
                .OrderBy(p => p.Name)
                .AsNoTracking()
                .ToListAsync();

            // Build page lookup for parent names
            var pageDict = pages.ToDictionary(p => p.Id, p => p.Name);

            var pagePermissions = pages.Select(page => new Role_PagePermission
            {
                PageId     = page.Id,
                PageName   = page.Name,
                PageCode   = page.Code,
                Path       = page.Path,
                ParentId   = page.ParentId,
                ParentName = page.ParentId.HasValue && pageDict.TryGetValue(page.ParentId.Value, out var pn) ? pn : null,
                SortOrder  = page.SortOrder,
                Permissions = permissions.Select(perm => new Role_PermissionItem
                {
                    Id      = perm.Id,
                    Code    = perm.Code,
                    Name    = perm.Name,
                    Granted = grantedSet.TryGetValue(page.Id, out var perms) && perms.Contains(perm.Id)
                }).ToList()
            }).ToList();

            return new Role_Detail
            {
                Id              = role.Id,
                Code            = role.Code,
                Name            = role.Name,
                Description     = role.Description,
                Keyword         = role.Keyword,
                Status          = role.Status,
                PagePermissions = pagePermissions
            };
        }

        // ---------------------------------------------------------------
        // GET: Danh sách Pages + Permissions (dùng khi tạo role mới)
        // ---------------------------------------------------------------
        public async Task<List<Role_PagePermission>> GetAllPagesWithPermissions()
        {
            var pages = await _context.Pages
                .Where(p => p.Status == 1)
                .OrderBy(p => p.SortOrder)
                .AsNoTracking()
                .ToListAsync();

            var permissions = await _context.Permissions
                .Where(p => p.Status == 1)
                .OrderBy(p => p.Name)
                .AsNoTracking()
                .ToListAsync();

            var pageDict = pages.ToDictionary(p => p.Id, p => p.Name);

            return pages.Select(page => new Role_PagePermission
            {
                PageId     = page.Id,
                PageName   = page.Name,
                PageCode   = page.Code,
                Path       = page.Path,
                ParentId   = page.ParentId,
                ParentName = page.ParentId.HasValue && pageDict.TryGetValue(page.ParentId.Value, out var pn) ? pn : null,
                SortOrder  = page.SortOrder,
                Permissions = permissions.Select(perm => new Role_PermissionItem
                {
                    Id      = perm.Id,
                    Code    = perm.Code,
                    Name    = perm.Name,
                    Granted = false
                }).ToList()
            }).ToList();
        }

        // ---------------------------------------------------------------
        // GET: Accounts thuộc một Role
        // ---------------------------------------------------------------
        public async Task<List<Role_AccountItem>> GetAccountsByRole(Guid roleId)
        {
            return await _context.UserRoles
                .Where(ur => ur.RoleId == roleId)
                .Join(_context.Users,
                    ur => ur.UserId,
                    u  => u.Id,
                    (ur, u) => new Role_AccountItem
                    {
                        UserId   = u.Id,
                        Username = u.Username,
                        FullName = u.FullName,
                        Msnv     = u.Msnv,
                        Status   = u.Status
                    })
                .AsNoTracking()
                .ToListAsync();
        }

        // ---------------------------------------------------------------
        // POST: Tạo mới / cập nhật Role + RolePagePermission
        // ---------------------------------------------------------------
        public async Task<(bool ok, string msg, Guid? id)> UpsertRole(Role_UpsertRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return (false, "Tên role không được để trống.", null);

            Role role;
            bool isNew = false;

            if (req.Id.HasValue && req.Id.Value != Guid.Empty)
            {
                role = await _context.Roles.FindAsync(req.Id.Value);
                if (role == null) return (false, "Không tìm thấy role.", null);
                role.UpdateDate = DateTime.Now;
            }
            else
            {
                isNew = true;
                role = new Role { Id = Guid.NewGuid(), CreateDate = DateTime.Now, UpdateDate = DateTime.Now };
                _context.Roles.Add(role);
            }

            role.Code        = req.Code?.Trim();
            role.Name        = req.Name.Trim();
            role.Description = req.Description?.Trim();
            role.Keyword     = req.Keyword?.Trim();
            role.Status      = req.Status;

            // Cập nhật RolePagePermission: xóa cũ, thêm mới
            if (!isNew)
            {
                var oldPerms = _context.RolePagePermissions.Where(rp => rp.RoleId == role.Id);
                _context.RolePagePermissions.RemoveRange(oldPerms);
            }

            foreach (var pagePerm in req.PagePermissions ?? new())
            {
                foreach (var permId in pagePerm.PermissionIds ?? new())
                {
                    _context.RolePagePermissions.Add(new RolePagePermission
                    {
                        Id           = Guid.NewGuid(),
                        RoleId       = role.Id,
                        PageId       = pagePerm.PageId,
                        PermissionId = permId,
                        Status       = 1,
                        CreateDate   = DateTime.Now,
                        UpdateDate   = DateTime.Now
                    });
                }
            }

            await _context.SaveChangesAsync();
            return (true, isNew ? "Đã tạo role thành công." : "Đã cập nhật role.", role.Id);
        }

        // ---------------------------------------------------------------
        // POST: Cập nhật trạng thái Role
        // ---------------------------------------------------------------
        public async Task<(bool ok, string msg)> SetRoleStatus(List<Guid> ids, int status)
        {
            var roles = await _context.Roles
                .Where(r => ids.Contains(r.Id))
                .ToListAsync();

            if (!roles.Any()) return (false, "Không tìm thấy role.");

            foreach (var r in roles) { r.Status = status; r.UpdateDate = DateTime.Now; }
            await _context.SaveChangesAsync();
            return (true, $"Đã cập nhật {roles.Count} role.");
        }

        // ---------------------------------------------------------------
        // POST: Gán / bỏ gán Account vào Role
        // ---------------------------------------------------------------
        public async Task<(bool ok, string msg)> AssignAccountToRole(Guid roleId, Guid userId)
        {
            var exists = await _context.UserRoles
                .AnyAsync(ur => ur.RoleId == roleId && ur.UserId == userId);
            if (exists) return (false, "Tài khoản đã thuộc role này.");

            _context.UserRoles.Add(new UserRole
            {
                Id         = Guid.NewGuid(),
                RoleId     = roleId,
                UserId     = userId,
                Status     = 1,
                CreateDate = DateTime.Now,
                UpdateDate = DateTime.Now
            });
            await _context.SaveChangesAsync();
            return (true, "Đã thêm tài khoản vào role.");
        }

        public async Task<(bool ok, string msg)> RemoveAccountFromRole(Guid roleId, Guid userId)
        {
            var ur = await _context.UserRoles
                .FirstOrDefaultAsync(x => x.RoleId == roleId && x.UserId == userId);
            if (ur == null) return (false, "Không tìm thấy liên kết.");

            _context.UserRoles.Remove(ur);
            await _context.SaveChangesAsync();
            return (true, "Đã xóa tài khoản khỏi role.");
        }

        // =====================================================================
        //  PAGE MANAGEMENT (Quản lý trang truy cập)
        // =====================================================================

        public class Page_ListItem
        {
            public Guid   Id             { get; set; }
            public string Code           { get; set; }
            public string Name           { get; set; }
            public string ControllerName { get; set; }
            public string Path           { get; set; }
            public Guid?  ParentId       { get; set; }
            public string ParentName     { get; set; }
            public int    SortOrder      { get; set; }
            public int    Status         { get; set; }
            public int    RoleCount      { get; set; }  // số role được cấp trang này
        }

        public class Page_UpsertRequest
        {
            public Guid?   Id             { get; set; }
            public string  Code           { get; set; }
            public string  Name           { get; set; }
            public string  ControllerName { get; set; }
            public string  Path           { get; set; }
            public Guid?   ParentId       { get; set; }
            public int     SortOrder      { get; set; }
            public int     Status         { get; set; } = 1;
        }

        public class Permission_ListItem
        {
            public Guid   Id     { get; set; }
            public string Code   { get; set; }
            public string Name   { get; set; }
            public int    Status { get; set; }
        }

        public class Permission_UpsertRequest
        {
            public Guid?  Id      { get; set; }
            public string Code    { get; set; }
            public string Name    { get; set; }
            public int    Status  { get; set; } = 1;
        }

        /// <summary>Lấy danh sách trang truy cập (Status >= 0), nhóm theo ParentId.</summary>
        public async Task<List<Page_ListItem>> GetPageList(string keyword = null)
        {
            var query = _context.Pages.AsQueryable()
                .Where(p => p.Status >= 0);

            if (!string.IsNullOrWhiteSpace(keyword))
                query = query.Where(p => p.Name.Contains(keyword) || p.Code.Contains(keyword) || p.Path.Contains(keyword));

            var pages = await query
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
                .AsNoTracking()
                .ToListAsync();

            // Parent name map
            var parentIds = pages.Where(p => p.ParentId.HasValue).Select(p => p.ParentId!.Value).Distinct().ToList();
            var parentMap = await _context.Pages
                .Where(p => parentIds.Contains(p.Id))
                .AsNoTracking()
                .ToDictionaryAsync(p => p.Id, p => p.Name);

            // Số role được cấp mỗi trang
            var roleCounts = await _context.RolePagePermissions
                .Where(rp => rp.Status == 1)
                .GroupBy(rp => rp.PageId)
                .Select(g => new { PageId = g.Key, Count = g.Select(x => x.RoleId).Distinct().Count() })
                .ToDictionaryAsync(x => x.PageId, x => x.Count);

            return pages.Select(p => new Page_ListItem
            {
                Id             = p.Id,
                Code           = p.Code,
                Name           = p.Name,
                ControllerName = p.ControllerName,
                Path           = p.Path,
                ParentId       = p.ParentId,
                ParentName     = p.ParentId.HasValue && parentMap.TryGetValue(p.ParentId.Value, out var pn) ? pn : null,
                SortOrder      = p.SortOrder,
                Status         = p.Status,
                RoleCount      = roleCounts.TryGetValue(p.Id, out var rc) ? rc : 0
            }).ToList();
        }

        /// <summary>Tạo mới / cập nhật Page.</summary>
        public async Task<(bool ok, string msg, Guid? id)> UpsertPage(Page_UpsertRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return (false, "Tên trang không được để trống.", null);

            Page page;
            bool isNew = false;

            if (req.Id.HasValue && req.Id.Value != Guid.Empty)
            {
                page = await _context.Pages.FindAsync(req.Id.Value);
                if (page == null) return (false, "Không tìm thấy trang.", null);
                page.UpdateDate = DateTime.Now;
            }
            else
            {
                isNew = true;
                page = new Page { Id = Guid.NewGuid(), CreateDate = DateTime.Now, UpdateDate = DateTime.Now };
                _context.Pages.Add(page);
            }

            page.Code           = req.Code?.Trim();
            page.Name           = req.Name.Trim();
            page.ControllerName = req.ControllerName?.Trim();
            page.Path           = req.Path?.Trim();
            page.ParentId       = req.ParentId;
            page.SortOrder      = req.SortOrder;
            page.Status         = req.Status;

            await _context.SaveChangesAsync();
            return (true, isNew ? "Đã tạo trang thành công." : "Đã cập nhật trang.", page.Id);
        }

        /// <summary>Cập nhật trạng thái nhiều Page.</summary>
        public async Task<(bool ok, string msg)> SetPageStatus(List<Guid> ids, int status)
        {
            var pages = await _context.Pages.Where(p => ids.Contains(p.Id)).ToListAsync();
            if (!pages.Any()) return (false, "Không tìm thấy trang.");
            foreach (var p in pages) { p.Status = status; p.UpdateDate = DateTime.Now; }
            await _context.SaveChangesAsync();
            return (true, $"Đã cập nhật {pages.Count} trang.");
        }

        /// <summary>Lấy danh sách Permission.</summary>
        public async Task<List<Permission_ListItem>> GetPermissionList()
        {
            return await _context.Permissions
                .Where(p => p.Status >= 0)
                .OrderBy(p => p.Name)
                .AsNoTracking()
                .Select(p => new Permission_ListItem { Id = p.Id, Code = p.Code, Name = p.Name, Status = p.Status })
                .ToListAsync();
        }

        /// <summary>Tạo mới / cập nhật Permission.</summary>
        public async Task<(bool ok, string msg, Guid? id)> UpsertPermission(Permission_UpsertRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return (false, "Tên quyền không được để trống.", null);

            Permission perm;
            bool isNew = false;

            if (req.Id.HasValue && req.Id.Value != Guid.Empty)
            {
                perm = await _context.Permissions.FindAsync(req.Id.Value);
                if (perm == null) return (false, "Không tìm thấy quyền.", null);
                perm.UpdateDate = DateTime.Now;
            }
            else
            {
                isNew = true;
                perm = new Permission { Id = Guid.NewGuid(), CreateDate = DateTime.Now, UpdateDate = DateTime.Now };
                _context.Permissions.Add(perm);
            }

            perm.Code   = req.Code?.Trim();
            perm.Name   = req.Name.Trim();
            perm.Status = req.Status;

            await _context.SaveChangesAsync();
            return (true, isNew ? "Đã tạo quyền thành công." : "Đã cập nhật quyền.", perm.Id);
        }
    }
}
