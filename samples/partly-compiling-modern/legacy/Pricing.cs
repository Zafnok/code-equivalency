using Equiv.Samples.PartlyCompilingModern.Settings;

namespace Equiv.Samples.PartlyCompilingModern
{
    public static class Pricing
    {
        public static int Net(int gross)
        {
            return gross - Discount(gross);
        }

        public static int Discount(int gross)
        {
            if (gross > 100)
            {
                return 10;
            }

            return 0;
        }

        public static string Currency()
        {
            return Regional.Currency();
        }

        public static bool HasCurrency()
        {
            return Currency() != null;
        }
    }
}
