# Agent Note: Stable release versions

Status: implemented

## Problem

The [initial fork release policy](2026-10-04-fork-releases.md) uses a lemon
suffix for fork identity. SemVer and NuGet interpret every such version as
prerelease, even when GitHub's prerelease checkbox is cleared.

## Decision

Stable releases use plain versions such as 1.5.3. Test releases use alpha, beta
or rc suffixes. GitHub's prerelease flag follows the presence of a SemVer suffix.
The repository, release title and notes identify the fork. The existing tagged
build, regression gates and asset inspection remain unchanged.

## Alternatives considered

- Clearing only GitHub's prerelease flag preserves the existing assets, but
  leaves their embedded NuGet versions classified as prerelease.
- A mandatory fork suffix distinguishes same-name packages, but incorrectly
  ties source identity to release maturity.

## Consequences

Stable ZIPs and offline packages must be rebuilt without a suffix. Fork packages
retain upstream IDs, so consumers must choose their source deliberately; the
fork workflow does not publish to upstream feeds. A future public NuGet feed
requires a separate package-identity decision. Existing consumer source pins
are not moved by release publication.
