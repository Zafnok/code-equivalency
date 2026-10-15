using System.Collections.Concurrent;
using System.Threading;

namespace Equiv.Samples.CalleeReplacement
{
    public static class Slots
    {
        private static readonly ConcurrentDictionary<string, AsyncLocal<object>> State = new ConcurrentDictionary<string, AsyncLocal<object>>();

        public static void SetData(string name, object data) => State.GetOrAdd(name, _ => new AsyncLocal<object>()).Value = data;

        public static object GetData(string name) => State.TryGetValue(name, out AsyncLocal<object> data) ? data.Value : null;
    }

    public static class Culture
    {
        public const string Key = "Equiv.Samples.Culture";

        public static string Current() => Slots.GetData(Key) as string ?? "invariant";

        public static void Enter(string name) => Slots.SetData(Key, name);

        public static void Leave() => Slots.SetData(Key, string.Empty);
    }
}
