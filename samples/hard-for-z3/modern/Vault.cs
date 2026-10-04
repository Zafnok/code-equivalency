namespace Equiv.Samples.HardForZ3;

public class Vault
{
    public bool Opens(uint left, uint right)
    {
        return left > 1 && right > 1 && (ulong)left * right == 4503597865762987UL;
    }
}
