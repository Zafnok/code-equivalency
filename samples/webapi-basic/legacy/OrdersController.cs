using System.Web.Http;

namespace Equiv.Samples.WebApiBasic
{
    [RoutePrefix("api/orders")]
    public class OrdersController : ApiController
    {
        [HttpGet, Route("{id:int}")]
        public int Get(int id) => id;

        [HttpGet, Route("find/{id:int}")]
        public IHttpActionResult Find(int id)
        {
            if (id < 0) return NotFound();
            return Ok(id);
        }
    }
}
