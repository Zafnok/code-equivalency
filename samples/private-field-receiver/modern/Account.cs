namespace Equiv.Samples.PrivateFieldReceiver;

public class Account
{
    private readonly string owner;
    private int balance;

    public Account(string owner, int balance)
    {
        this.owner = owner;
        this.balance = balance;
    }

    private string Label()
    {
        return owner.ToUpper() + ": " + balance;
    }
}
