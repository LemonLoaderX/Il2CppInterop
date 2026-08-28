using AsmResolver.DotNet;
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

using var gameAssemblies = new AssemblyMetadataAccess(Array.Empty<AssemblyDefinition>());
using var context = new RewriteGlobalContext(
    new GeneratorOptions(),
    gameAssemblies,
    new AssemblyMetadataAccess(new[] { unityAssembly }));

Pass79UnstripTypes.DoPass(context);

var outputAssembly = context.GetAssemblyByName("UnityLayoutFixture").NewAssembly;
var outputType = outputAssembly.ManifestModule!.TopLevelTypes
    .Single(type => type.Name == "<PrivateImplementationDetails>")
    .NestedTypes.Single(type => type.Name == "__StaticArrayInitTypeSize=6");

var outputLayout = outputType.ClassLayout ??
    throw new InvalidOperationException("Unstripping removed the explicit class layout.");
Assert(outputLayout.PackingSize == 1, "Unstripping changed the class packing size.");
Assert(outputLayout.ClassSize == 6, "Unstripping changed the explicit class size.");

Console.WriteLine("Il2CppInterop generator layout-preservation tests passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
