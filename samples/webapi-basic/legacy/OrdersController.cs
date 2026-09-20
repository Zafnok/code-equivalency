using System.Web.Http;

namespace Equiv.Samples.WebApiBasic
{
    [RoutePrefix("api/orders")]
    public class OrdersController : ApiController
    {
        [HttpGet, Route("{id:int}")]
        public int Get(int id) => id;
    }
}
