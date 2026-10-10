using System;
using System.Linq;

namespace Equiv.Samples.GenericEnumMembers;

public static class Days
{
    public static bool Known(DayOfWeek d)
    {
        return Enum.IsDefined(typeof(DayOfWeek), d);
    }

    public static int Count()
    {
        int n = 0;
        foreach (DayOfWeek d in Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>())
        {
            n++;
        }

        return n;
    }
}
