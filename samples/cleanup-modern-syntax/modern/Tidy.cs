using System.Collections.Generic;
using System.Linq;

namespace Equiv.Samples.CleanupModernSyntax;

public class Tidy
{
    public int Grade(int score)
    {
        return score switch
        {
            >= 90 => 4,
            >= 80 => 3,
            >= 70 => 2,
            _ => 0,
        };
    }

    public string Join(string a, string b)
    {
        return $"{a}-{b}";
    }

    public int? LengthOf(string x)
    {
        return x?.Length;
    }

    public string OrDefault(string x, string d)
    {
        x ??= d;

        return x;
    }

    public int Measure(object o)
    {
        if (o is Circle c)
        {
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

        return values.Where(value => value > 0).ToList();
    }
}
