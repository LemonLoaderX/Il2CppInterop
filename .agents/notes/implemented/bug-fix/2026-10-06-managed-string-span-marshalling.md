# Agent Note: Keep Unity string pinning in the managed heap

Status: implemented

## Problem

Unity 6 bindings use `MemoryExtensions.AsSpan(string)`, a pinned reference and
`ManagedSpanWrapper` to pass a managed string's pointer and length to an icall.
Unstripping redirects this local to an IL2CPP ReadOnlySpan proxy class while
retaining value-type receiver addresses. The call also retains a required
`InAttribute` modifier absent from the proxy's generated method. The result is a
MissingMethodException before the native AssetBundle call.

## Decision

The translator recognizes the exact string AsSpan overload and keeps its
ReadOnlySpan<char> locals, GetPinnableReference and Length calls managed. These
references target the generated assembly's .NET runtime contract; MemoryExtensions
uses the System.Memory facade. Original pinned locals and exception cleanup remain
intact. Ordinary generated IL2CPP Span APIs retain their proxy representation.

The managed mapping is scoped to bodies containing this overload. A mapped Span
cannot cross a method signature, field, generic argument or type-token boundary
that would require an IL2CPP proxy. Unsupported mixed bodies use the existing
explicit unstripping failure, rather than emitting incompatible calls.

## Alternatives considered

- Removing the required modifier can resolve the first missing-method error, but
  leaves a local-address receiver targeting a class and moves string pinning into
  another GC heap. It does not preserve the binding's semantics.
- Mapping every Span to managed Span is simpler globally, but breaks native method
  signatures that require IL2CPP objects. Only string binding temporaries are mapped.
- Repairing generated DLLs in Patcher hides the source defect and excludes independent
  generator users. The fix belongs in the generator's translation pass.

## Consequences

Regenerate game Interop to receive this fix; runtime proxy APIs remain unchanged.
The executable fixture serializes and invokes the translated body with null, empty,
Unicode and long strings, forces a compacting GC while pinned, and rejects a proxy
signature escape. Actual Unity AssetBundle calls require device verification.
No output cache, runtime reflection dispatch or new public format is introduced.

## Prior-note Audit

The [reference scan note](2026-10-05-unstripping-reference-scans.md) governs pass-local
type lookup performance and remains independent. Fork release notes govern packaging
and versions. None covers the managed/native string lifetime boundary.
