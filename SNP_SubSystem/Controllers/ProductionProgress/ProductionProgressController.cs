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

        public IActionResult ATProgress()
        {
            return View("~/Views/ProductionProgress/Index.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> GetDepartments()
        {
            var list = await _service.GetDepartmentList();
            return Ok(list);
        }

        [HttpGet]
        public async Task<IActionResult> GetLines(Guid? departmentId = null)
        {
            var list = await _service.GetLineList(departmentId);
            return Ok(list);
        }

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
