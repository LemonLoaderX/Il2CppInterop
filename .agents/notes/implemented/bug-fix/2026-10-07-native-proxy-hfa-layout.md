# Agent Note: Classify proxy HFAs from native field metadata

Status: implemented

## Problem

NativeType passes non-blittable IL2CPP value-type proxies to the fixed-size carrier
builder on Android ARM64. The HFA classifier only accepts CLR value types, but
the generator represents generic structs as proxy classes. A native pair of floats
can therefore receive an integer carrier and use the wrong argument/return registers.
Ordinary blittable CLR structs already use their actual signature and are unaffected.

## Decision

Proxy classes deriving from Il2CppSystem.ValueType use their initialized native
class pointer and IL2CPP field APIs. Classification skips static fields, rejects
byrefs/references/enums and recursively flattens native value fields. Each boxed
field offset loses the Il2CppObject header before contributing an unboxed offset.
The existing homogeneous-element, dense-offset, exact-size and four-element checks
select the carrier. Cyclic metadata is rejected instead of recursing indefinitely.

CLR value types retain their reflection-based classifier. The change stays in
Interop; Loader needs no new return-type special cases or generated ABI metadata.

## Alternatives considered

- Accepting CLR proxy fields or properties is small, but those describe wrappers
  and accessors, not the native closed generic layout.
- Generator-emitted ABI metadata avoids runtime field queries, but introduces a
  new producer/consumer contract and regeneration requirement when the initialized
  native class already provides the authoritative offsets and inflated field types.
- Rejecting all generic value types avoids wrong calls but unnecessarily removes
  layouts that can be classified through the existing native API.

## Consequences

Proxy classification requires initialized IL2CPP metadata and adds field queries
when building signatures. Missing metadata fails explicitly. Existing integer and
indirect aggregate handling remains; this does not expand SIMD/vector ABI support.

The native-hfa regression supplies IL2CPP-shaped metadata for closed proxy classes
without CLR instance fields. It covers nested float/double HFAs and negative mixed,
padded, oversized, reference, byref, enum and overlapping layouts. Native C invokes
emitted callbacks that modify every element, checking both argument and return ABI.
Host runs check classification; native ARM64 runs additionally exercise AAPCS64 and
the production NativeType branch. Actual Unity metadata inflation and hook installation
remain application acceptance boundaries, not claims made by the metadata shim.

## Prior-note audit

The active string-span and reference-scan notes are unrelated generator fixes and
remain intact. No prior HFA note exists; PATCHES.md retains the broader ABI contract.
