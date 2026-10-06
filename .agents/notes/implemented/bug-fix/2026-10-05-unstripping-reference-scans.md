# Agent Note: Avoid repeated cross-assembly signature scans

Status: implemented

## Problem

Type restoration scans every source module's signatures again for every Unity
target assembly, even when that module has no references to the target. It also
linearly searches Unity type definitions for each missing reference. These scans
dominate cross-assembly restoration on large game inputs.

## Decision

The pass collects referenced full names by target assembly once per unchanged
source module. It skips unrelated targets and uses pass-local Unity type lookup
tables. Reference repair invalidates the source collection immediately; subsequent
targets and fixed-point iterations therefore see new or redirected signatures.
The existing target order, source-name identity and restoration rules remain.
The self-reference path uses the same collection routine.

## Alternatives considered

- Keeping the repeated scans needs less temporary memory but scales with both the
  number of source modules and all Unity target modules, repeating signature work.
- Retaining collections throughout generation avoids more scans but becomes stale
  as restored types and signatures change. Invalidation belongs to this pass.
- Caching generated outputs can avoid whole runs but does not improve a fresh
  generation and creates independent identity, storage and maintenance costs.

## Consequences

Temporary sets add memory proportional to one source module's referenced types;
Unity lookup tables live only for the pass. No persistent cache or public API is
added. Fixtures cover renamed self/nested/cross-assembly references, restored
generic/delegate types and a serialized cross-assembly generic dependency chain.
Real-input verification compares assembly sets
and complete PE contents in memory except generation GUIDs and PE timestamps;
it never edits output binaries. Product consumers update their pins separately.

## Prior-note Audit

The existing fork-release process notes govern packaging and version semantics,
not signature restoration. They remain unchanged. PATCHES.md owns the public
generator correctness and verification contract.
