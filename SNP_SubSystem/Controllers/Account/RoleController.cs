using Microsoft.AspNetCore.Mvc;
using Sub_Services.Execute;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SNP_SubSystem.Controllers.Account
{
    public class RoleController : Controller
    {
        private readonly SubSystemService _service;

        public RoleController(SubSystemService service)
        {
            _service = service;
        }

        // ---------------------------------------------------------------
        // GET: Trang quản lý role (2 cột: Role list + Accounts)
        // GET /Role/Index
        // ---------------------------------------------------------------
        public IActionResult Index()
        {
            return View("~/Views/Account/Role/index.cshtml");
        }

        // ---------------------------------------------------------------
        // GET: Trang thêm / chỉnh sửa Role + phân quyền trang
        // GET /Role/Setting?id={guid}   (id = null → tạo mới)
        // ---------------------------------------------------------------
        public IActionResult Setting(Guid? id = null)
        {
            ViewData["RoleId"] = id?.ToString() ?? "";
            return View("~/Views/Account/Role/Partial/_RoleSetting.cshtml");
        }

        // ---------------------------------------------------------------
        // GET: Danh sách Role
        // GET /Role/GetList?keyword=
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetList(string keyword = null)
        {
            var list = await _service.GetRoleList(keyword);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: Chi tiết Role (kèm trang + quyền hiện tại)
        // GET /Role/GetDetail?id={guid}
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetDetail(Guid id)
        {
            var detail = await _service.GetRoleDetail(id);
            if (detail == null)
                return NotFound(new { success = false, message = "Không tìm thấy role." });
            return Ok(detail);
        }

        // ---------------------------------------------------------------
        // GET: Toàn bộ Pages + Permissions (dùng khi tạo role mới)
        // GET /Role/GetAllPages
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetAllPages()
        {
            var list = await _service.GetAllPagesWithPermissions();
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: Accounts thuộc Role
        // GET /Role/GetAccounts?roleId={guid}
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetAccounts(Guid roleId)
        {
            var list = await _service.GetAccountsByRole(roleId);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: Tìm kiếm User để thêm vào Role
        // GET /Role/SearchUsers?keyword=
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> SearchUsers(string keyword = null)
        {
            var req = new SubSystemService.Account_QueryRequest
            {
                Keyword  = keyword,
                Status   = 1,
                Page     = 1,
                PageSize = 30
            };
            var result = await _service.GetAccountList(req);
            return Ok(result.Items);
        }

        // ---------------------------------------------------------------
        // POST: Tạo mới / cập nhật Role
        // POST /Role/Upsert
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> Upsert([FromBody] SubSystemService.Role_UpsertRequest req)
        {
            if (req == null)
                return BadRequest(new { success = false, message = "Request không hợp lệ." });

            var (ok, msg, id) = await _service.UpsertRole(req);
            return ok
                ? Ok(new { success = true, message = msg, id })
                : BadRequest(new { success = false, message = msg });
        }

        // ---------------------------------------------------------------
        // POST: Cập nhật trạng thái Role
        // POST /Role/SetStatus
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> SetStatus([FromBody] SetStatusRequest req)
        {
            if (req == null || req.Ids == null || req.Ids.Count == 0)
                return BadRequest(new { success = false, message = "Danh sách ID trống." });

            var (ok, msg) = await _service.SetRoleStatus(req.Ids, req.TargetStatus);
            return ok
                ? Ok(new { success = true, message = msg })
                : BadRequest(new { success = false, message = msg });
        }

        // ---------------------------------------------------------------
        // POST: Gán User vào Role
        // POST /Role/AssignUser
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> AssignUser([FromBody] AssignUserRequest req)
        {
            if (req == null)
                return BadRequest(new { success = false, message = "Request không hợp lệ." });

            var (ok, msg) = await _service.AssignAccountToRole(req.RoleId, req.UserId);
            return ok
                ? Ok(new { success = true, message = msg })
                : BadRequest(new { success = false, message = msg });
        }

        // ---------------------------------------------------------------
        // POST: Xóa User khỏi Role
        // POST /Role/RemoveUser
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> RemoveUser([FromBody] AssignUserRequest req)
        {
            if (req == null)
                return BadRequest(new { success = false, message = "Request không hợp lệ." });

            var (ok, msg) = await _service.RemoveAccountFromRole(req.RoleId, req.UserId);
            return ok
                ? Ok(new { success = true, message = msg })
                : BadRequest(new { success = false, message = msg });
        }

        // ---------------------------------------------------------------
        // GET: Danh sách Pages
        // GET /Role/GetPages?keyword=
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetPages(string keyword = null)
        {
            var list = await _service.GetPageList(keyword);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // POST: Tạo mới / cập nhật Page
        // POST /Role/UpsertPage
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> UpsertPage([FromBody] SubSystemService.Page_UpsertRequest req)
        {
            if (req == null) return BadRequest(new { success = false, message = "Request không hợp lệ." });
            var (ok, msg, id) = await _service.UpsertPage(req);
            return ok ? Ok(new { success = true, message = msg, id }) : BadRequest(new { success = false, message = msg });
        }

        // ---------------------------------------------------------------
        // POST: Cập nhật trạng thái Page
        // POST /Role/SetPageStatus
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> SetPageStatus([FromBody] SetStatusRequest req)
        {
            if (req == null || req.Ids == null || !req.Ids.Any())
                return BadRequest(new { success = false, message = "Danh sách ID trống." });
            var (ok, msg) = await _service.SetPageStatus(req.Ids, req.TargetStatus);
            return ok ? Ok(new { success = true, message = msg }) : BadRequest(new { success = false, message = msg });
        }

        // ---------------------------------------------------------------
        // GET: Danh sách Permissions
        // GET /Role/GetPermissions
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetPermissions()
        {
            var list = await _service.GetPermissionList();
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // POST: Tạo mới / cập nhật Permission
        // POST /Role/UpsertPermission
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> UpsertPermission([FromBody] SubSystemService.Permission_UpsertRequest req)
        {
            if (req == null) return BadRequest(new { success = false, message = "Request không hợp lệ." });
            var (ok, msg, id) = await _service.UpsertPermission(req);
            return ok ? Ok(new { success = true, message = msg, id }) : BadRequest(new { success = false, message = msg });
        }

        // ── DTOs ────────────────────────────────────────────────────────
        public class SetStatusRequest
        {
            public List<Guid> Ids          { get; set; }
            public int        TargetStatus { get; set; }
        }

        public class AssignUserRequest
        {
            public Guid RoleId { get; set; }
            public Guid UserId { get; set; }
        }
    }
}
