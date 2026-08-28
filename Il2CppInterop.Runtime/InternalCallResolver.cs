using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Il2CppInterop.Common;
using Microsoft.Extensions.Logging;

namespace Il2CppInterop.Runtime;

internal static class InternalCallResolver
{
    private static readonly ConcurrentDictionary<string, byte> MissingSignatures =
        new(StringComparer.Ordinal);

    internal static bool TryResolve<T>(
        string signature,
        Func<string, IntPtr> resolver,
        out T? icall) where T : Delegate
    {
        var pointer = resolver(signature);
        if (pointer == IntPtr.Zero)
        {
            icall = null;
            return false;
        }

        icall = Marshal.GetDelegateForFunctionPointer<T>(pointer);
        return true;
    }

    internal static T Resolve<T>(string signature, Func<string, IntPtr> resolver) where T : Delegate
    {
        if (TryResolve(signature, resolver, out T? icall))
            return icall;

        if (MissingSignatures.TryAdd(signature, 0))
            Logger.Instance.LogTrace("ICall {Signature} not resolved", signature);
        return GenerateMissingDelegate<T>(signature);
    }

    private static T GenerateMissingDelegate<T>(string signature) where T : Delegate
    {
        var invoke = typeof(T).GetMethod("Invoke")!;
        var trampoline = new DynamicMethod(
            "(missing icall delegate) " + typeof(T).FullName,
            invoke.ReturnType,
            invoke.GetParameters().Select(parameter => parameter.ParameterType).ToArray(),
            typeof(IL2CPP),
            true);
        var body = trampoline.GetILGenerator();

        body.Emit(OpCodes.Ldstr, signature);
        body.Emit(
            OpCodes.Newobj,
            typeof(MissingIl2CppInternalCallException).GetConstructor(new[] { typeof(string) })!);
        body.Emit(OpCodes.Throw);

        return (T)trampoline.CreateDelegate(typeof(T));
    }
}
