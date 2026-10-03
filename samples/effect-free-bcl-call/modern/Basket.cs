using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Equiv.Samples.EffectFreeBclCall
{
    public class Basket
    {
        private readonly IList<int> items;

        public Basket()
        {
            this.items = new Collection<int>();
        }

        public IList<int> Items
        {
            get { return this.items; }
        }
    }
}
