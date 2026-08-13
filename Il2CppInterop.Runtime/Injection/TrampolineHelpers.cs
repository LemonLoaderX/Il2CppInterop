using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.Startup;

namespace Il2CppInterop.Runtime.Injection;

internal static class TrampolineHelpers
{
    private static AssemblyBuilder _fixedStructAssembly;
    private static ModuleBuilder _fixedStructModuleBuilder;
    private static readonly Dictionary<int, Type> _fixedStructCache = new();

    internal static Type GetFixedSizeStructType(int size)
    {
        // Primitive integer types have an unambiguous ARM64 register ABI and,
        // unlike an empty explicit-layout type, CoreCLR preserves their bits.
        switch (size)
        {
            case 1:
                return typeof(byte);
            case 2:
                return typeof(ushort);
            case 4:
                return typeof(uint);
            case 8:
                return typeof(ulong);
        }

        if (_fixedStructCache.TryGetValue(size, out var result))
        {
            return result;
        }

        _fixedStructAssembly ??= AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("FixedSizeStructAssembly"), AssemblyBuilderAccess.Run);
        _fixedStructModuleBuilder ??= _fixedStructAssembly.DefineDynamicModule("FixedSizeStructAssembly");

        var tb = _fixedStructModuleBuilder.DefineType($"IL2CPPDetour_FixedSizeStruct_{size}b", TypeAttributes.ExplicitLayout, typeof(ValueType), size);
        for (var offset = 0; offset < size; offset++)
        {
            var field = tb.DefineField($"Byte{offset}", typeof(byte), FieldAttributes.Public);
            field.SetOffset(offset);
        }

        var type = tb.CreateType();
        return _fixedStructCache[size] = type;
    }

    internal static Type NativeType(this Type managedType)
    {
        if (managedType.IsByRef)
        {
            var directType = managedType.GetElementType();

            // bool is byte in Il2Cpp, but int in CLR => force size to be correct
            if (directType == typeof(bool))
            {
                return typeof(byte).MakeByRefType();
            }

            if (directType == typeof(string) || directType.IsSubclassOf(typeof(Il2CppObjectBase)))
            {
                return typeof(IntPtr*);
            }
        }
        else if (managedType.IsSubclassOf(typeof(Il2CppSystem.ValueType)) &&
                 (!Environment.Is64BitProcess ||
                  (Il2CppInteropRuntime.Instance.IsAndroid &&
                   RuntimeInformation.ProcessArchitecture == Architecture.Arm64)))
        {
            // Receive native aggregates by value. Android ARM64 passes small
            // aggregates in registers; the trampoline takes their address when
            // boxing them into the generated managed wrapper.
            uint align = 0;
            var fixedSize = IL2CPP.il2cpp_class_value_size(Il2CppClassPointerStore.GetNativeClassPointer(managedType), ref align);
            return GetFixedSizeStructType(fixedSize);
        }
        else if (managedType == typeof(string) || managedType.IsSubclassOf(typeof(Il2CppObjectBase))) // General reference type
        {
            return typeof(IntPtr);
        }
        else if (managedType == typeof(bool))
        {
            // bool is byte in Il2Cpp, but int in CLR => force size to be correct
            return typeof(byte);
        }

        return managedType;
    }
}
