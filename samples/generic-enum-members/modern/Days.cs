using System;
using System.Linq;

namespace Equiv.Samples.GenericEnumMembers;

public static class Days
{
    public static bool Known(DayOfWeek d)
    {
        return Enum.IsDefined(d);
    }

    public static int Count()
    {
        int n = 0;
        foreach (DayOfWeek d in Enum.GetValues<DayOfWeek>())
        {
            n++;
        }

        return n;
    }
}
