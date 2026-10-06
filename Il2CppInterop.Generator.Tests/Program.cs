using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using Il2CppInterop.Generator;
using Il2CppInterop.Generator.Contexts;
using Il2CppInterop.Generator.MetadataAccess;
using Il2CppInterop.Generator.Passes;

var unityAssembly = new AssemblyDefinition("UnityLayoutFixture", new Version(1, 0, 0, 0));
var unityModule = new ModuleDefinition("UnityLayoutFixture.dll");
unityAssembly.Modules.Add(unityModule);

var privateImplementationDetails = new TypeDefinition(
    string.Empty,
    "<PrivateImplementationDetails>",
    TypeAttributes.NotPublic | TypeAttributes.Sealed);
var fixedBuffer = new TypeDefinition(
    string.Empty,
    "__StaticArrayInitTypeSize=6",
    TypeAttributes.NestedPrivate | TypeAttributes.ExplicitLayout | TypeAttributes.Sealed,
    unityModule.DefaultImporter.ImportType(typeof(ValueType)));
fixedBuffer.ClassLayout = new ClassLayout(1, 6);
privateImplementationDetails.NestedTypes.Add(fixedBuffer);
unityModule.TopLevelTypes.Add(privateImplementationDetails);

var genericContainer = new TypeDefinition(
    "Fixture",
    "GenericContainer",
    TypeAttributes.Public,
    unityModule.DefaultImporter.ImportType(typeof(object)));
var genericBase = new TypeDefinition(
    string.Empty,
    "GenericBase`1",
    TypeAttributes.NestedPrivate,
    unityModule.DefaultImporter.ImportType(typeof(object)));
genericBase.GenericParameters.Add(new GenericParameter(
    "T", GenericParameterAttributes.DefaultConstructorConstraint));
genericBase.GenericParameters[0].Constraints.Add(new GenericParameterConstraint(
    unityModule.DefaultImporter.ImportType(typeof(Exception))));
genericContainer.NestedTypes.Add(genericBase);
unityModule.TopLevelTypes.Add(genericContainer);
var genericDerived = new TypeDefinition(
    "Fixture",
    "GenericDerived",
    TypeAttributes.Public,
    genericBase.MakeGenericInstanceType(genericContainer.ToTypeSignature()).ToTypeDefOrRef());
unityModule.TopLevelTypes.Add(genericDerived);

var delegateContainer = new TypeDefinition(
    "Fixture",
    "DelegateContainer",
    TypeAttributes.Public,
    unityModule.DefaultImporter.ImportType(typeof(object)));
var nestedDelegate = new TypeDefinition(
    string.Empty,
    "Callback",
    TypeAttributes.NestedPrivate | TypeAttributes.Sealed,
    unityModule.DefaultImporter.ImportType(typeof(MulticastDelegate)));
var delegateConstructor = new MethodDefinition(
    ".ctor",
    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName |
    MethodAttributes.RuntimeSpecialName,
    MethodSignature.CreateInstance(
        unityModule.CorLibTypeFactory.Void,
        unityModule.CorLibTypeFactory.Object,
        unityModule.CorLibTypeFactory.IntPtr))
{
    ImplAttributes = MethodImplAttributes.Runtime
};
nestedDelegate.Methods.Add(delegateConstructor);
var delegateInvoke = new MethodDefinition(
    "Invoke",
    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.NewSlot,
    MethodSignature.CreateInstance(unityModule.CorLibTypeFactory.Void))
{
    ImplAttributes = MethodImplAttributes.Runtime
};
nestedDelegate.Methods.Add(delegateInvoke);
delegateContainer.NestedTypes.Add(nestedDelegate);
unityModule.TopLevelTypes.Add(delegateContainer);
var delegateDerived = new TypeDefinition(
    "Fixture",
    "DelegateDerived",
    TypeAttributes.Public,
    unityModule.DefaultImporter.ImportType(typeof(Dictionary<,>))
        .MakeGenericInstanceType(unityModule.CorLibTypeFactory.String, nestedDelegate.ToTypeSignature())
        .ToTypeDefOrRef());
unityModule.TopLevelTypes.Add(delegateDerived);

var dependencyAssembly = new AssemblyDefinition("UnityDependencyFixture", new Version(1, 0, 0, 0));
var dependencyModule = new ModuleDefinition("UnityDependencyFixture.dll");
dependencyAssembly.Modules.Add(dependencyModule);
var externalBase = new TypeDefinition(
    "Fixture.Dependency",
    "ExternalBase`1",
    TypeAttributes.Public,
    dependencyModule.DefaultImporter.ImportType(typeof(object)));
externalBase.GenericParameters.Add(new GenericParameter("T"));
dependencyModule.TopLevelTypes.Add(externalBase);
var externalDerived = new TypeDefinition(
    "Fixture",
    "ExternalDerived",
    TypeAttributes.Public,
    unityModule.DefaultImporter.ImportType(externalBase)
        .MakeGenericInstanceType(unityModule.CorLibTypeFactory.String)
        .ToTypeDefOrRef());
unityModule.TopLevelTypes.Add(externalDerived);

var localInitType = new TypeDefinition(
    "Fixture",
    "LocalInit",
    TypeAttributes.Public,
    unityModule.DefaultImporter.ImportType(typeof(object)));
var readDefault = new MethodDefinition(
    "ReadDefault",
    MethodAttributes.Public | MethodAttributes.Static,
    MethodSignature.CreateStatic(unityModule.CorLibTypeFactory.Int32))
{
    CilMethodBody = new CilMethodBody
    {
        InitializeLocals = true
    }
};
var defaultValue = new CilLocalVariable(unityModule.CorLibTypeFactory.Int32);
readDefault.CilMethodBody.LocalVariables.Add(defaultValue);
readDefault.CilMethodBody.Instructions.Add(CilOpCodes.Ldloc, defaultValue);
readDefault.CilMethodBody.Instructions.Add(CilOpCodes.Ret);
localInitType.Methods.Add(readDefault);
unityModule.TopLevelTypes.Add(localInitType);

var nullableAttribute = new TypeDefinition(
    "System.Runtime.CompilerServices",
    "NullableAttribute",
    TypeAttributes.NotPublic | TypeAttributes.Sealed,
    unityModule.DefaultImporter.ImportType(typeof(Attribute)));
var nullableConstructor = new MethodDefinition(
    ".ctor",
    MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RuntimeSpecialName,
    MethodSignature.CreateInstance(unityModule.CorLibTypeFactory.Void, unityModule.CorLibTypeFactory.Byte))
{
    CilMethodBody = new CilMethodBody()
};
nullableConstructor.CilMethodBody.Instructions.Add(CilOpCodes.Ret);
nullableAttribute.Methods.Add(nullableConstructor);
unityModule.TopLevelTypes.Add(nullableAttribute);

using var gameAssemblies = new AssemblyMetadataAccess(Array.Empty<AssemblyDefinition>());
using var context = new RewriteGlobalContext(
    new GeneratorOptions(),
    gameAssemblies,
    new AssemblyMetadataAccess(new[] { unityAssembly, dependencyAssembly }));

var orphanedDefaultAttributes = Pass19CopyMethodParameters.NormalizeParameterAttributes(
    ParameterAttributes.Optional | ParameterAttributes.HasDefault,
    hasConstant: false);
Assert((orphanedDefaultAttributes & ParameterAttributes.HasDefault) == 0,
    "Parameter copying retained HasDefault without a Constant row.");
Assert((orphanedDefaultAttributes & ParameterAttributes.Optional) != 0,
    "Parameter copying removed the independent Optional flag.");
var validDefaultAttributes = Pass19CopyMethodParameters.NormalizeParameterAttributes(
    ParameterAttributes.Optional | ParameterAttributes.HasDefault,
    hasConstant: true);
Assert((validDefaultAttributes & ParameterAttributes.HasDefault) != 0,
    "Parameter copying removed HasDefault from a valid constant.");

Pass79UnstripTypes.DoPass(context);
Pass80UnstripMethods.DoPass(context);
Pass81FillUnstrippedMethodBodies.DoPass(context);

var outputAssembly = context.GetAssemblyByName("UnityLayoutFixture").NewAssembly;
var outputType = outputAssembly.ManifestModule!.TopLevelTypes
    .Single(type => type.Name == "_PrivateImplementationDetails_")
    .NestedTypes.Single(type => type.Name == "__StaticArrayInitTypeSize_6");

var outputLayout = outputType.ClassLayout ??
    throw new InvalidOperationException("Unstripping removed the explicit class layout.");
Assert(outputLayout.PackingSize == 1, "Unstripping changed the class packing size.");
Assert(outputLayout.ClassSize == 6, "Unstripping changed the explicit class size.");

var outputGenericBase = outputAssembly.ManifestModule.TopLevelTypes
    .Single(type => type.Name == "GenericContainer")
    .NestedTypes.Single(type => type.Name == "GenericBase`1");
Assert(outputGenericBase.GenericParameters.Count == 1,
    "Unstripping removed the generic type parameter from a reference-type shell.");
Assert((outputGenericBase.GenericParameters[0].Attributes &
        GenericParameterAttributes.DefaultConstructorConstraint) == 0,
    "Unstripping retained a constructor constraint that generated proxy types cannot satisfy.");
Assert(outputGenericBase.GenericParameters[0].Constraints.Count == 1 &&
       outputGenericBase.GenericParameters[0].Constraints[0].Constraint?.FullName == typeof(Exception).FullName,
    "Unstripping removed the generic type constraint from a reference-type shell.");
var outputGenericDerived = outputAssembly.ManifestModule.TopLevelTypes
    .Single(type => type.Name == "GenericDerived");
var outputGenericSignature = (outputGenericDerived.BaseType as TypeSpecification)?.Signature ??
    throw new InvalidOperationException("The restored generic base did not retain its type specification.");
var outputGenericBaseReference = outputGenericSignature.GetUnderlyingTypeDefOrRef();
Assert(ReferenceEquals(outputGenericBaseReference, outputGenericBase),
    "Unstripping left a same-assembly TypeRef instead of the restored generic TypeDef.");

var outputLocalInit = outputAssembly.ManifestModule.TopLevelTypes
    .Single(type => type.Name == "LocalInit");
var outputReadDefault = outputLocalInit.Methods
    .Single(method => method.Name == "ReadDefault");
Assert(outputReadDefault.CilMethodBody?.InitializeLocals == true,
    "Unstripping removed the method flag that zero-initializes local variables.");

var outputNullableAttribute = outputAssembly.ManifestModule.TopLevelTypes
    .SingleOrDefault(type => type.FullName == nullableAttribute.FullName);
Assert(outputNullableAttribute == null ||
       (outputNullableAttribute.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public ||
       outputNullableAttribute.Methods.Any(method => method.IsConstructor &&
           method.Signature?.ParameterTypes.SingleOrDefault()?.FullName == "System.Byte"),
    "Unstripping exposed a public NullableAttribute without its byte constructor.");

var outputDelegate = outputAssembly.ManifestModule.TopLevelTypes
    .Single(type => type.Name == "DelegateContainer")
    .NestedTypes.Single(type => type.Name == "Callback");
Assert(outputDelegate.BaseType?.FullName == typeof(MulticastDelegate).FullName,
    "Unstripping removed the delegate type required by restored signatures.");
var outputDelegateInvoke = outputDelegate.Methods.SingleOrDefault(method => method.Name == "Invoke");
Assert(outputDelegateInvoke != null && outputDelegateInvoke.ImplAttributes == MethodImplAttributes.Runtime,
    "Unstripping did not preserve the runtime Invoke signature on a referenced delegate.");
var outputDelegateDerived = outputAssembly.ManifestModule.TopLevelTypes
    .Single(type => type.Name == "DelegateDerived");
var outputDelegateSignature = (outputDelegateDerived.BaseType as TypeSpecification)?.Signature as
    GenericInstanceTypeSignature ??
    throw new InvalidOperationException("The restored delegate reference did not retain its generic signature.");
var outputDelegateArgument = outputDelegateSignature.TypeArguments[1]
    .GetUnderlyingTypeDefOrRef();
Assert(ReferenceEquals(outputDelegateArgument, outputDelegate),
    "Unstripping left a same-assembly TypeRef instead of the restored delegate TypeDef.");

var outputDependency = context.GetAssemblyByName("UnityDependencyFixture").NewAssembly;
var outputExternalBase = outputDependency.ManifestModule!.TopLevelTypes
    .Single(type => type.Name == "ExternalBase`1");
Assert(outputExternalBase.GenericParameters.Count == 1,
    "Unstripping did not restore a generic type referenced by another Unity module.");

TestRenamedReferences();
TestCrossAssemblyDependencyChain();
Console.WriteLine("Il2CppInterop generator unstripping tests passed.");

static void TestCrossAssemblyDependencyChain()
{
    var assemblies = new[] { "ChainSource", "ChainLeaf", "ChainMiddle" }
        .Select(name =>
        {
            var assembly = new AssemblyDefinition(name, new Version(1, 0, 0, 0));
            assembly.Modules.Add(new ModuleDefinition(name + ".dll"));
            return assembly;
        }).ToArray();
    var source = assemblies[0].ManifestModule!;
    var leafModule = assemblies[1].ManifestModule!;
    var middleModule = assemblies[2].ManifestModule!;
    var leaf = new TypeDefinition("Fixture", "Leaf`1", TypeAttributes.Public,
        leafModule.DefaultImporter.ImportType(typeof(object)));
    leaf.GenericParameters.Add(new GenericParameter("T"));
    leafModule.TopLevelTypes.Add(leaf);
    var middle = new TypeDefinition("Fixture", "Middle`1", TypeAttributes.Public,
        middleModule.DefaultImporter.ImportType(leaf)
            .MakeGenericInstanceType(middleModule.CorLibTypeFactory.Int32).ToTypeDefOrRef());
    middle.GenericParameters.Add(new GenericParameter("T"));
    middleModule.TopLevelTypes.Add(middle);
    source.TopLevelTypes.Add(new TypeDefinition("Fixture", "Derived", TypeAttributes.Public,
        source.DefaultImporter.ImportType(middle)
            .MakeGenericInstanceType(source.CorLibTypeFactory.String).ToTypeDefOrRef()));

    using var context = new RewriteGlobalContext(new GeneratorOptions(),
        new AssemblyMetadataAccess(Array.Empty<AssemblyDefinition>()),
        new AssemblyMetadataAccess(assemblies));
    Pass79UnstripTypes.DoPass(context);
    var restoredMiddle = context.GetAssemblyByName("ChainMiddle")
        .TryGetTypeByName(middle.FullName)?.NewType;
    var restoredLeaf = context.GetAssemblyByName("ChainLeaf")
        .TryGetTypeByName(leaf.FullName)?.NewType;
    Assert(restoredMiddle?.GenericParameters.Count == 1 && restoredLeaf?.GenericParameters.Count == 1,
        "Cross-assembly restoration missed a dependency introduced by a restored generic type.");
    var directory = Path.Combine(Path.GetTempPath(), "interop-chain-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var loadContext = new System.Runtime.Loader.AssemblyLoadContext("chain-fixture", isCollectible: true);
    try
    {
        System.Reflection.Assembly? loadedSource = null;
        foreach (var name in new[] { "ChainLeaf", "ChainMiddle", "ChainSource" })
        {
            var path = Path.Combine(directory, name + ".dll");
            context.GetAssemblyByName(name).NewAssembly.Write(path);
            using var stream = File.OpenRead(path);
            var loaded = loadContext.LoadFromStream(stream);
            if (name == "ChainSource")
                loadedSource = loaded;
        }
        var baseType = loadedSource!.GetType("Fixture.Derived", true)!.BaseType!;
        Assert(baseType.GetGenericTypeDefinition().FullName == "Fixture.Middle`1" &&
               baseType.BaseType!.GetGenericTypeDefinition().FullName == "Fixture.Leaf`1",
            "The serialized dependency chain does not load with its generated definitions.");
    }
    finally
    {
        loadContext.Unload();
        Directory.Delete(directory, true);
    }
}

static void TestRenamedReferences()
{
    var self = new AssemblyDefinition("RenamedSelfFixture", new Version(1, 0, 0, 0));
    var module = new ModuleDefinition("RenamedSelfFixture.dll");
    self.Modules.Add(module);
    var parent = new TypeDefinition("Fixture", "Parent-Type", TypeAttributes.Public,
        module.DefaultImporter.ImportType(typeof(object)));
    module.TopLevelTypes.Add(parent);
    var nested = new TypeDefinition("", "Nested-Type", TypeAttributes.NestedPublic,
        module.DefaultImporter.ImportType(typeof(object)));
    parent.NestedTypes.Add(nested);
    var readValue = new MethodDefinition("ReadValue", MethodAttributes.Public | MethodAttributes.Static,
        MethodSignature.CreateStatic(module.CorLibTypeFactory.Int32)) { CilMethodBody = new CilMethodBody() };
    readValue.CilMethodBody.Instructions.Add(CilOpCodes.Ldc_I4_7);
    readValue.CilMethodBody.Instructions.Add(CilOpCodes.Ret);
    parent.Methods.Add(readValue);
    module.TopLevelTypes.Add(new TypeDefinition("Fixture", "SelfDerived", TypeAttributes.Public,
        module.DefaultImporter.ImportType(parent)));
    module.TopLevelTypes.Add(new TypeDefinition("Fixture", "NestedDerived", TypeAttributes.Public,
        module.DefaultImporter.ImportType(nested)));

    var external = new AssemblyDefinition("RenamedExternalFixture", new Version(1, 0, 0, 0));
    var externalModule = new ModuleDefinition("RenamedExternalFixture.dll");
    external.Modules.Add(externalModule);
    var externalBase = new TypeDefinition("Fixture", "External-Type", TypeAttributes.Public,
        externalModule.DefaultImporter.ImportType(typeof(object)));
    externalModule.TopLevelTypes.Add(externalBase);
    module.TopLevelTypes.Add(new TypeDefinition("Fixture", "ExternalDerived", TypeAttributes.Public,
        module.DefaultImporter.ImportType(externalBase)));

    using var context = new RewriteGlobalContext(new GeneratorOptions(),
        new AssemblyMetadataAccess(Array.Empty<AssemblyDefinition>()),
        new AssemblyMetadataAccess(new[] { self, external }));
    Pass79UnstripTypes.DoPass(context);
    Pass80UnstripMethods.DoPass(context);
    Pass81FillUnstrippedMethodBodies.DoPass(context);
    var output = context.GetAssemblyByName(self.Name!);
    Assert(output.TryGetTypeByName(parent.FullName)?.NewType.Name == "Parent_Type",
        "Renaming lost the original parent type identity.");
    Assert(output.TryGetTypeByName(nested.FullName)?.NewType.Name == "Nested_Type",
        "Renaming lost the original nested type identity.");
    foreach (var assembly in context.Assemblies)
        Assert(assembly.NewAssembly.ManifestModule!.GetAllTypes().GroupBy(type => type.FullName)
            .All(group => group.Count() == 1), "Renaming generated duplicate type definitions.");

    var directory = Path.Combine(Path.GetTempPath(), "interop-renamed-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var loadContext = new System.Runtime.Loader.AssemblyLoadContext("renamed-fixture", isCollectible: true);
    try
    {
        foreach (var assembly in context.Assemblies)
            assembly.NewAssembly.Write(Path.Combine(directory, assembly.NewAssembly.Name + ".dll"));
        using (var stream = File.OpenRead(Path.Combine(directory, "RenamedExternalFixture.dll")))
            loadContext.LoadFromStream(stream);
        using var selfStream = File.OpenRead(Path.Combine(directory, "RenamedSelfFixture.dll"));
        var loaded = loadContext.LoadFromStream(selfStream);
        Assert(loaded.GetType("Fixture.SelfDerived", true)!.BaseType!.FullName == "Fixture.Parent_Type",
            "Self reference still targets the old name.");
        Assert(loaded.GetType("Fixture.NestedDerived", true)!.BaseType!.FullName == "Fixture.Parent_Type+Nested_Type",
            "Nested reference still targets the old name.");
        Assert(loaded.GetType("Fixture.ExternalDerived", true)!.BaseType!.FullName == "Fixture.External_Type",
            "Cross-assembly reference still targets the old name.");
        Assert((int)loaded.GetType("Fixture.Parent_Type", true)!.GetMethod("ReadValue")!.Invoke(null, null)! == 7,
            "Renaming prevented method restoration.");
    }
    finally
    {
        loadContext.Unload();
        Directory.Delete(directory, true);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
