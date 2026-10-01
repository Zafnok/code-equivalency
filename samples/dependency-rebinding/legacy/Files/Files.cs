namespace Equiv.Samples.DependencyRebinding.Files
{
    public abstract class FileBase
    {
        public abstract bool Exists(string p);

        public abstract bool Delete(string p);
    }

    public interface IFs
    {
        FileBase File { get; }
    }

    public sealed class NoFiles : FileBase
    {
        public override bool Exists(string p) => false;

        public override bool Delete(string p) => false;
    }
}
