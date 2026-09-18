using System;

namespace Equiv.Samples.RemovedNullCheck
{
    public class Greeter
    {
        public string Greet(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException("name");
            }

            return "Hello, " + name.ToUpper();
        }
    }
}
