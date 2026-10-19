using System;

namespace Equiv.Samples.PrivateFieldReceiver
{
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
            if (owner == null)
            {
                throw new InvalidOperationException("no owner");
            }

            return owner.ToUpper() + ": " + balance;
        }
    }
}
