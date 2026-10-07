using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.HarmonySupport;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.Injection.Hooks;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Startup;
using MonoMod.Utils;

if (args.Length == 2 && args[0] == "--native-hfa")
{
    NativeHfaProbe.Run(args[1]);
    return;
}

VerifyHfa<Float2>(8, typeof(float), 2);
VerifyHfa<Double4>(32, typeof(double), 4);
VerifyHfa<SequentialFloat3>(12, typeof(float), 3);
VerifyHfa<NestedFloat4>(16, typeof(float), 4);

var mixed = TrampolineHelpers.GetFixedSizeStructType(typeof(Mixed), 8);
Assert(!TrampolineHelpers.IsArm64Hfa(mixed), "Mixed aggregates must use the integer ABI class.");
Assert(mixed == typeof(ulong), "An eight-byte non-HFA should use the primitive integer carrier.");

var float5 = TrampolineHelpers.GetFixedSizeStructType(typeof(Float5), 20);
Assert(!TrampolineHelpers.IsArm64Hfa(float5), "AAPCS64 limits HFAs to four elements.");
Assert(Marshal.SizeOf(float5) == 20, "The non-HFA carrier size is incorrect.");

VerifyResolvedIcall();
VerifyMissingIcall();
VerifyHookExceptionReporter();
VerifyExplicitGameAssemblyHandle();
VerifyGenericMethodResolvers();
VerifyArm64ExceptionReturnBuffer();
VerifyHarmonyTrampolineConversionExceptionReturn();

Console.WriteLine("Il2CppInterop runtime regression tests passed.");

static void VerifyResolvedIcall()
{
    IncrementICall expected = value => value + 1;
    var pointer = Marshal.GetFunctionPointerForDelegate(expected);
    var resolverCalls = 0;

    var resolved = InternalCallResolver.TryResolve(
        "Fixture::Increment",
        _ =>
        {
            resolverCalls++;
            return pointer;
        },
        out IncrementICall? actual);

    Assert(resolved, "A non-null ICall pointer was not resolved.");
    Assert(actual is not null && actual(41) == 42, "The resolved ICall delegate returned the wrong value.");
    Assert(resolverCalls == 1, "The ICall resolver was not invoked exactly once.");
    GC.KeepAlive(expected);
}

static void VerifyMissingIcall()
{
    const string signature = "UnityEngine.Cursor::get_visible";
    var resolved = InternalCallResolver.TryResolve<IncrementICall>(
        signature,
        _ => IntPtr.Zero,
        out var unavailable);

    Assert(!resolved && unavailable is null, "A null ICall pointer was reported as available.");

    var missing = InternalCallResolver.Resolve<IncrementICall>(signature, _ => IntPtr.Zero);
    try
    {
        _ = missing(0);
        throw new InvalidOperationException("The missing ICall delegate did not throw.");
    }
    catch (MissingIl2CppInternalCallException exception)
    {
        Assert(exception.Signature == signature, "The missing ICall exception lost its signature.");
        Assert(exception.Message == $"ICall with signature {signature} was not resolved",
            "The missing ICall error message changed unexpectedly.");
    }
}

static void VerifyHookExceptionReporter()
{
    var hook = new ThrowingLogHook();
    hook.Report(new InvalidOperationException("first"));
    hook.Report(new InvalidOperationException("second"));
    Assert(hook.LogCalls == 1,
        "The native hook exception reporter did not suppress repeated logging or let its logger failure escape.");
}

static void VerifyGenericMethodResolvers()
{
    Assert(!InjectorHelpers.UseThreeArgumentGenericMethodHook(new Version(2020, 3, 47)),
        "Unity 2020.3.47 must retain the legacy generic-method ABI.");
    Assert(InjectorHelpers.UseThreeArgumentGenericMethodHook(new Version(2020, 3, 48)),
        "Unity 2020.3.48 must use the three-argument generic-method ABI.");
    Assert(!InjectorHelpers.UseThreeArgumentGenericMethodHook(new Version(2022, 3, 61)),
        "Unity 2022.3.61 must retain the legacy generic-method ABI.");
    Assert(InjectorHelpers.UseThreeArgumentGenericMethodHook(new Version(2022, 3, 62)),
        "Unity 2022.3.62 must use the three-argument generic-method ABI.");
    Assert(InjectorHelpers.UseThreeArgumentGenericMethodHook(new Version(6000, 0, 0)),
        "Unity 6000 must use the three-argument generic-method ABI.");

    var expectedLegacy = (IntPtr)0x12345678;
    var expectedThreeArgument = (IntPtr)0x23456789;
    var requested = new List<InjectionTarget>();
    Il2CppInteropRuntime.Create(new RuntimeConfiguration
    {
        UnityVersion = new Version(2022, 3, 44),
        DetourProvider = new UnusedDetourProvider(),
        IsAndroid = true,
        GameAssemblyHandle = (IntPtr)0x34567890,
        InjectionTargetResolver = target =>
        {
            requested.Add(target);
            return target switch
            {
                InjectionTarget.GenericMethodGetMethodLegacy => expectedLegacy,
                InjectionTarget.GenericMethodGetMethodThreeArgument => expectedThreeArgument,
                _ => IntPtr.Zero
            };
        }
    });

    var actualLegacy = new GenericMethod_GetMethod_Legacy_Hook().FindTargetMethod();
    var actualThreeArgument = new GenericMethod_GetMethod_ThreeArgument_Hook().FindTargetMethod();
    Assert(actualLegacy == expectedLegacy,
        "The legacy generic-method hook ignored the configured native resolver.");
    Assert(actualThreeArgument == expectedThreeArgument,
        "The three-argument generic-method hook ignored the configured native resolver.");
    Assert(requested.Count == 2 && requested[0] == InjectionTarget.GenericMethodGetMethodLegacy,
        "The legacy generic-method hook requested the wrong native target.");
    Assert(requested[1] == InjectionTarget.GenericMethodGetMethodThreeArgument,
        "The three-argument generic-method hook requested the wrong native target.");
    Assert((uint)InjectionTarget.GenericMethodGetMethodThreeArgument == 1,
        "The three-argument generic-method target ID changed.");
    Assert((uint)InjectionTarget.GenericMethodGetMethodLegacy == 7,
        "The legacy generic-method target ID changed.");
    Assert(Enum.Parse<InjectionTarget>("GenericMethodGetMethodUnity6") ==
           InjectionTarget.GenericMethodGetMethodThreeArgument,
        "The legacy Unity6 target name is no longer a compatible alias.");
    Assert(Enum.Parse<InjectionTarget>("GenericMethodGetMethod") ==
           InjectionTarget.GenericMethodGetMethodLegacy,
        "The legacy generic-method target name is no longer a compatible alias.");
}

static void VerifyExplicitGameAssemblyHandle()
{
    try
    {
        Il2CppInteropRuntime.Create(new RuntimeConfiguration { IsAndroid = true });
        throw new InvalidOperationException("Android accepted a missing IL2CPP instance.");
    }
    catch (ArgumentException) { }

    // A host-owned handle works even when no library named GameAssembly exists.
    string library = OperatingSystem.IsWindows() ? "kernel32.dll" :
        OperatingSystem.IsMacOS() ? "/usr/lib/libSystem.B.dylib" : "libc.so.6";
    string symbol = OperatingSystem.IsWindows() ? "GetCurrentProcessId" : "getpid";
    IntPtr handle = NativeLibrary.Load(library);
    try
    {
        var runtime = Il2CppInteropRuntime.Create(new RuntimeConfiguration
        {
            IsAndroid = true,
            GameAssemblyHandle = handle,
            UnityVersion = new Version(6000, 3, 8)
        });
        Assert(InjectorHelpers.Il2CppHandle == handle, "Injection did not reuse the host's exact handle.");
        Assert(InjectorHelpers.TryGetIl2CppExport(symbol, out var address) &&
            address == NativeLibrary.GetExport(handle, symbol), "Injection exports came from another library.");
        runtime.Dispose();
        Assert(NativeLibrary.GetExport(handle, symbol) == address, "Interop freed the host's borrowed handle.");
    }
    finally { NativeLibrary.Free(handle); }
}

static unsafe void VerifyArm64ExceptionReturnBuffer()
{
    var first = Il2CppDetourMethodPatcher.GetZeroArm64ValueReturnBuffer();
    var second = Il2CppDetourMethodPatcher.GetZeroArm64ValueReturnBuffer();
    Assert(first != IntPtr.Zero && first == second,
        "The ARM64 value-return exception fallback buffer was not stable and non-null.");

    var size = (IntPtr.Size * 2) + 16;
    for (var i = 0; i < size; i++)
        Assert(Marshal.ReadByte(first, i) == 0,
            "The ARM64 value-return exception fallback buffer was not zero-initialized.");
}

static unsafe void VerifyHarmonyTrampolineConversionExceptionReturn()
{
    Il2CppInteropRuntime.Create(new RuntimeConfiguration
    {
        UnityVersion = new Version(6000, 0, 0),
        DetourProvider = new UnusedDetourProvider(),
        IsAndroid = false,
        InjectionTargetResolver = static _ => IntPtr.Zero
    });

    var original = typeof(TrampolineProbeTargets).GetMethod(
        nameof(TrampolineProbeTargets.Original),
        BindingFlags.Public | BindingFlags.Static)!;
    var conversionFailure = typeof(TrampolineProbeTargets).GetMethod(
        nameof(TrampolineProbeTargets.ReturnUninitializedObject),
        BindingFlags.Public | BindingFlags.Static)!;
    var patcher = new Il2CppDetourMethodPatcher(original);
    var generate = typeof(Il2CppDetourMethodPatcher).GetMethod(
        "GenerateNativeToManagedTrampoline",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    object[] arguments = [ conversionFailure, 0 ];
    var definition = (DynamicMethodDefinition)generate.Invoke(patcher, arguments)!;
    // Keep this probe focused on Il2CppInterop's generated IL rather than the test project's older MonoMod emitter.
    var generated = DMDCecilGenerator.Generate(definition);
    var trampoline = (ConversionFailureTrampoline)generated.CreateDelegate(
        typeof(ConversionFailureTrampoline));

    Assert(trampoline(null) == IntPtr.Zero,
        "The Harmony native-to-managed trampoline let a managed return-conversion exception escape.");
}

static void VerifyHfa<T>(int size, Type elementType, int elementCount)
{
    var carrier = TrampolineHelpers.GetFixedSizeStructType(typeof(T), size);
    Assert(TrampolineHelpers.IsArm64Hfa(carrier), $"{typeof(T).Name} was not classified as an HFA.");
    Assert(Marshal.SizeOf(carrier) == size, $"{typeof(T).Name} carrier size is incorrect.");
    var fields = carrier.GetFields();
    Assert(fields.Length == elementCount, $"{typeof(T).Name} carrier field count is incorrect.");
    Assert(fields.All(field => field.FieldType == elementType),
        $"{typeof(T).Name} carrier element type is incorrect.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int IncrementICall(int value);

unsafe delegate IntPtr ConversionFailureTrampoline(Il2CppMethodInfo* methodInfo);

public static class TrampolineProbeTargets
{
    public static TrampolineReference Original() => null!;

    public static TrampolineReference ReturnUninitializedObject() =>
        (TrampolineReference)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            typeof(TrampolineReference));
}

public sealed class TrampolineReference(IntPtr pointer) : Il2CppObjectBase(pointer);

sealed class UnusedDetourProvider : IDetourProvider
{
    public IDetour Create<TDelegate>(nint original, TDelegate target) where TDelegate : Delegate =>
        throw new InvalidOperationException("The trampoline regression probe must not install a native detour.");
}

sealed class ThrowingLogHook : Hook<Action>
{
    public int LogCalls { get; private set; }

    public void Report(Exception exception) => ReportManagedException(exception);

    protected override void LogManagedException(Exception exception)
    {
        LogCalls++;
        throw new InvalidOperationException("logger failure");
    }

    public override string TargetMethodName => "RuntimeTest";
    public override Action GetDetour() => static () => { };
    public override IntPtr FindTargetMethod() => IntPtr.Zero;
}

[StructLayout(LayoutKind.Explicit, Size = 8)]
struct Float2
{
    [FieldOffset(0)] public float X;
    [FieldOffset(4)] public float Y;
}

[StructLayout(LayoutKind.Explicit, Size = 32)]
struct Double4
{
    [FieldOffset(0)] public double X;
    [FieldOffset(8)] public double Y;
    [FieldOffset(16)] public double Z;
    [FieldOffset(24)] public double W;
}

[StructLayout(LayoutKind.Explicit, Size = 8)]
struct Mixed
{
    [FieldOffset(0)] public float X;
    [FieldOffset(4)] public int Y;
}

[StructLayout(LayoutKind.Explicit, Size = 20)]
struct Float5
{
    [FieldOffset(0)] public float A;
    [FieldOffset(4)] public float B;
    [FieldOffset(8)] public float C;
    [FieldOffset(12)] public float D;
    [FieldOffset(16)] public float E;
}

[StructLayout(LayoutKind.Sequential)]
struct SequentialFloat3
{
    public float X;
    public float Y;
    public float Z;
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
struct NestedFloat4
{
    [FieldOffset(0)] public Float2 A;
    [FieldOffset(8)] public Float2 B;
}
