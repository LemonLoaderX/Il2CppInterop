using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;
using Il2CppInterop.Common;
using Il2CppInterop.Generator.Contexts;
using Il2CppInterop.Generator.Extensions;
using Il2CppInterop.Generator.Utils;
using Microsoft.Extensions.Logging;

namespace Il2CppInterop.Generator.Passes;

public static class Pass79UnstripTypes
{
    public static void DoPass(RewriteGlobalContext context)
    {
        var typesUnstripped = 0;

        foreach (var unityAssembly in context.UnityAssemblies.Assemblies)
        {
            var processedAssembly = context.TryGetAssemblyByName(unityAssembly.Name);
            if (processedAssembly == null)
            {
                var newAssembly = new AssemblyDefinition(unityAssembly.Name, unityAssembly.Version);
                newAssembly.Modules.Add(new ModuleDefinition(unityAssembly.ManifestModule!.Name));
                var newContext = new AssemblyRewriteContext(context, unityAssembly,
                    newAssembly);
                context.AddAssemblyContext(unityAssembly.Name!, newContext);
                processedAssembly = newContext;
            }

            var imports = processedAssembly.Imports;

            foreach (var unityType in unityAssembly.ManifestModule!.TopLevelTypes)
                ProcessType(processedAssembly, unityType, null, imports, ref typesUnstripped, false);

            RestoreMissingSelfReferences(processedAssembly, unityAssembly, imports, ref typesUnstripped);
        }

        RestoreMissingCrossAssemblyReferences(context, ref typesUnstripped);

        Logger.Instance.LogTrace("Unstripped {UnstrippedTypeCount} types", typesUnstripped);
    }

    private static void ProcessType(AssemblyRewriteContext processedAssembly, TypeDefinition unityType,
        TypeDefinition? enclosingNewType, RuntimeAssemblyReferences imports, ref int typesUnstripped,
        bool restoreReferenceOnly)
    {
        if (unityType.Name == "<Module>")
            return;

        if (restoreReferenceOnly && !unityType.IsReferenceType())
            return;

        var isDelegate = unityType.BaseType?.FullName == "System.MulticastDelegate";

        // Delegate methods cannot be recovered unless another restored signature requires the type.
        if (!restoreReferenceOnly && isDelegate)
            return;

        var newModule = processedAssembly.NewAssembly.ManifestModule!;
        var processedType = enclosingNewType == null
            ? processedAssembly.TryGetTypeByName(unityType.FullName)?.NewType
            : enclosingNewType.NestedTypes.SingleOrDefault(it => it.Name == unityType.Name);
        if (unityType.IsEnum)
        {
            if (processedType != null) return;

            typesUnstripped++;
            var clonedType = CloneEnum(unityType, imports);
            if (enclosingNewType == null)
            {
                newModule.TopLevelTypes.Add(clonedType);
            }
            else
            {
                enclosingNewType.NestedTypes.Add(clonedType);
            }

            processedAssembly.RegisterTypeRewrite(new TypeRewriteContext(processedAssembly, null, clonedType));

            return;
        }

        if (processedType == null && !unityType.IsEnum && !HasNonBlittableFields(unityType) &&
            (restoreReferenceOnly || !unityType.HasGenericParameters()))
        {
            typesUnstripped++;
            var clonedType = new TypeDefinition(unityType.Namespace, unityType.Name, ForcePublic(unityType.Attributes), unityType.BaseType == null ? null : newModule.DefaultImporter.ImportType(unityType.BaseType));
            foreach (var genericParameter in unityType.GenericParameters)
            {
                var clonedParameter = new GenericParameter(
                    genericParameter.Name.MakeValidInSource(),
                    genericParameter.Attributes.StripValueTypeConstraint() &
                    ~GenericParameterAttributes.DefaultConstructorConstraint);
                foreach (var constraint in genericParameter.Constraints)
                {
                    if (constraint.Constraint != null)
                        clonedParameter.Constraints.Add(new GenericParameterConstraint(
                            newModule.DefaultImporter.ImportType(constraint.Constraint)));
                }
                clonedType.GenericParameters.Add(clonedParameter);
            }
            if (restoreReferenceOnly && isDelegate)
            {
                foreach (var unityMethod in unityType.Methods)
                {
                    if (unityMethod.Signature == null)
                        continue;

                    clonedType.Methods.Add(new MethodDefinition(
                        unityMethod.Name,
                        unityMethod.Attributes,
                        newModule.DefaultImporter.ImportMethodSignature(unityMethod.Signature))
                    {
                        ImplAttributes = unityMethod.ImplAttributes
                    });
                }
            }
            if (enclosingNewType == null)
            {
                newModule.TopLevelTypes.Add(clonedType);
            }
            else
            {
                enclosingNewType.NestedTypes.Add(clonedType);
            }

            if (clonedType.IsValueType() && unityType.ClassLayout is { } classLayout)
                clonedType.ClassLayout = new ClassLayout(classLayout.PackingSize, classLayout.ClassSize);

            // Unity assemblies sometimes have struct layouts on classes.
            // This gets overlooked on mono but not on coreclr.
            if (!clonedType.IsValueType() && (clonedType.IsExplicitLayout || clonedType.IsSequentialLayout))
            {
                clonedType.IsExplicitLayout = clonedType.IsSequentialLayout = false;
                clonedType.IsAutoLayout = true;
            }

            processedAssembly.RegisterTypeRewrite(new TypeRewriteContext(processedAssembly, null, clonedType));
            processedType = clonedType;
        }

        foreach (var nestedUnityType in unityType.NestedTypes)
            ProcessType(processedAssembly, nestedUnityType, processedType, imports, ref typesUnstripped,
                restoreReferenceOnly);
    }

    private static void RestoreMissingSelfReferences(AssemblyRewriteContext processedAssembly,
        AssemblyDefinition unityAssembly, RuntimeAssemblyReferences imports, ref int typesUnstripped)
    {
        var newModule = processedAssembly.NewAssembly.ManifestModule!;
        var unityTypes = unityAssembly.ManifestModule!.GetAllTypes()
            .ToDictionary(type => type.FullName, StringComparer.Ordinal);

        while (true)
        {
            var existingTypes = new HashSet<string>(
                newModule.GetAllTypes().Select(type => type.FullName),
                StringComparer.Ordinal);
            var missingReferences = GetMissingSelfReferences(
                newModule,
                processedAssembly.NewAssembly.Name!,
                existingTypes);
            var restoredAny = false;

            foreach (var missingReference in missingReferences)
            {
                if (!unityTypes.TryGetValue(missingReference, out var unityType) ||
                    !unityType.IsReferenceType())
                    continue;

                var enclosingNewType = unityType.DeclaringType == null
                    ? null
                    : processedAssembly.TryGetTypeByName(unityType.DeclaringType.FullName)?.NewType;
                if (unityType.DeclaringType != null && enclosingNewType == null)
                    continue;

                ProcessType(processedAssembly, unityType, enclosingNewType, imports, ref typesUnstripped, true);
                var restoredType = processedAssembly.TryGetTypeByName(unityType.FullName)?.NewType;
                if (restoredType != null)
                    RedirectReferences(
                        newModule,
                        processedAssembly.NewAssembly.Name!,
                        missingReference,
                        restoredType);
                restoredAny = true;
            }

            if (!restoredAny)
                return;
        }
    }

    private static void RestoreMissingCrossAssemblyReferences(RewriteGlobalContext context,
        ref int typesUnstripped)
    {
        var unityAssemblies = context.UnityAssemblies.Assemblies.ToDictionary(
            assembly => assembly.Name!.ToString(),
            assembly => assembly,
            StringComparer.Ordinal);

        while (true)
        {
            var restoredAny = false;
            foreach (var sourceAssembly in context.Assemblies.ToArray())
            {
                var sourceModule = sourceAssembly.NewAssembly.ManifestModule!;
                foreach (var (targetAssemblyName, unityAssembly) in unityAssemblies)
                {
                    var targetAssembly = context.TryGetAssemblyByName(targetAssemblyName);
                    if (targetAssembly == null)
                        continue;

                    var existingTargetTypes = new HashSet<string>(
                        targetAssembly.NewAssembly.ManifestModule!.GetAllTypes()
                            .Select(type => type.FullName),
                        StringComparer.Ordinal);
                    var missingReferences = GetMissingSelfReferences(
                        sourceModule,
                        targetAssemblyName,
                        existingTargetTypes);
                    foreach (var missingReference in missingReferences)
                    {
                        var unityType = unityAssembly.ManifestModule!.GetAllTypes()
                            .FirstOrDefault(type => type.FullName == missingReference);
                        if (unityType == null || !unityType.IsReferenceType())
                            continue;

                        var enclosingNewType = unityType.DeclaringType == null
                            ? null
                            : targetAssembly.TryGetTypeByName(unityType.DeclaringType.FullName)?.NewType;
                        if (unityType.DeclaringType != null && enclosingNewType == null)
                            continue;

                        ProcessType(
                            targetAssembly,
                            unityType,
                            enclosingNewType,
                            targetAssembly.Imports,
                            ref typesUnstripped,
                            true);
                        var restoredType = targetAssembly.TryGetTypeByName(unityType.FullName)?.NewType;
                        if (restoredType == null)
                            continue;

                        RedirectReferences(
                            sourceModule,
                            targetAssemblyName,
                            missingReference,
                            sourceModule.DefaultImporter.ImportType(restoredType));
                        restoredAny = true;
                    }
                }
            }

            if (!restoredAny)
                return;
        }
    }

    private static void RedirectReferences(ModuleDefinition module, string assemblyName, string fullName,
        ITypeDefOrRef restoredType)
    {
        foreach (var type in module.GetAllTypes())
        {
            type.BaseType = RewriteReference(type.BaseType, assemblyName, fullName, restoredType);
            foreach (var implementation in type.Interfaces)
                implementation.Interface = RewriteReference(
                    implementation.Interface, assemblyName, fullName, restoredType)!;
            foreach (var field in type.Fields)
                if (field.Signature != null)
                    field.Signature.FieldType = RewriteSignature(
                        field.Signature.FieldType, assemblyName, fullName, restoredType);
            foreach (var parameter in type.GenericParameters)
                foreach (var constraint in parameter.Constraints)
                    constraint.Constraint = RewriteReference(
                        constraint.Constraint, assemblyName, fullName, restoredType)!;
            foreach (var method in type.Methods)
                RewriteMethodSignature(method.Signature, assemblyName, fullName, restoredType);
            foreach (var property in type.Properties)
            {
                if (property.Signature == null)
                    continue;
                property.Signature.ReturnType = RewriteSignature(
                    property.Signature.ReturnType, assemblyName, fullName, restoredType);
                for (var index = 0; index < property.Signature.ParameterTypes.Count; index++)
                    property.Signature.ParameterTypes[index] = RewriteSignature(
                        property.Signature.ParameterTypes[index], assemblyName, fullName, restoredType);
            }
            foreach (var @event in type.Events)
                @event.EventType = RewriteReference(
                    @event.EventType, assemblyName, fullName, restoredType)!;
        }
    }

    private static void RewriteMethodSignature(MethodSignature? signature, string assemblyName,
        string fullName, ITypeDefOrRef restoredType)
    {
        if (signature == null)
            return;

        signature.ReturnType = RewriteSignature(
            signature.ReturnType, assemblyName, fullName, restoredType);
        for (var index = 0; index < signature.ParameterTypes.Count; index++)
            signature.ParameterTypes[index] = RewriteSignature(
                signature.ParameterTypes[index], assemblyName, fullName, restoredType);
    }

    private static ITypeDefOrRef? RewriteReference(ITypeDefOrRef? reference, string assemblyName,
        string fullName, ITypeDefOrRef restoredType)
    {
        if (reference is TypeSpecification { Signature: not null } specification)
            return new TypeSpecification(RewriteSignature(
                specification.Signature, assemblyName, fullName, restoredType));

        return reference is TypeReference typeReference && typeReference.FullName == fullName &&
               ReferencesAssembly(typeReference, assemblyName)
            ? restoredType
            : reference;
    }

    private static TypeSignature RewriteSignature(TypeSignature signature, string assemblyName,
        string fullName, ITypeDefOrRef restoredType)
    {
        switch (signature)
        {
            case TypeDefOrRefSignature typeDefOrRef:
                return typeDefOrRef.Type is TypeReference typeReference &&
                       typeReference.FullName == fullName && ReferencesAssembly(typeReference, assemblyName)
                    ? restoredType.ToTypeSignature()
                    : signature;
            case GenericInstanceTypeSignature genericInstance:
                var genericType = RewriteReference(
                    genericInstance.GenericType, assemblyName, fullName, restoredType)!;
                return new GenericInstanceTypeSignature(
                    genericType,
                    genericInstance.IsValueType(),
                    genericInstance.TypeArguments
                        .Select(argument => RewriteSignature(
                            argument, assemblyName, fullName, restoredType))
                        .ToArray());
            case ByReferenceTypeSignature byReference:
                return new ByReferenceTypeSignature(
                    RewriteSignature(byReference.BaseType, assemblyName, fullName, restoredType));
            case PointerTypeSignature pointer:
                return new PointerTypeSignature(
                    RewriteSignature(pointer.BaseType, assemblyName, fullName, restoredType));
            case SzArrayTypeSignature array:
                return new SzArrayTypeSignature(
                    RewriteSignature(array.BaseType, assemblyName, fullName, restoredType));
            case PinnedTypeSignature pinned:
                return new PinnedTypeSignature(
                    RewriteSignature(pinned.BaseType, assemblyName, fullName, restoredType));
            case BoxedTypeSignature boxed:
                return new BoxedTypeSignature(
                    RewriteSignature(boxed.BaseType, assemblyName, fullName, restoredType));
            case CustomModifierTypeSignature modifier:
                return new CustomModifierTypeSignature(
                    RewriteReference(
                        modifier.ModifierType, assemblyName, fullName, restoredType)!,
                    modifier.IsRequired,
                    RewriteSignature(modifier.BaseType, assemblyName, fullName, restoredType));
            default:
                return signature;
        }
    }

    private static string[] GetMissingSelfReferences(ModuleDefinition module, string assemblyName,
        HashSet<string> existingTypes)
    {
        var missingReferences = new HashSet<string>(StringComparer.Ordinal);

        void AddReference(ITypeDefOrRef? reference)
        {
            if (reference is TypeSpecification specification)
            {
                AddSignature(specification.Signature);
                return;
            }

            if (reference is TypeReference typeReference &&
                ReferencesAssembly(typeReference, assemblyName) &&
                !existingTypes.Contains(typeReference.FullName))
                missingReferences.Add(typeReference.FullName);
        }

        void AddSignature(TypeSignature? signature)
        {
            if (signature == null)
                return;

            AddReference(signature.GetUnderlyingTypeDefOrRef());
            if (signature is GenericInstanceTypeSignature genericInstance)
            {
                foreach (var typeArgument in genericInstance.TypeArguments)
                    AddSignature(typeArgument);
            }
            else if (signature is TypeSpecificationSignature specification)
            {
                AddSignature(specification.BaseType);
            }
        }

        foreach (var type in module.GetAllTypes())
        {
            AddReference(type.BaseType);
            foreach (var implementation in type.Interfaces)
                AddReference(implementation.Interface);
            foreach (var field in type.Fields)
                AddSignature(field.Signature?.FieldType);
            foreach (var method in type.Methods)
            {
                AddSignature(method.Signature?.ReturnType);
                if (method.Signature == null)
                    continue;
                foreach (var parameterType in method.Signature.ParameterTypes)
                    AddSignature(parameterType);
            }
            foreach (var property in type.Properties)
            {
                AddSignature(property.Signature?.ReturnType);
                if (property.Signature == null)
                    continue;
                foreach (var parameterType in property.Signature.ParameterTypes)
                    AddSignature(parameterType);
            }
            foreach (var @event in type.Events)
                AddReference(@event.EventType);
        }

        return missingReferences.ToArray();
    }

    private static bool ReferencesAssembly(TypeReference type, string assemblyName)
    {
        IResolutionScope? scope = type.Scope;
        while (scope is TypeReference declaringType)
            scope = declaringType.Scope;

        return scope is AssemblyReference assemblyReference &&
               string.Equals(assemblyReference.Name, assemblyName, StringComparison.Ordinal);
    }

    private static TypeDefinition CloneEnum(TypeDefinition sourceEnum, RuntimeAssemblyReferences imports)
    {
        var newType = new TypeDefinition(sourceEnum.Namespace, sourceEnum.Name, ForcePublic(sourceEnum.Attributes),
            imports.Module.Enum().ToTypeDefOrRef());
        foreach (var sourceEnumField in sourceEnum.Fields)
        {
            TypeSignature fieldType = sourceEnumField.Name == "value__"
                ? imports.Module.ImportCorlibReference(sourceEnumField.Signature!.FieldType.FullName)
                : newType.ToTypeSignature();
            var newField = new FieldDefinition(sourceEnumField.Name, sourceEnumField.Attributes, new FieldSignature(fieldType));
            newField.Constant = sourceEnumField.Constant;
            newType.Fields.Add(newField);
        }

        return newType;
    }

    private static bool HasNonBlittableFields(TypeDefinition type)
    {
        if (!type.IsValueType()) return false;

        var typeSignature = type.ToTypeSignature();
        foreach (var fieldDefinition in type.Fields)
        {
            if (fieldDefinition.IsStatic || SignatureComparer.Default.Equals(fieldDefinition.Signature?.FieldType, typeSignature))
                continue;

            if (!fieldDefinition.Signature!.FieldType.IsValueType())
                return true;

            if (fieldDefinition.Signature.FieldType.Namespace?.StartsWith("System") ?? false &&
                HasNonBlittableFields(fieldDefinition.Signature.FieldType.Resolve()))
                return true;
        }

        return false;
    }

    private static TypeAttributes ForcePublic(TypeAttributes typeAttributes)
    {
        var visibility = typeAttributes & TypeAttributes.VisibilityMask;
        if (visibility == 0 || visibility == TypeAttributes.Public)
            return typeAttributes | TypeAttributes.Public;

        return (typeAttributes & ~TypeAttributes.VisibilityMask) | TypeAttributes.NestedPublic;
    }
}
