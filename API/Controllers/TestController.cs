using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    public class TestController : BaseController
    {
        [HttpGet]
        public string Test()
        {
            return "succeed get";
        }
    }
}