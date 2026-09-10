using Microsoft.AspNetCore.Mvc;
using Sub_Services.Execute;
using System;
using System.Threading.Tasks;

namespace SNP_SubSystem.Controllers.ProductionDepartment
{
    public class ProductionDepartmentController : Controller
    {
        private readonly SubSystemService _service;

        public ProductionDepartmentController(SubSystemService service)
        {
            _service = service;
        }

        public IActionResult ATDepartment()
        {
            return View("~/Views/ProductionDepartment/Index.cshtml");
        }

        // ---------------------------------------------------------------
        // GET: Tất cả layouts (optionally filter by dept)
        // GET /ProductionDepartment/GetLayouts
        // GET /ProductionDepartment/GetLayouts?departmentId={guid}
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetLayouts(Guid? departmentId = null)
        {
            var list = await _service.GetAllLayouts(departmentId);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: Danh sách bộ phận cho dropdown
        // GET /ProductionDepartment/GetDepartments
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetDepartments()
        {
            var list = await _service.GetDepartmentsForLayout();
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: Danh sách mã sản xuất theo máy
        // GET /ProductionDepartment/GetProductionInfos?machineId={guid}&keyword=
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetProductionInfos(Guid machineId, string keyword = null)
        {
            var list = await _service.GetProductionInfosByMachine(machineId, keyword);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: DailyOutput + Details của một mã trên một máy
        // GET /ProductionDepartment/GetDailyOutputs?productionInfoId=&machineId=&from=&to=
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetDailyOutputs(
            Guid productionInfoId, Guid machineId,
            DateTime? from = null, DateTime? to = null)
        {
            var list = await _service.GetDailyOutputs(productionInfoId, machineId, from, to);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: Danh sách Style để pick
        // GET /ProductionDepartment/GetStylesForPicker?deptId=&keyword=
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetStylesForPicker(Guid deptId, string keyword = null)
        {
            var list = await _service.GetStylesForPicker(deptId, keyword);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: Danh sách mã sản xuất theo bộ phận (để chọn có sẵn)
        // GET /ProductionDepartment/GetProductionInfosByDept?deptId=&keyword=
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetProductionInfosByDept(Guid deptId, string keyword = null)
        {
            var list = await _service.GetProductionInfosByDept(deptId, keyword);
            return Ok(list);
        }

        /// <summary>
        /// Trang chi tiết sản xuất của một máy.
        /// </summary>
        public IActionResult MachineDetail(
            string machineId   = "",
            string machineName = "",
            string machineCode = "",
            string dept        = "",
            string deptId      = "")
        {
            ViewData["MachineId"]   = machineId;
            ViewData["MachineName"] = machineName;
            ViewData["MachineCode"] = machineCode;
            ViewData["Dept"]        = dept;
            ViewData["DeptId"]      = deptId;
            return View("~/Views/ProductionDepartment/PartialView/_MachineDetail.cshtml");
        }

        // ---------------------------------------------------------------
        // POST: Gán mã có sẵn vào máy
        // POST /ProductionDepartment/AssignProductionInfo
        // Body: { productionInfoId, machineId }
        // ---------------------------------------------------------------

        [HttpPost]
        public async Task<IActionResult> AssignProductionInfo([FromBody] AssignRequest req)
        {
            if (req == null || req.ProductionInfoId == Guid.Empty || req.MachineId == Guid.Empty)
                return BadRequest(new { success = false, message = "Thiếu dữ liệu." });

            var (ok, msg) = await _service.AssignProductionInfoToMachine(req.ProductionInfoId, req.MachineId);
            if (!ok) return Conflict(new { success = false, message = msg });
            return Ok(new { success = true, message = msg });
        }

        // ---------------------------------------------------------------
        // POST: Tạo mới mã sản xuất rồi gán vào máy
        // POST /ProductionDepartment/CreateProductionInfo
        // ---------------------------------------------------------------

        [HttpPost]
        public async Task<IActionResult> CreateProductionInfo(
            [FromBody] SubSystemService.CreateProductionInfoRequest req)
        {
            if (req == null)
                return BadRequest(new { success = false, message = "Thiếu dữ liệu." });

            var (ok, msg, id) = await _service.CreateAndAssignProductionInfo(req);
            if (!ok) return BadRequest(new { success = false, message = msg });
            return Ok(new { success = true, message = msg, id = id });
        }

        public class AssignRequest
        {
            public Guid ProductionInfoId { get; set; }
            public Guid MachineId        { get; set; }
        }

        // ---------------------------------------------------------------
        // GET: Trang ghi sản lượng
        // GET /ProductionDepartment/RecordOutput
        // ---------------------------------------------------------------

        public IActionResult RecordOutput()
        {
            return View("~/Views/ProductionDepartment/PartialView/_RecordOutput.cshtml");
        }

        // ---------------------------------------------------------------
        // GET: Danh sách máy (cho RecordOutput)
        // GET /ProductionDepartment/GetMachinesForRecording?deptId=&keyword=
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetMachinesForRecording(Guid? deptId = null, string keyword = null)
        {
            var list = await _service.GetMachinesForRecording(deptId, keyword);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: Mã sản xuất theo máy (cho RecordOutput)
        // GET /ProductionDepartment/GetProductionCodesForMachine?machineId=&keyword=&status=
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetProductionCodesForMachine(
            Guid machineId, string keyword = null, int? status = null)
        {
            var list = await _service.GetProductionCodesForMachine(machineId, keyword, status);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: Chi tiết công đoạn (StyleDetail) của mã + tổng đã nhập
        // GET /ProductionDepartment/GetStyleDetailsForRecording?productionInfoId=&machineId=&date=
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetStyleDetailsForRecording(
            Guid productionInfoId, Guid machineId, string date = null)
        {
            DateOnly? d = null;
            if (!string.IsNullOrEmpty(date) && DateOnly.TryParse(date, out var parsed))
                d = parsed;
            var list = await _service.GetStyleDetailsForRecording(productionInfoId, machineId, d);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // POST: Ghi sản lượng (cộng dồn)
        // POST /ProductionDepartment/SaveOutputRecord
        // ---------------------------------------------------------------

        [HttpPost]
        public async Task<IActionResult> SaveOutputRecord(
            [FromBody] SubSystemService.RecordOutput_SaveRequest req)
        {
            if (req == null)
                return BadRequest(new { success = false, message = "Thiếu dữ liệu." });
            var (ok, msg) = await _service.SaveOutputRecord(req);
            if (!ok) return BadRequest(new { success = false, message = msg });
            return Ok(new { success = true, message = msg });
        }
    }
}
