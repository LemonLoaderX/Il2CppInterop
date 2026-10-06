# Android ARM64 patch set

This branch extends upstream commit
`81a6f78c8b653e0da4a3420ac4cd00819e8b6292`. The patches are loader-neutral and
belong in Il2CppInterop because they implement IL2CPP ABI, injection, and
generated-assembly behavior.

## ARM64 ABI

Value arguments and returns follow AAPCS64 classification, including nested
homogeneous floating-point aggregates and indirect returns. Android-only return
adapters do not alter desktop targets. Runtime regression tests cover the
classifications and generated trampoline shapes.

## Injection and native hooks

Android resolves `libil2cpp.so` without desktop x86 scanning, supports legacy
and three-argument generic-method lookup forms, and selects the matching managed
hook for each native signature. Failed native hooks remain failed instead of
publishing partial state. Internal-call lookup also exposes a non-throwing path
for optional Unity functions.

Android hosts must supply `RuntimeConfiguration.GameAssemblyHandle`, borrowed
from the IL2CPP instance initialized by Unity, and bind P/Invoke to that same
instance. Injection export lookup reuses the supplied handle and never frees it.
Reopening `libil2cpp.so` by name can select another linker namespace and crash
inside an uninitialized runtime. A missing Android handle is rejected; desktop
hosts retain their legacy lookup when no handle is supplied. Runtime regressions
verify export lookup through the exact host handle and its ownership contract.

## Generator correctness

Unity unstripping preserves explicit layouts, restored type metadata, and method
local initialization. Parameter copying removes an orphaned `HasDefault` flag
when the input has no Constant row while preserving valid constants and the
independent `Optional` flag. Generation consumes the target game assembly when
native GC write barriers are required. Generator tests must pass before updating
the bundled CLI or runtime assemblies.

Unity 6 managed dependencies embed internal nullable-metadata attributes. The
unstripping passes intentionally omit constructors and reference-type instance
fields, so making those attributes public produced empty `NullableAttribute`
shells that C# selected and then rejected with CS0656. Keep these internal
compiler helpers out of generated assemblies; the generator regression fixture
covers this alongside normal Unity type restoration. Run
`dotnet run --project Il2CppInterop.Generator.Tests/Il2CppInterop.Generator.Tests.csproj --configuration Release`
before pinning the fork in a Patcher release, then regenerate game Interop rather
than editing an existing generated DLL.

Keep future fixes in this repository rather than applying post-build changes to
Il2CppInterop binaries in a loader or APK tool.

Upstream restores Unity 6.4 type names using the same source-name conversion as
rewritten types, including compiler-generated nested value types. The fork keeps
generic/delegate shell restoration and explicit layout metadata while adopting
that naming rule. Harmony support retains the managed wrapper detour supported
by the MonoMod 22 API used by its consumers.

Restored types retain an original Unity full-name lookup alongside the converted
name. Reference repair and later unstripping passes use that source identity;
dropping it duplicates renamed shells and leaves references to nonexistent types.
Regression fixtures serialize and load renamed self, nested and cross-assembly
references, rather than checking only the presence of the converted definition.

Cross-assembly restoration collects signature references once per unchanged source
module and groups them by target assembly. It rebuilds that local collection after
reference repair, preserving restoration order and dependency discovery. Unity
source types use pass-local full-name lookup tables. These indexes are transient;
there is no generated-output cache or additional installed metadata. Compare real
generated assemblies as well as the unstripping fixtures when changing this pass.

## Release workflow

The original `.github/workflows/dotnet.yml` and Cake Pack task own fork releases.
Use `v<version>` for stable releases and `v<version>-alpha.N`, `-beta.N` or `-rc.N`
for test releases. Fork identity belongs in the repository and release notes,
not a mandatory prerelease suffix. For example:

```sh
./build.sh --target=Pack --build_version=1.5.3
```

Pack builds all existing frameworks, including Generator net472, then runs the
Generator and Runtime regression executables. Cross-assembly restoration uses
KeyValuePair properties because .NET Framework lacks its Deconstruct method.
Only the six product components enter ZIPs; test executables and unrelated output
directories are excluded. ZIPs include LICENSE/PATCHES.md. The NuGet bundle contains
only packages for the requested fork version. CI adds SHA256SUMS.

Tag builds create a draft GitHub Release; a nonempty SemVer suffix marks it as
prerelease. Inspect its asset versions, contents,
checksums and CLI startup before publishing the draft. Fork CI never pushes to
the BepInEx or NuGet.org feeds; those original publishing steps are restricted to
the upstream repository. Existing Loader/Patcher pins are updated independently
after consumer verification, not as a side effect of creating a release.
