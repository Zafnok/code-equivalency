namespace Equiv.Samples.SwitchExpressionNoMatch
{
    public static class Codes
    {
        public static int Weight(int code) => code switch { 1 => 10, 2 => 20 };

        public static int WeightOrZero(int code) => code switch { 1 => 10, 2 => 20, _ => 0 };

        public static int Sign(bool positive) => positive switch { true => 1, false => -1 };
    }
}
