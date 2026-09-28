namespace Equiv.Samples.CalleeChangedInvisible
{
    public static class Grading
    {
        public static string Classify(int a) => Score(a) > 0 ? "pos" : "neg";

        public static int Score(int a) => a > 10 ? a - 1 : a;
    }
}
