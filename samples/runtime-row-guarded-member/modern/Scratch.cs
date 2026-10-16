namespace Equiv.Samples.RuntimeRowGuardedMember
{
    public sealed class Scratch
    {
        private readonly string log;
        private string path;
        private bool keep;

        public Scratch(string log, string path)
        {
            this.log = log;
            this.path = path;
        }

        public void TearDown()
        {
            if (System.IO.File.Exists(this.log))
            {
                System.IO.File.Delete(this.log);
            }
        }

        public void Dispose()
        {
            if (!keep && path != null && System.IO.File.Exists(path))
                System.IO.File.Delete(path);
            path = null;
        }

        public void Move(string next)
        {
            if (System.IO.File.Exists(path))
            {
                Retarget(next);
                System.IO.File.Delete(path);
            }
        }

        public void Retarget(string next)
        {
            path = next;
            keep = false;
        }
    }
}
