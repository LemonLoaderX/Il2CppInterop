using System;
using System.Runtime.CompilerServices;
using Il2CppInterop.Runtime.Startup;

namespace Il2CppInterop.Runtime.Injection;

public interface IDetour : IDisposable
{
    nint Target { get; }
    nint Detour { get; }
    nint OriginalTrampoline { get; }

    void Apply();
    T GenerateTrampoline<T>() where T : Delegate;
}

public interface IDetourProvider
{
    IDetour Create<TDelegate>(nint original, TDelegate target) where TDelegate : Delegate;
}

public static class ValueTypeReturnRegistry
{
    private sealed class ReturnInfo
    {
        internal ReturnInfo(int size) => Size = size;

        internal int Size { get; }
    }

    private static readonly ConditionalWeakTable<Delegate, ReturnInfo> ReturnSizes = new();

    internal static void Register(Delegate target, int returnSize)
    {
        ReturnSizes.Remove(target);
        ReturnSizes.Add(target, new ReturnInfo(returnSize));
    }

    public static int GetReturnSize(Delegate target) =>
        target != null && ReturnSizes.TryGetValue(target, out var info) ? info.Size : 0;
}

internal static class Detour
{
    public static IDetour Apply<T>(nint original, T target, out T trampoline) where T : Delegate
    {
        var detour = Il2CppInteropRuntime.Instance.DetourProvider.Create(original, target);
        trampoline = detour.GenerateTrampoline<T>();
        detour.Apply();
        return detour;
    }
}
