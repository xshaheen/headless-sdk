<!-- Title: type(scope)!: the outcome, not the mechanism. The ! marks a breaking change for consumers.
     Write in plain words: no filler, no internal jargon, no plan, review, or finding IDs.
     Budget: everything outside <details> stays under ~400 words, ~700 for a large PR. A reviewer reads the visible
     text; anything that only backs up a claim goes in a collapsed block. Mechanical changes (version pin bumps,
     the regenerated root .editorconfig, renames) belong in their own PR, stacked under the behavior change.
     This repository is public: name no private repository, client app, or local path. Write "a consumer app". -->

<!-- TL;DR, at most 35 words: what a consumer build does differently now, and what was wrong before. -->

**Review:** <!-- ~N min · risk low, medium, or high · authored by a human, by an agent and read by a human, or by an
agent only; name what a human read line by line -->

## Review guide

<!-- The questions you want the reviewer to answer, most consequential first, each linked to the hunk that carries
     it (Files changed, line number, Copy permalink). Ask for a decision, not a reading order: "Should the test
     satellite keep CA2007 at warning?", not "start in Headless.NET.Sdk.Tests.editorconfig". Three to six rows. -->

| Decide | Where |
| --- | --- |
| | |

**Skim:** <!-- what is mechanical and why it is safe to skim, with file counts. Delete if nothing. -->

**Least sure about:** <!-- the consumption mode, satellite, or platform the author would check first if this broke. -->

## Problem

<!-- What a consumer build did wrong or lacked: the project type, the consumption mode (PackageReference or MSBuild
     SDK), the property or analyzer, and the wrong result. -->

> **Rollout risk:** <!-- what changes in consumer builds after the next release and pin bump, and how a consumer
> backs out (pin the previous version, a documented opt-out). Write "none" if nothing can. -->

Fixes #

## Solution

<!-- One sentence on what is different now. Then one row per behavior a consumer build can observe, each backed by
     a consumer-build test. Add a mermaid diagram only when the import order of props and targets changes. -->

| Situation | Before | After |
| --- | --- | --- |

## Decisions

<!-- Delete if none. One line each: the choice, and the alternative you rejected with the reason. -->

## Contract changes

<!-- Delete if none. One row per surface a consumer depends on: an MSBuild property or its default, an analyzer
     severity, a banned symbol, a scaffold file, a package ID, a supported target framework. Cross-check the README's
     Support contract section. Under the table, show each changed shipped file's surface as a `diff` block of the bare
     lines a consumer sees (property, severity, or banned-symbol lines), no surrounding XML. -->

| Surface | Change | Who breaks, and the migration |
| --- | --- | --- |

## Verification

<!-- The state on the head commit only; earlier commits and reruns go in Branch history. Condense each gate run to
     one row: verdict, tests passed/failed/skipped (with skip reasons), compiler and analyzer findings. Add a stage
     row only for a failure or warning, quoting its first line. Never paste summary.md. -->

| Check | Result on `<head commit>` |
| --- | --- |
| `make verify-affected` | |

- **Regression:** <!-- the behavior the new test holds, and for a fix the `make prove TEST=...` table: fails on
  base, passes on head -->
- **Consumption modes:** <!-- which of PackageReference, MSBuild SDK, single- and multi-targeting, and which
  satellites the tests cover for this change -->
- **Not verified:** <!-- each claim no check covers, and why (macOS smoke runs only in Publish). Delete if none. -->

<details><summary>Branch history</summary>

<!-- Delete if the branch has one commit worth describing. What each later commit added, and the checks run on it. -->

</details>

## Left out

<!-- Delete if none. Related problems you saw and deliberately left, so a reviewer doesn't ask. -->
