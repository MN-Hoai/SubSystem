using Microsoft.AspNetCore.Mvc;
using Sub_Services.Execute;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SNP_SubSystem.Controllers.Account
{
    public class AccountController : Controller
    {
        private readonly SubSystemService _service;

        public AccountController(SubSystemService service)
        {
            _service = service;
        }

        // ---------------------------------------------------------------
        // GET: Trang quản lý tài khoản
        // GET /Account/Index
        // ---------------------------------------------------------------
        public IActionResult Index()
        {
            return View("~/Views/Account/index.cshtml");
        }

        // ---------------------------------------------------------------
        // POST: Lấy danh sách tài khoản (phân trang, tìm kiếm)
        // POST /Account/GetList
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> GetList([FromBody] SubSystemService.Account_QueryRequest req)
        {
            if (req == null)
                return BadRequest(new { success = false, message = "Request không hợp lệ." });

            var result = await _service.GetAccountList(req);
            return Ok(new { success = true, data = result });
        }

        // ---------------------------------------------------------------
        // GET: Lấy chi tiết một tài khoản
        // GET /Account/GetById?id={guid}
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetById(Guid id)
        {
            var item = await _service.GetAccountById(id);
            if (item == null)
                return NotFound(new { success = false, message = "Không tìm thấy tài khoản." });
            return Ok(new { success = true, data = item });
        }

        // ---------------------------------------------------------------
        // POST: Tạo mới / cập nhật tài khoản
        // POST /Account/Upsert
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> Upsert([FromBody] SubSystemService.Account_UpsertRequest req)
        {
            if (req == null)
                return BadRequest(new { success = false, message = "Request không hợp lệ." });

            var (ok, msg, newId) = await _service.UpsertAccount(req);
            return ok
                ? Ok(new { success = true, message = msg, id = newId })
                : BadRequest(new { success = false, message = msg });
        }

        // ---------------------------------------------------------------
        // POST: Cập nhật trạng thái hàng loạt (khoá/mở khoá/xoá mềm)
        // POST /Account/SetStatus
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> SetStatus([FromBody] SetStatusRequest req)
        {
            if (req == null || req.Ids == null || req.Ids.Count == 0)
                return BadRequest(new { success = false, message = "Danh sách ID trống." });

            var (ok, msg) = await _service.SetAccountStatus(req.Ids, req.TargetStatus);
            return ok
                ? Ok(new { success = true, message = msg })
                : BadRequest(new { success = false, message = msg });
        }

        // ── Helper DTOs ─────────────────────────────────────────────────
        public class SetStatusRequest
        {
            public List<Guid> Ids          { get; set; }
            public int        TargetStatus { get; set; }
        }
    }
}
