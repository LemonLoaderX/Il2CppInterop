using System;
using Il2CppInterop.Common.Host;
using Il2CppInterop.Common.XrefScans;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.XrefScans;

namespace Il2CppInterop.Runtime.Startup;

public record RuntimeConfiguration
{
    public Version UnityVersion { get; init; }
    public IDetourProvider DetourProvider { get; init; }
    public bool IsAndroid { get; init; }
    public Func<InjectionTarget, nint> InjectionTargetResolver { get; init; }
}

public enum InjectionTarget : uint
{
    [Obsolete("Use GenericMethodGetMethodThreeArgument.")]
    GenericMethodGetMethodUnity6 = 1,
    GenericMethodGetMethodThreeArgument = 1,
    MetadataGetTypeInfoFromTypeDefinitionIndex = 2,
    ClassFromIl2CppType = 3,
    ClassFromName = 4,
    ClassGetDefaultFieldValue = 5,
    ClassInit = 6,
    [Obsolete("Use GenericMethodGetMethodLegacy.")]
    GenericMethodGetMethod = 7,
    GenericMethodGetMethodLegacy = 7
}

public sealed class Il2CppInteropRuntime : BaseHost
{
    private Il2CppInteropRuntime()
    {
    }

    public static Il2CppInteropRuntime Instance => GetInstance<Il2CppInteropRuntime>();

    public Version UnityVersion { get; private init; }

    public IDetourProvider DetourProvider { get; private init; }

    public bool IsAndroid { get; private init; }

    public Func<InjectionTarget, nint> InjectionTargetResolver { get; private init; }

    public static Il2CppInteropRuntime Create(RuntimeConfiguration configuration)
    {
        var res = new Il2CppInteropRuntime
        {
            UnityVersion = configuration.UnityVersion,
            DetourProvider = configuration.DetourProvider,
            IsAndroid = configuration.IsAndroid,
            InjectionTargetResolver = configuration.InjectionTargetResolver
        };
        SetInstance(res);
        res.AddXrefScanner<Il2CppInteropRuntime, XrefScanImpl>();
        return res;
    }

    public override void Start()
    {
        UnityVersionHandler.RecalculateHandlers();
        base.Start();
    }
}
