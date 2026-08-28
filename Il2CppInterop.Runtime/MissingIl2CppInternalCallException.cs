using System;

namespace Il2CppInterop.Runtime;

/// <summary>Reports an internal call that is absent from the current Unity player.</summary>
public sealed class MissingIl2CppInternalCallException : Exception
{
    /// <summary>Initializes an exception for the unresolved internal-call signature.</summary>
    public MissingIl2CppInternalCallException(string signature)
        : base($"ICall with signature {signature} was not resolved")
    {
        Signature = signature;
    }

    /// <summary>Gets the Unity internal-call signature that could not be resolved.</summary>
    public string Signature { get; }
}
