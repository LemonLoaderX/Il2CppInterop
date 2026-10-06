using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace Il2CppInterop.Generator.Utils;

// Unity bindings pin managed strings before passing a pointer/length to an icall.
// These spans must stay in the managed heap, rather than become IL2CPP proxy objects.
internal static class StringSpanMarshalling
{
    public static bool IsSpan(TypeSignature? type) => type is GenericInstanceTypeSignature
    {
        GenericType.FullName: "System.ReadOnlySpan`1",
        TypeArguments.Count: 1
    } span && IsCorlibType(span.GenericType) && span.TypeArguments[0].FullName == "System.Char";

    public static bool ContainsSpan(TypeSignature? type) => type != null &&
        (IsSpan(type) || type is GenericInstanceTypeSignature generic && generic.TypeArguments.Any(ContainsSpan) ||
         type is TypeSpecificationSignature specification && ContainsSpan(specification.BaseType));

    public static GenericInstanceTypeSignature GetSpanType(ModuleDefinition module) =>
        new(new TypeReference(module, module.CorLibTypeFactory.CorLibScope, "System", "ReadOnlySpan`1"),
            true, module.CorLibTypeFactory.Char);

    public static bool IsStringAsSpan(IMethodDescriptor method) =>
        method.DeclaringType?.FullName == "System.MemoryExtensions" && IsCorlibType(method.DeclaringType) &&
        method.Name == "AsSpan" && method.Signature is { HasThis: false, GenericParameterCount: 0 } signature &&
        IsSpan(signature.ReturnType) && signature.ParameterTypes.Count == 1 &&
        signature.ParameterTypes[0].FullName == "System.String";

    public static IMethodDefOrRef? ResolveMethod(IMethodDescriptor method, ModuleDefinition module)
    {
        var signature = method.Signature;
        if (signature == null || signature.GenericParameterCount != 0)
            return null;

        if (IsStringAsSpan(method))
        {
            // MemoryExtensions is forwarded by System.Memory, not System.Runtime.
            var memory = new AssemblyReference("System.Memory", new Version(6, 0, 0, 0))
            {
                PublicKeyOrToken = new byte[] { 0xcc, 0x7b, 0x13, 0xff, 0xcd, 0x2d, 0xdd, 0x51 }
            };
            return new MemberReference(new TypeReference(module, memory, "System", "MemoryExtensions"),
                "AsSpan", MethodSignature.CreateStatic(GetSpanType(module), module.CorLibTypeFactory.String));
        }

        if (!IsSpan(method.DeclaringType?.ToTypeSignature()) || !signature.HasThis ||
            signature.ParameterTypes.Count != 0)
            return null;

        TypeSignature returnType;
        switch (method.Name?.Value)
        {
            case "get_Length" when signature.ReturnType.FullName == "System.Int32":
                returnType = module.CorLibTypeFactory.Int32;
                break;
            case "GetPinnableReference" when IsElementReference(signature.ReturnType):
                returnType = new CustomModifierTypeSignature(new TypeReference(module,
                    module.CorLibTypeFactory.CorLibScope, "System.Runtime.InteropServices", "InAttribute"), true,
                    new GenericParameterSignature(module, GenericParameterType.Type, 0).MakeByReferenceType());
                break;
            default:
                return null;
        }
        return new MemberReference(GetSpanType(module).ToTypeDefOrRef(), method.Name,
            MethodSignature.CreateInstance(returnType));
    }

    private static bool IsCorlibType(ITypeDescriptor type) => type.Scope?.Name is
        "mscorlib" or "netstandard" or "System.Private.CoreLib" or "System.Runtime" or "System.Memory";

    private static bool IsElementReference(TypeSignature type)
    {
        while (type is CustomModifierTypeSignature modifier)
            type = modifier.BaseType;
        return type is ByReferenceTypeSignature
        {
            BaseType: GenericParameterSignature { ParameterType: GenericParameterType.Type, Index: 0 }
        };
    }
}
