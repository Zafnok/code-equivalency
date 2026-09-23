using System.Collections.Generic;

namespace Equiv.Samples.BusinessLayer
{
    public class Order
    {
        public int Id { get; set; }

        public string CustomerName { get; set; }

        public List<OrderLine> Lines { get; set; }
    }
}
