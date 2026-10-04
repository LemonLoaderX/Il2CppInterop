# Agent Note: Downloadable Il2CppInterop fork releases

Status: implemented

## Problem

The upstream workflow publishes NuGet packages before GitHub Release creation.
The Android fork does not own those feeds, has additional regression executables,
and needs downloadable binaries with a distinguishable version. Globally zipping
every output directory also includes local test output.

## Decision

The suffix policy below is superseded by
[stable release versions](2026-10-04-stable-release-versions.md); the packaging
and verification contract remains in effect.

The existing workflow and Cake Pack task remain the release entry point. Fork
tags split an upstream numeric prefix from a lemon suffix. Pack gates the six
product components on Generator/Runtime regressions and includes their license
and integration contract. A separate ZIP supplies exact-version offline NuGet
packages. CI attaches ZIP checksums and creates a draft for final asset inspection.
Upstream feed publishing is limited to the upstream repository.
Optional Cake arguments are omitted when empty: its command parser rejects
`--build_version=` on branch builds and `--build_tag=` on unsuffixed tags.

## Alternatives considered

- Publishing to the original feeds offers normal NuGet discovery, but the fork
  does not own their package identities or publishing credentials.
- A separate release pipeline isolates fork behavior, but duplicates the existing
  tested Cake build/pack entry point and increases maintenance.
- Uploading local outputs is quicker, but bypasses tagged CI provenance and can
  bundle stale test outputs or mismatched versions.

## Consequences

Users can download component ZIPs or install offline packages without building
the source. The CLI remains framework-dependent and needs .NET 6. A lemon suffix
identifies fork package versions; upstream assembly contracts remain intact.
All existing frameworks must compile; the net472 generator avoids unavailable
KeyValuePair deconstruction. Publishing a release does not move consumer pins.
Local verification covers Pack, both regressions, ZIP selection and CLI startup;
release verification checks exact tagged CI assets before draft publication.
