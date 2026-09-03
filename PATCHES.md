# Android ARM64 patch set

This branch extends upstream commit
`f03c8f4ae507d47ea814f3d11d1ec6b0391c1576`. The patches are loader-neutral and
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

## Generator correctness

Unity unstripping preserves explicit layouts, restored type metadata, and method
local initialization. Parameter copying removes an orphaned `HasDefault` flag
when the input has no Constant row while preserving valid constants and the
independent `Optional` flag. Generation consumes the target game assembly when
native GC write barriers are required. Generator tests must pass before updating
the bundled CLI or runtime assemblies.

Keep future fixes in this repository rather than applying post-build changes to
Il2CppInterop binaries in a loader or APK tool.
