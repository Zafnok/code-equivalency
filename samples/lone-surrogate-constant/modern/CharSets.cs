namespace Equiv.Samples.LoneSurrogateConstant
{
    public static class CharSets
    {
        public static int Find(string s) => s.IndexOfAny("\uD800\uDBFF".ToCharArray());
    }
}
