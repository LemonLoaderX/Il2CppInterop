using System;
using System.Collections.Generic;
using System.Linq;
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
    private static readonly Dictionary<(Type ManagedType, int Size), Type> _fixedStructCache = new();
    private static readonly HashSet<Type> _arm64HfaTypes = new();

    internal static Type GetFixedSizeStructType(Type managedType, int size)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        var hfa = TryGetArm64Hfa(managedType, size, out var elementType, out var elementCount);
        // Primitive integer types have an unambiguous ARM64 register ABI and,
        // unlike an empty explicit-layout type, CoreCLR preserves their bits.
        if (!hfa)
        {
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
        }

        var key = (managedType, size);
        lock (_fixedStructCache)
        {
            if (_fixedStructCache.TryGetValue(key, out var result))
                return result;

            _fixedStructAssembly ??= AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("FixedSizeStructAssembly"),
                AssemblyBuilderAccess.Run);
            _fixedStructModuleBuilder ??=
                _fixedStructAssembly.DefineDynamicModule("FixedSizeStructAssembly");

            var tb = _fixedStructModuleBuilder.DefineType(
                $"IL2CPPDetour_Arm64Struct_{_fixedStructCache.Count}_{size}b",
                TypeAttributes.ExplicitLayout | TypeAttributes.Sealed,
                typeof(ValueType),
                size);
            if (hfa)
            {
                var elementSize = elementType == typeof(float) ? sizeof(float) : sizeof(double);
                for (var index = 0; index < elementCount; index++)
                {
                    var field = tb.DefineField(
                        $"Element{index}",
                        elementType,
                        FieldAttributes.Public);
                    field.SetOffset(index * elementSize);
                }
            }
            else
            {
                for (var offset = 0; offset < size; offset++)
                {
                    var field = tb.DefineField(
                        $"Byte{offset}",
                        typeof(byte),
                        FieldAttributes.Public);
                    field.SetOffset(offset);
                }
            }

            var type = tb.CreateType();
            _fixedStructCache[key] = type;
            if (hfa)
                _arm64HfaTypes.Add(type);
            return type;
        }
    }

    internal static bool IsArm64Hfa(Type type)
    {
        lock (_fixedStructCache)
            return _arm64HfaTypes.Contains(type);
    }

    private static bool TryGetArm64Hfa(
        Type managedType,
        int size,
        out Type elementType,
        out int elementCount)
    {
        elementType = typeof(void);
        elementCount = 0;
        var fields = managedType
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => !field.IsStatic)
            .OrderBy(field => field.GetCustomAttribute<FieldOffsetAttribute>()?.Value ?? int.MaxValue)
            .ToArray();
        if (fields.Length is < 1 or > 4 ||
            fields.Any(field => field.FieldType != typeof(float) && field.FieldType != typeof(double)))
        {
            return false;
        }

        var candidateElementType = fields[0].FieldType;
        if (fields.Any(field => field.FieldType != candidateElementType))
            return false;
        elementType = candidateElementType;

        var elementSize = elementType == typeof(float) ? sizeof(float) : sizeof(double);
        for (var index = 0; index < fields.Length; index++)
        {
            if (fields[index].GetCustomAttribute<FieldOffsetAttribute>()?.Value != index * elementSize)
                return false;
        }

        elementCount = fields.Length;
        return size == elementCount * elementSize;
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
            return GetFixedSizeStructType(managedType, fixedSize);
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
