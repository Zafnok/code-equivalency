namespace Equiv.Samples.AddedNullGuard
{
    public class Greeter
    {
        public string Greet(string name)
        {
            return "Hello, " + name.ToUpper();
        }

        public string Shout(string name)
        {
            if (name == null)
            {
                return "HELLO";
            }

            return "HELLO, " + name.ToUpper();
        }
    }
}
