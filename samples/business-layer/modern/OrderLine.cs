namespace Equiv.Samples.BusinessLayer
{
    public class OrderLine
    {
        public string Sku { get; set; }

        public int Quantity { get; set; }

        public decimal UnitPrice { get; set; }

        public decimal Discount { get; set; }
    }
}
