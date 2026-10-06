using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using Il2CppInterop.Generator;
using Il2CppInterop.Generator.Contexts;
using Il2CppInterop.Generator.MetadataAccess;
using Il2CppInterop.Generator.Utils;

public static class StringSpanTests
{
    public static void Run()
    {
        var corlib = new AssemblyDefinition("mscorlib", new Version(4, 0, 0, 0));
        corlib.Modules.Add(new ModuleDefinition("mscorlib.dll"));
        var fixture = new AssemblyDefinition("StringSpanFixture", new Version(1, 0, 0, 0));
        fixture.Modules.Add(new ModuleDefinition("StringSpanFixture.dll"));
        using var context = new RewriteGlobalContext(new GeneratorOptions(),
            new AssemblyMetadataAccess(new[] { corlib, fixture }),
            new AssemblyMetadataAccess(Array.Empty<AssemblyDefinition>()));
        var core = context.CorLib;
        var proxySpan = RegisterProxy(core, "System", "ReadOnlySpan`1");
        proxySpan.GenericParameters.Add(new GenericParameter("T"));
        var proxyExtensions = RegisterProxy(core, "System", "MemoryExtensions");
        RegisterProxy(core, "System.Runtime.InteropServices", "InAttribute");
        var proxySpanOfChar = proxySpan.MakeGenericInstanceType(core.Imports.Module.CorLibTypeFactory.Char);
        AddMethod(proxyExtensions, "AsSpan", MethodSignature.CreateStatic(proxySpanOfChar,
            core.Imports.Module.CorLibTypeFactory.String), CilOpCodes.Ldnull);
        AddMethod(proxySpan, "GetPinnableReference", MethodSignature.CreateInstance(
            new GenericParameterSignature(GenericParameterType.Type, 0).MakeByReferenceType()), CilOpCodes.Ldnull,
            CilOpCodes.Throw);
        AddMethod(proxySpan, "get_Length", MethodSignature.CreateInstance(core.Imports.Module.CorLibTypeFactory.Int32),
            CilOpCodes.Ldc_I4_0);

        var source = fixture.ManifestModule!;
        var span = new TypeReference(source, source.CorLibTypeFactory.CorLibScope, "System", "ReadOnlySpan`1")
            .MakeGenericInstanceType(source.CorLibTypeFactory.Char);
        var extensions = new TypeReference(source, source.CorLibTypeFactory.CorLibScope, "System", "MemoryExtensions");
        var inAttribute = new TypeReference(source, source.CorLibTypeFactory.CorLibScope,
            "System.Runtime.InteropServices", "InAttribute");
        var pinnableReturn = new CustomModifierTypeSignature(inAttribute, true,
            new GenericParameterSignature(GenericParameterType.Type, 0).MakeByReferenceType());
        var sourceType = new TypeDefinition("Fixture", "StringReader", TypeAttributes.Public,
            source.CorLibTypeFactory.Object.ToTypeDefOrRef());
        source.TopLevelTypes.Add(sourceType);
        var original = new MethodDefinition("Read", MethodAttributes.Public | MethodAttributes.Static,
            MethodSignature.CreateStatic(source.CorLibTypeFactory.Int32, source.CorLibTypeFactory.String))
        {
            CilMethodBody = new CilMethodBody { InitializeLocals = true }
        };
        sourceType.Methods.Add(original);
        var spanLocal = new CilLocalVariable(span);
        var pinLocal = new CilLocalVariable(source.CorLibTypeFactory.Char.MakeByReferenceType().MakePinnedType());
        original.CilMethodBody.LocalVariables.Add(spanLocal);
        original.CilMethodBody.LocalVariables.Add(pinLocal);
        var il = original.CilMethodBody.Instructions;
        il.Add(CilOpCodes.Ldarg_0);
        il.Add(CilOpCodes.Call, new MemberReference(extensions, "AsSpan",
            MethodSignature.CreateStatic(span, source.CorLibTypeFactory.String)));
        il.Add(CilOpCodes.Stloc, spanLocal);
        il.Add(CilOpCodes.Ldloca, spanLocal);
        il.Add(CilOpCodes.Call, new MemberReference(span.ToTypeDefOrRef(), "GetPinnableReference",
            MethodSignature.CreateInstance(pinnableReturn)));
        il.Add(CilOpCodes.Stloc, pinLocal);
        il.Add(CilOpCodes.Ldloca, spanLocal);
        il.Add(CilOpCodes.Call, new MemberReference(span.ToTypeDefOrRef(), "get_Length",
            MethodSignature.CreateInstance(source.CorLibTypeFactory.Int32)));
        var empty = new CilInstructionLabel();
        il.Add(CilOpCodes.Brfalse, empty);
        il.Add(CilOpCodes.Ldloc, pinLocal);
        il.Add(CilOpCodes.Conv_U);
        il.Add(CilOpCodes.Call, new MemberReference(sourceType, nameof(ReadAfterCollection),
            MethodSignature.CreateStatic(source.CorLibTypeFactory.Int32, source.CorLibTypeFactory.UIntPtr)));
        il.Add(CilOpCodes.Ret);
        empty.Instruction = il.Add(CilOpCodes.Ldc_I4_M1);
        il.Add(CilOpCodes.Ret);

        var output = context.GetAssemblyByName(fixture.Name!);
        var targetType = new TypeDefinition("Fixture", "StringReader", TypeAttributes.Public,
            output.Imports.Module.CorLibTypeFactory.Object.ToTypeDefOrRef());
        output.Imports.Module.TopLevelTypes.Add(targetType);
        var typeContext = new TypeRewriteContext(output, null, targetType);
        output.RegisterTypeRewrite(typeContext);
        var collect = new MethodDefinition(nameof(ReadAfterCollection), MethodAttributes.Public | MethodAttributes.Static,
            MethodSignature.CreateStatic(output.Imports.Module.CorLibTypeFactory.Int32,
                output.Imports.Module.CorLibTypeFactory.UIntPtr)) { CilMethodBody = new CilMethodBody() };
        targetType.Methods.Add(collect);
        collect.CilMethodBody.Instructions.Add(CilOpCodes.Ldarg_0);
        collect.CilMethodBody.Instructions.Add(CilOpCodes.Call, output.Imports.Module.DefaultImporter.ImportMethod(
            typeof(StringSpanTests).GetMethod(nameof(ReadAfterCollection))!));
        collect.CilMethodBody.Instructions.Add(CilOpCodes.Ret);
        var translated = new MethodDefinition(original.Name, original.Attributes,
            MethodSignature.CreateStatic(output.Imports.Module.CorLibTypeFactory.Int32,
                output.Imports.Module.CorLibTypeFactory.String));
        targetType.Methods.Add(translated);
        if (!UnstripTranslator.TranslateMethod(original, translated, typeContext, output.Imports))
            throw new InvalidOperationException("String-span marshalling could not be translated.");

        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("string-span-fixture", isCollectible: true);
        loadContext.Resolving += (_, name) => name.Name == typeof(StringSpanTests).Assembly.GetName().Name
            ? typeof(StringSpanTests).Assembly : null;
        try
        {
            foreach (var assembly in new[] { core.NewAssembly, output.NewAssembly })
            {
                using var stream = new MemoryStream();
                assembly.ManifestModule!.Write(stream);
                stream.Position = 0;
                loadContext.LoadFromStream(stream);
            }
            var method = loadContext.Assemblies.Single(a => a.GetName().Name == "StringSpanFixture")
                .GetType("Fixture.StringReader", true)!.GetMethod("Read")!;
            foreach (var text in new string?[] { null, "", "ascii", "\u6c49\u5b57", new string('x', 100_000) })
            {
                var result = (int)method.Invoke(null, new object?[] { text })!;
                if (result != (string.IsNullOrEmpty(text) ? -1 : text[0]))
                    throw new InvalidOperationException("Translated string pinning changed the first character.");
            }
        }
        finally
        {
            loadContext.Unload();
        }

        var consume = new MemberReference(sourceType, "Consume",
            MethodSignature.CreateStatic(source.CorLibTypeFactory.Int32, span));
        original.CilMethodBody.Instructions.Clear();
        original.CilMethodBody.Instructions.Add(CilOpCodes.Ldarg_0);
        original.CilMethodBody.Instructions.Add(CilOpCodes.Call, new MemberReference(extensions, "AsSpan",
            MethodSignature.CreateStatic(span, source.CorLibTypeFactory.String)));
        original.CilMethodBody.Instructions.Add(CilOpCodes.Stloc, spanLocal);
        original.CilMethodBody.Instructions.Add(CilOpCodes.Ldloc, spanLocal);
        original.CilMethodBody.Instructions.Add(CilOpCodes.Call, consume);
        original.CilMethodBody.Instructions.Add(CilOpCodes.Ret);
        if (UnstripTranslator.TranslateMethod(original, translated, typeContext, output.Imports))
            throw new InvalidOperationException("A managed span was allowed to escape into an IL2CPP signature.");

        original.CilMethodBody.Instructions.Clear();
        original.CilMethodBody.Instructions.Add(CilOpCodes.Ldnull);
        original.CilMethodBody.Instructions.Add(CilOpCodes.Call, new MemberReference(span.ToTypeDefOrRef(), "get_Length",
            MethodSignature.CreateInstance(source.CorLibTypeFactory.Int32)));
        original.CilMethodBody.Instructions.Add(CilOpCodes.Ret);
        if (!UnstripTranslator.TranslateMethod(original, translated, typeContext, output.Imports))
            throw new InvalidOperationException("An unrelated Span API stopped translating.");
        var proxyCall = (IMethodDescriptor)translated.CilMethodBody!.Instructions
            .Single(i => i.OpCode == CilOpCodes.Call).Operand;
        if (proxyCall.DeclaringType?.FullName != "Il2CppSystem.ReadOnlySpan`1<System.Char>")
            throw new InvalidOperationException("An unrelated IL2CPP Span API changed its proxy representation.");
    }

    public static unsafe int ReadAfterCollection(nuint pointer)
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        return *(char*)pointer;
    }

    private static TypeDefinition RegisterProxy(AssemblyRewriteContext context, string ns, string name)
    {
        var type = new TypeDefinition("Il2Cpp" + ns, name, TypeAttributes.Public,
            context.Imports.Module.CorLibTypeFactory.Object.ToTypeDefOrRef());
        context.Imports.Module.TopLevelTypes.Add(type);
        context.RegisterTypeRewrite(new TypeRewriteContext(context, null, type), ns + "." + name);
        return type;
    }

    private static void AddMethod(TypeDefinition type, string name, MethodSignature signature,
        params CilOpCode[] body)
    {
        var method = new MethodDefinition(name, MethodAttributes.Public |
            (signature.HasThis ? 0 : MethodAttributes.Static), signature) { CilMethodBody = new CilMethodBody() };
        type.Methods.Add(method);
        foreach (var opCode in body)
            method.CilMethodBody.Instructions.Add(opCode);
        method.CilMethodBody.Instructions.Add(CilOpCodes.Ret);
    }
}
