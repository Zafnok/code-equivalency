using System.Collections.Generic;

namespace Equiv.Samples.EffectFreeBclCall
{
    public class Basket
    {
        private readonly IList<int> items;

        public Basket()
        {
            this.items = new List<int>();
        }

        public IList<int> Items
        {
            get { return this.items; }
        }
    }
}
