namespace EnergyBalance.Acquisition.Protocols.Mercury;

public class MercuryProtocolException : Exception
{
    public MercuryProtocolException(string message) : base(message)
    {
    }

    public MercuryProtocolException(string message, Exception inner) : base(message, inner)
    {
    }
}
