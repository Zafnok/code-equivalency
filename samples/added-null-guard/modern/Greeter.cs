namespace Equiv.Samples.AddedNullGuard;

public class Greeter
{
    public string Greet(string name)
    {
        return "Hello, " + (name ?? string.Empty).ToUpper();
    }

    public string Shout(string name)
    {
        return "HELLO, " + name.ToUpper();
    }
}
