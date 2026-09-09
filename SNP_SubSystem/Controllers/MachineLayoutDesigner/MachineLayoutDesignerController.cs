using Microsoft.AspNetCore.Mvc;

namespace SNP_SubSystem.Controllers.MachineLayoutDesigner
{
    public class MachineLayoutDesignerController : Controller
    {
        public IActionResult Index()
        {
            return View("~/Views/MachineLayoutDesigner/Index.cshtml");
        }
    }
}
