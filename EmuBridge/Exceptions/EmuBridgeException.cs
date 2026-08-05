namespace EmuBridge.Exceptions;

public class EmuBridgeException : Exception
{
    public EmuBridgeException(string message) : base(message) { }
    public EmuBridgeException(string message, Exception inner) : base(message, inner) { }
}
