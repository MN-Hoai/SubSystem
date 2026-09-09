using Microsoft.AspNetCore.Mvc;

namespace SNP_SubSystem.Controllers.ProductionDepartment
{
    public class ProductionDepartmentController : Controller
    {
        public IActionResult ATDepartment()
        {
            return View("~/Views/ProductionDepartment/Index.cshtml");
        }


        /// <summary>
        /// Trang chi tiết sản xuất của một máy.
        /// TODO: Nhận machineId, machineName từ query string để load dữ liệu thực.
        /// </summary>
        public IActionResult MachineDetail(
            string machineId   = "",
            string machineName = "",
            string machineCode = "",
            string dept        = "")
        {
            ViewData["MachineId"]   = machineId;
            ViewData["MachineName"] = machineName;
            ViewData["MachineCode"] = machineCode;
            ViewData["Dept"]        = dept;
            return View("~/Views/ProductionDepartment/PartialView/_MachineDetail.cshtml");
        }
    }
}
