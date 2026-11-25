# Development

The [FormuLang standard](https://github.com/Adisoreq/FormuLang) and this library are developed in parallel. This document describes how to keep them in sync.

## Keeping the library in sync with the standard

### 1. Pin the standard revision

The library always targets one revision of the standard. The revision is recorded in [Conformance](conformance.md) (`Target standard` and `Standard revision checked`). When the standard changes:

1. Read the standard's [CHANGELOG](https://github.com/Adisoreq/FormuLang/blob/main/CHANGELOG.md) and [Core CHANGELOG](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/CHANGELOG.md) since the pinned revision.
2. Add every relevant change to [Conformance](conformance.md) as a deviation (it is a deviation until the library implements it).
3. Update the pinned revision.

This way the conformance document is always an honest list of the differences, even when the library lags behind the standard.

### 2. Copy identifiers, do not invent them

Values defined by the standard are copied from it, never chosen in the library:

- error codes and names ([Core errors](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/errors/README.md)),
- operator spellings, binding powers and associativity ([Operator parsing](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/operator-parsing.md#binding-power)),
- standard identifiers (`core-1.0`), environment constants and variables ([Core](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/README.md)).

If the implementation needs something the standard does not define (for example a new error), propose it in the standard first, then implement it.

### 3. Turn the standard's examples into tests

Most standard documents contain examples with an input and an expected result (the tokenization table in [Tokenization](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/tokenization.md#example), the [Core examples](https://github.com/Adisoreq/FormuLang/tree/main/docs/standards/core/examples), the **Algorithm** and **Errors** sections of each function). They are the conformance test suite:

- one test per example, named after the document it comes from, e.g. `Tokenization_Example_FormatCall`;
- a test that fails because the standard changed is a deviation: add it to [Conformance](conformance.md) instead of changing the expected result.

A discrepancy found while implementing is often a gap in the standard itself (an undefined edge case, two documents that disagree). Fix it in the standard, so every implementation gets the same answer.

### 4. Change workflow

For a change of the language:

1. Change the standard (syntax / semantics document and changelog).
2. Add the example of the change as a test in the library. It fails.
3. Implement the change. The test passes.
4. Update [Conformance](conformance.md) and [CHANGELOG](../CHANGELOG.md).

For a change of the library only (refactoring, performance, API), the standard is not involved.

## Versioning

| | Scheme | Example |
| :-- | :----- | :------ |
| Standard (Core) | `<name>-<major>.<minor>` | `core-1.0` |
| Library | [SemVer](https://semver.org) | `1.0.1` |

The library version is independent of the standard version: a library release can fix bugs without any change of the standard. Every release lists the standards it supports in the [CHANGELOG](../CHANGELOG.md), and `IEnvironment.Standards` reports the same identifiers at runtime.

Since `1.0.0` the public API follows SemVer: a breaking change of the API needs a new major version. A new minor version of a standard (e.g. `core-1.1`) is implemented in a minor version of the library.

The version of all packages is set once, in `<Version>` of [Directory.Build.props](../Directory.Build.props).

## Conformance tests

The standard repository has language-independent test cases in its `tests/` directory (JSON). They are copied to [tests/FormuLang.NET.Conformance.Tests/conformance](../tests/FormuLang.NET.Conformance.Tests/conformance), with the revision in `conformance/REVISION`, and every case is a test of `ConformanceTests`. When the library is synced with a newer revision of the standard, copy them again:

```bash
pwsh scripts/sync-conformance.ps1
```

The script expects a checkout of the standard next to this repository (`../FormuLang`); pass another path with `-Standard`. A case that disagrees with the algorithm of the standard itself is listed in `ConformanceTests.KnownDeviations` (and in [Conformance](conformance.md#known-deviations)) until the standard is fixed.

## Releasing

The packages `FormuLang.NET.Abstractions`, `FormuLang.NET.Core` and `FormuLang.NET.Extensions.Stock` are published on [nuget.org](https://www.nuget.org/packages?q=FormuLang.NET) by the [Release workflow](../.github/workflows/release.yml) when a version tag is pushed. The [CI workflow](../.github/workflows/ci.yml) builds, tests and packs every push and pull request.

One-time setup: create an API key on nuget.org (scope *Push*, glob pattern `FormuLang.NET.*`) and add it to the repository as the secret `NUGET_API_KEY` (*Settings → Secrets and variables → Actions*). The workflow runs in the `nuget` environment, where you can require an approval before publishing.

A release:

1. Set `<Version>` in [Directory.Build.props](../Directory.Build.props) and move the changes from `[Unreleased]` to a new section of the [CHANGELOG](../CHANGELOG.md).
2. Check the packages locally: [scripts/pack.ps1](../scripts/pack.ps1) builds the solution in Release, runs the tests and packs the three packages (`.nupkg` and `.snupkg`) into `artifacts`, like the Release workflow. It warns if the version is already tagged or the working tree has uncommitted changes (`-SkipTests` packs without the tests, `-Output` sets another directory).

   ```bash
   pwsh scripts/pack.ps1
   ```

3. Commit, then tag the commit with the version and push the tag:

   ```bash
   git tag v1.0.1
   ```

   ```bash
   git push origin v1.0.1
   ```

The workflow checks that the tag matches `<Version>`, runs the tests, pushes the packages and their symbols (`.snupkg`) to nuget.org and creates a GitHub release with the packages. A version that is already on nuget.org is skipped; nuget.org does not allow replacing a published version, only unlisting it.

## Extensions

An extension standard (e.g. **Stock**) is implemented in its own project, `FormuLang.NET.Extensions.<Name>`, with its own section in [Conformance](conformance.md). Add the project only when the extension has a first version of its specification in the standard repository; until then keep it in the README as *planned*.

## Building and running

```bash
dotnet build FormuLang.NET.sln
```

```bash
dotnet run --project samples/CalculatorCLI
```

```bash
dotnet run --project samples/WebPreview
```

```bash
dotnet test FormuLang.NET.sln
```
