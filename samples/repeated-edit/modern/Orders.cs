namespace Equiv.Samples.RepeatedEdit
{
    public static class Orders
    {
        public static int Quantity(string text) => int.Parse(text);

        public static int Total(string quantity, int price) => int.Parse(quantity) * price;

        public static bool IsBulk(string quantity) => int.Parse(quantity) >= 100;

        public static int Discount(int total) => total >= 1000 ? total / 10 : 0;
    }
}
