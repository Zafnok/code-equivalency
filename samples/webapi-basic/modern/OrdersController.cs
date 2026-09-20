using Microsoft.AspNetCore.Mvc;

namespace Equiv.Samples.WebApiBasic
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        [HttpGet("{id}")]
        public int Get(int id) => id;
    }
}
