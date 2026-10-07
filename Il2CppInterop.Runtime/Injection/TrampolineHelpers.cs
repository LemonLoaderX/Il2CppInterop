using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.Runtime;
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
        var elements = new List<(Type Type, int Offset)>();
        // Non-blittable IL2CPP value types (including generic structs) are CLR
        // proxy classes. Their CLR fields describe the wrapper, not the native ABI.
        var collected = managedType.IsSubclassOf(typeof(Il2CppSystem.ValueType))
            ? TryCollectNativeArm64HfaElements(Il2CppClassPointerStore.GetNativeClassPointer(managedType),
                0, elements, new HashSet<IntPtr>())
            : TryCollectArm64HfaElements(managedType, 0, elements);
        if (!collected ||
            elements.Count is < 1 or > 4)
        {
            return false;
        }

        var candidateElementType = elements[0].Type;
        if (elements.Any(element => element.Type != candidateElementType))
            return false;
        elementType = candidateElementType;

        var elementSize = elementType == typeof(float) ? sizeof(float) : sizeof(double);
        var orderedElements = elements.OrderBy(element => element.Offset).ToArray();
        for (var index = 0; index < orderedElements.Length; index++)
        {
            if (orderedElements[index].Offset != index * elementSize)
                return false;
        }

        elementCount = orderedElements.Length;
        return size == elementCount * elementSize;
    }

    private static bool TryCollectArm64HfaElements(
        Type type,
        int baseOffset,
        ICollection<(Type Type, int Offset)> elements)
    {
        if (type == typeof(float) || type == typeof(double))
        {
            elements.Add((type, baseOffset));
            return true;
        }
        if (!type.IsValueType || type.IsEnum || type.IsPrimitive)
            return false;

        var fields = type
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => !field.IsStatic)
            .ToArray();
        if (fields.Length == 0)
            return false;

        foreach (var field in fields)
        {
            int offset;
            var explicitOffset = field.GetCustomAttribute<FieldOffsetAttribute>();
            if (explicitOffset != null)
            {
                offset = explicitOffset.Value;
            }
            else
            {
                try
                {
                    offset = checked((int)Marshal.OffsetOf(type, field.Name));
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            if (!TryCollectArm64HfaElements(
                    field.FieldType,
                    checked(baseOffset + offset),
                    elements) ||
                elements.Count > 4)
            {
                return false;
            }
        }
        return true;
    }

    private static unsafe bool TryCollectNativeArm64HfaElements(
        IntPtr klass, int baseOffset, ICollection<(Type Type, int Offset)> elements, HashSet<IntPtr> visiting)
    {
        if (klass == IntPtr.Zero)
            throw new ArgumentException("A native value type must have initialized IL2CPP class metadata.");
        if (!IL2CPP.il2cpp_class_is_valuetype(klass) || IL2CPP.il2cpp_class_is_enum(klass))
            return false;

        var kind = (Il2CppTypeEnum)IL2CPP.il2cpp_type_get_type(IL2CPP.il2cpp_class_get_type(klass));
        if (kind is Il2CppTypeEnum.IL2CPP_TYPE_R4 or Il2CppTypeEnum.IL2CPP_TYPE_R8)
        {
            elements.Add((kind == Il2CppTypeEnum.IL2CPP_TYPE_R4 ? typeof(float) : typeof(double), baseOffset));
            return elements.Count <= 4;
        }
        if (kind is not (Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE or Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST) ||
            !visiting.Add(klass))
            return false;

        try
        {
            var initialCount = elements.Count;
            var iterator = IntPtr.Zero;
            IntPtr field;
            while ((field = IL2CPP.il2cpp_class_get_fields(klass, ref iterator)) != IntPtr.Zero)
            {
                if (((FieldAttributes)IL2CPP.il2cpp_field_get_flags(field) & FieldAttributes.Static) != 0)
                    continue;
                var fieldType = IL2CPP.il2cpp_field_get_type(field);
                if (IL2CPP.il2cpp_type_is_byref(fieldType))
                    return false;
                // IL2CPP instance field offsets include the boxed object header,
                // even for nested value types; the ABI carries unboxed storage.
                var offset = checked((int)IL2CPP.il2cpp_field_get_offset(field) - sizeof(Il2CppObject));
                if (offset < 0 || !TryCollectNativeArm64HfaElements(IL2CPP.il2cpp_class_from_type(fieldType),
                        checked(baseOffset + offset), elements, visiting))
                    return false;
            }
            return elements.Count > initialCount;
        }
        finally
        {
            visiting.Remove(klass);
        }
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
