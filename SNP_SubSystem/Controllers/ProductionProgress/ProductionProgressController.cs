using Microsoft.AspNetCore.Mvc;
using Sub_Entities.Entities;
using Sub_Services.Execute;

namespace SNP_SubSystem.Controllers.ProductionProgress
{
    public class ProductionProgressController : Controller
    {
        private readonly SubSystemService _service;

        public ProductionProgressController(SubSystemService service)
        {
            _service = service;
        }

        // =====================================================================
        //  GET: /ProductionProgress/ATProgress
        //  Tra ve View chinh cua trang Tien do san xuat.
        // =====================================================================
        public IActionResult ATProgress()
        {
            return View("~/Views/ProductionProgress/Index.cshtml");
        }

        // =====================================================================
        //  GET: /ProductionProgress/GetDepartments
        //  Tra ve danh sach phong ban dang hoat dong (Status == 1).
        //  Dung de do vao dropdown loc Phong ban tren giao dien.
        // =====================================================================

        /// <summary>
        /// Tra ve JSON array chua danh sach phong ban:
        /// [ { "id": "GUID", "departmentName": "Ten phong ban" }, ... ]
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetDepartments()
        {
            var list = await _service.GetDepartmentList();
            return Ok(list);
        }

        // =====================================================================
        //  GET: /ProductionProgress/GetLines?departmentId=GUID (optional)
        //  Tra ve danh sach Line distinct tu ProductionInfo.
        //  Co the loc theo phong ban neu truyen departmentId.
        // =====================================================================

        /// <summary>
        /// Tra ve JSON array chua danh sach gia tri Line duy nhat:
        /// [ "01", "02", "03", ... ]
        ///
        /// Query param (tuy chon):
        ///   departmentId (Guid) — neu truyen, chi lay Line cua phong ban do.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetLines(Guid? departmentId = null)
        {
            var list = await _service.GetLineList(departmentId);
            return Ok(list);
        }

        // =====================================================================
        //  POST: /ProductionProgress/GetList
        //  Nhan request JSON tu client, thuc hien truy van co loc + phan trang,
        //  tra ve JSON chua PagedResult<ProductionProgress_OutputInfo>.
        // =====================================================================

        /// <summary>
        /// Endpoint API nhan yeu cau truy van danh sach tien do san xuat.
        ///
        /// Request body (JSON):
        /// {
        ///   "fromDate"              : "2026-08-01",
        ///   "toDate"                : "2026-08-30",
        ///   "keyword"               : "polo",
        ///   "line"                  : "01",
        ///   "productionDepartmentId": "GUID...",
        ///   "status"                : 1,
        ///   "page"                  : 1,
        ///   "pageSize"              : 20
        /// }
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetList(
            [FromBody] SubSystemService.ProductionProgress_QueryRequest request)
        {
            if (request == null)
                return BadRequest(new { message = "Request body khong hop le." });

            if (request.FromDate > request.ToDate)
                return BadRequest(new { message = "FromDate khong duoc lon hon ToDate." });

            var result = await _service.GetProductionProgressList(request);
            return Ok(result);
        }
    }
}
