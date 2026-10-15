using System.Runtime.Remoting.Messaging;

namespace Equiv.Samples.CalleeReplacement
{
    public static class Culture
    {
        public const string Key = "Equiv.Samples.Culture";

        public static string Current() => CallContext.LogicalGetData(Key) as string ?? "invariant";

        public static void Enter(string name) => CallContext.LogicalSetData(Key, name);

        public static void Leave() => CallContext.LogicalSetData(Key, null);
    }
}
