using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.Startup;

internal static class NativeHfaProbe
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetClass(int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CheckCallback(IntPtr callback);

    internal static void Run(string libraryPath)
    {
        // Run in a separate process: IL2CPP's P/Invoke bindings and class pointers
        // are process lifetime state, just as they are for a real game instance.
        var library = NativeLibrary.Load(Path.GetFullPath(libraryPath));
        NativeLibrary.SetDllImportResolver(typeof(IL2CPP).Assembly,
            (name, _, _) => name == "GameAssembly" ? library : IntPtr.Zero);
        using var runtime = Il2CppInteropRuntime.Create(new RuntimeConfiguration
        {
            IsAndroid = true, GameAssemblyHandle = library, UnityVersion = new Version(2022, 3, 0)
        });
        var getClass = Marshal.GetDelegateForFunctionPointer<GetClass>(NativeLibrary.GetExport(library, "fixture_class"));
        var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("HfaCallbacks"), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("HfaCallbacks");
        var cases = new (Type Proxy, int Size, Type? Element, int Count)[]
        {
            (typeof(PairProxy<float>), 8, typeof(float), 2),
            (typeof(PairProxy<double>), 16, typeof(double), 2),
            (typeof(PairProxy<PairProxy<float>>), 16, typeof(float), 4),
            (typeof(PairProxy<PairProxy<double>>), 32, typeof(double), 4),
            (typeof(PairProxy<int>), 8, null, 0),
            (typeof(PairProxy<byte>), 12, null, 0),
            (typeof(PairProxy<long>), 20, null, 0),
            (typeof(PairProxy<object>), 8, null, 0),
            (typeof(PairProxy<short>), 8, null, 0),
            (typeof(PairProxy<uint>), 8, null, 0),
            (typeof(PairProxy<ushort>), 8, null, 0),
        };
        for (var index = 0; index < cases.Length; index++)
        {
            var test = cases[index];
            Il2CppClassPointerStore.SetNativeClassPointer(test.Proxy, getClass(index));
            var carrier = TrampolineHelpers.GetFixedSizeStructType(test.Proxy, test.Size);
            if (TrampolineHelpers.IsArm64Hfa(carrier) != (test.Element != null) || Marshal.SizeOf(carrier) != test.Size)
                throw new Exception($"Incorrect native layout classification for case {index}.");
            if (test.Element == null)
                continue;
            if (carrier.GetFields().Length != test.Count || carrier.GetFields().Any(f => f.FieldType != test.Element))
                throw new Exception($"Incorrect native HFA elements for case {index}.");
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 && test.Proxy.NativeType() != carrier)
                throw new Exception("NativeType did not use the proxy's native carrier.");

            var delegateBuilder = module.DefineType($"Callback{index}", TypeAttributes.Public | TypeAttributes.Sealed,
                typeof(MulticastDelegate));
            delegateBuilder.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(UnmanagedFunctionPointerAttribute).GetConstructor(new[] { typeof(CallingConvention) })!,
                new object[] { CallingConvention.Cdecl }));
            delegateBuilder.DefineConstructor(MethodAttributes.Public | MethodAttributes.RTSpecialName,
                CallingConventions.Standard, new[] { typeof(object), typeof(IntPtr) })
                .SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
            delegateBuilder.DefineMethod("Invoke", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot,
                carrier, new[] { carrier }).SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
            var delegateType = delegateBuilder.CreateType()!;
            var transform = new DynamicMethod($"DoubleElements{index}", carrier, new[] { carrier });
            var il = transform.GetILGenerator();
            foreach (var field in carrier.GetFields())
            {
                il.Emit(OpCodes.Ldarga_S, (byte)0);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldfld, field);
                if (test.Element == typeof(float)) il.Emit(OpCodes.Ldc_R4, 2f);
                else il.Emit(OpCodes.Ldc_R8, 2d);
                il.Emit(OpCodes.Mul);
                il.Emit(OpCodes.Stfld, field);
            }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);
            var callback = transform.CreateDelegate(delegateType);
            var check = Marshal.GetDelegateForFunctionPointer<CheckCallback>(
                NativeLibrary.GetExport(library, $"fixture_callback_{index}"));
            if (check(Marshal.GetFunctionPointerForDelegate(callback)) != 1)
                throw new Exception($"Native aggregate argument/return roundtrip failed for case {index}.");
            GC.KeepAlive(callback);
        }
        Console.WriteLine($"NATIVE_HFA_PASS {RuntimeInformation.ProcessArchitecture}: proxy metadata, nested floats/doubles, " +
            "static fields, mixed/padded/oversized/reference/byref/enum/overlap rejection and native callback arguments/returns");
    }

    // Generated non-blittable structs are classes with property accessors. These
    // deliberately have no CLR instance fields describing their native contents.
    private sealed class PairProxy<T>(IntPtr pointer) : Il2CppSystem.ValueType(pointer);
}
