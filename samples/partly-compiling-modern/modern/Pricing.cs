using Equiv.Samples.PartlyCompilingModern.Settings;

namespace Equiv.Samples.PartlyCompilingModern
{
    public static class Pricing
    {
        public static int Net(int gross)
        {
            return gross - Discount(gross);
        }

        public static int Discount(int gross) => gross > 100 ? 10 : 0;

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
