using System;

namespace Equiv.Samples.RepeatedEdit
{
    public static class Orders
    {
        public static int Quantity(string text) => Convert.ToInt32(text);

        public static int Total(string quantity, int price) => Convert.ToInt32(quantity) * price;

        public static bool IsBulk(string quantity) => Convert.ToInt32(quantity) >= 100;

        public static int Discount(int total) => total > 1000 ? total / 10 : 0;
    }
}
