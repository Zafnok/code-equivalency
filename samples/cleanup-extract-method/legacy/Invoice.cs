namespace Equiv.Samples.CleanupExtractMethod
{
    public class Invoice
    {
        public int Total(int quantity, int unitPrice)
        {
            int subtotal = quantity * unitPrice;
            int discount = 0;
            if (subtotal > 100)
            {
                discount = subtotal / 10;
            }

            return subtotal - discount;
        }

        public int Shipping(int weight)
        {
            int billable = Billable(weight);
            return 5 + billable * 2;
        }

        private int Billable(int weight)
        {
            if (weight < 0)
            {
                return 0;
            }

            return weight;
        }
    }
}
