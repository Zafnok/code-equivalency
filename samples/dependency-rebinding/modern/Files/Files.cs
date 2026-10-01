namespace Equiv.Samples.DependencyRebinding.Files
{
    public interface IFile
    {
        bool Exists(string p);

        bool Delete(string p);
    }

    public interface IFs
    {
        IFile File { get; }
    }

    public sealed class NoFiles : IFile
    {
        public bool Exists(string p) => false;

        public bool Delete(string p) => false;
    }
}
