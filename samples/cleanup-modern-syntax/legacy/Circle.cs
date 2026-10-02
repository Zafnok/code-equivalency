namespace Equiv.Samples.CleanupModernSyntax
{
    public class Circle
    {
        public int Radius;

        public int Diameter
        {
            get
            {
                return this.Radius * 2;
            }
        }
    }
}
