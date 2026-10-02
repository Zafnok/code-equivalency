namespace Equiv.Samples.CleanupExtractMethod;

public class Invoice
{
    public int Total(int quantity, int unitPrice)
    {
        int subtotal = quantity * unitPrice;
        return subtotal - Discount(subtotal);
    }

    public int Shipping(int weight)
    {
        int billable = weight;
        if (billable < 0)
        {
            billable = 0;
        }

        return 5 + billable * 2;
    }

    private int Discount(int subtotal)
    {
        if (subtotal > 100)
        {
            return subtotal / 10;
        }

        return 0;
    }
}
