namespace Equiv.Samples.RuntimeRowIndexerGetter
{
    public static class Nodes
    {
        public static string First(System.Windows.Forms.TreeView t)
        {
            t.Nodes[0] = null;
            return t.Nodes[0].Text;
        }
    }
}
