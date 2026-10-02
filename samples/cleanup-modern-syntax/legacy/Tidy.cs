using System.Collections.Generic;

namespace Equiv.Samples.CleanupModernSyntax
{
    public class Tidy
    {
        public int Grade(int score)
        {
            if (score >= 90)
            {
                return 4;
            }
            else if (score >= 80)
            {
                return 3;
            }
            else if (score >= 70)
            {
                return 2;
            }
            else
            {
                return 0;
            }
        }

        public string Join(string a, string b)
        {
            return string.Format("{0}-{1}", a, b);
        }

        public int? LengthOf(string x)
        {
            return x == null ? (int?)null : x.Length;
        }

        public string OrDefault(string x, string d)
        {
            if (x == null)
            {
                x = d;
            }

            return x;
        }

        public int Measure(object o)
        {
            if (o is Circle)
            {
                var c = (Circle)o;
                return c.Radius;
            }

            return 0;
        }

        public List<int> Positives(List<int> values)
        {
            if (values == null)
            {
                return new List<int>();
            }

            var result = new List<int>();
            foreach (var value in values)
            {
                if (value > 0)
                {
                    result.Add(value);
                }
            }

            return result;
        }
    }
}
