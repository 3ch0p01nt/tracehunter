# TraceHunter Phase 0 — Bootstrap Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Stand up the TraceHunter repo skeleton — solution, projects, CI, baseline docs — so that `dotnet build` and `dotnet test` pass on a fresh clone and CI is green.

**Architecture:** Single .NET 8 solution with 8 source projects and 8 test projects, organized per the design at `docs/plans/2026-04-22-tracehunter-design.md` §11. Project graph: `Core` is the base; `Capture`/`Normalization`/`Enrichment`/`Detection`/`Storage` reference `Core`; `Web` references `Detection`/`Enrichment`/`Storage`/`Core`; `Host` references everything. No business logic in this phase — purely scaffolding.

**Tech Stack:** .NET 8 LTS SDK (latest 8.0.x patch — supports Windows 10 1607+ and all Windows 11; the only current LTS that still runs on LTSC 2016), C# 12, target framework `net8.0-windows`, xUnit, FluentAssertions, bUnit, GitHub Actions on `windows-latest`.

---

## Context for the executor

You're implementing Phase 0 of a project whose full design lives at `docs/plans/2026-04-22-tracehunter-design.md`. Read sections 1, 2, 4, 11, and 12 of that doc before you start so you know what the rest of the project will need from this scaffold. **Do not implement any business logic in this phase** — projects exist only to hold types and references; only placeholder smoke tests are added.

**Working directory:** `C:\Users\RobertSoligan\OneDrive - tasmonk\Coding_projects\ETW_TraceEvent`

**Shell:** Bash (Git for Windows). All paths use forward slashes; quotes around any path containing spaces.

**Commits:** One commit per task. Use Conventional Commits prefixes (`chore:`, `build:`, `ci:`, `docs:`, `test:`).

---

## Task 1: Create global.json pinning the .NET SDK

**Files:**
- Create: `global.json`

**Step 1: Write `global.json`**

```json
{
  "sdk": {
    "version": "8.0.100",
    "rollForward": "latestFeature"
  }
}
```

`rollForward: latestFeature` accepts any installed 8.x.y SDK at or above 8.0.100 — keeps dev/CI in sync without manual version bumps.

**Step 2: Verify**

Run: `dotnet --list-sdks`
Expected: at least one 8.0.x entry. If only newer SDKs are installed (e.g. 9.x or 10.x), install latest .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0 — .NET 8 is required because it's the only current LTS that supports Windows 10 1607 / LTSC 2016.

**Step 3: Commit**

```bash
git add global.json
git commit -m "build: pin .NET 8 SDK via global.json"
```

---

## Task 2: Create Directory.Build.props with shared project settings

**Files:**
- Create: `Directory.Build.props`

**Step 1: Write `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <LangVersion>12</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest</AnalysisLevel>
    <AnalysisMode>Recommended</AnalysisMode>
    <Deterministic>true</Deterministic>
    <Authors>Robert Soligan</Authors>
    <Company>TraceHunter</Company>
    <Product>TraceHunter</Product>
    <Copyright>Copyright (c) 2026 Robert Soligan</Copyright>
    <Version>0.1.0</Version>
    <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
  </PropertyGroup>
</Project>
```

**Step 2: Commit**

```bash
git add Directory.Build.props
git commit -m "build: add Directory.Build.props with shared project settings"
```

---

## Task 3: Create .editorconfig

**Files:**
- Create: `.editorconfig`

**Step 1: Write `.editorconfig`**

```ini
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = crlf
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true

[*.{csproj,props,targets,xml,yml,yaml,json}]
indent_size = 2

[*.md]
trim_trailing_whitespace = false

[*.cs]
csharp_new_line_before_open_brace = all
csharp_new_line_before_else = true
csharp_new_line_before_catch = true
csharp_new_line_before_finally = true
csharp_indent_case_contents = true
csharp_indent_switch_labels = true
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = true:suggestion
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false
```

**Step 2: Commit**

```bash
git add .editorconfig
git commit -m "build: add .editorconfig for C# and config files"
```

---

## Task 4: Create LICENSE (Apache 2.0)

**Files:**
- Create: `LICENSE`

**Step 1: Write `LICENSE`**

Copy the verbatim Apache License 2.0 text from https://www.apache.org/licenses/LICENSE-2.0.txt. Replace the `[yyyy]` placeholder in the appendix with `2026` and `[name of copyright owner]` with `Robert Soligan`. The full text is ~11 KB.

**Step 2: Verify**

Run: `head -1 LICENSE`
Expected: `                                 Apache License`

**Step 3: Commit**

```bash
git add LICENSE
git commit -m "docs: add Apache 2.0 license"
```

---

## Task 5: Create the solution file

**Files:**
- Create: `TraceHunter.sln`

**Step 1: Create**

```bash
dotnet new sln -n TraceHunter
```

**Step 2: Verify**

Run: `ls TraceHunter.sln`
Expected: file exists.

**Step 3: Commit**

```bash
git add TraceHunter.sln
git commit -m "build: create TraceHunter.sln"
```

---

## Task 6: Create the 8 src/ class library projects

**Files:**
- Create: `src/TraceHunter.Core/TraceHunter.Core.csproj`
- Create: `src/TraceHunter.Capture/TraceHunter.Capture.csproj`
- Create: `src/TraceHunter.Normalization/TraceHunter.Normalization.csproj`
- Create: `src/TraceHunter.Enrichment/TraceHunter.Enrichment.csproj`
- Create: `src/TraceHunter.Detection/TraceHunter.Detection.csproj`
- Create: `src/TraceHunter.Storage/TraceHunter.Storage.csproj`
- Create: `src/TraceHunter.Web/TraceHunter.Web.csproj`
- Create: `src/TraceHunter.Host/TraceHunter.Host.csproj`

**Step 1: Create each project**

```bash
dotnet new classlib -n TraceHunter.Core         -o src/TraceHunter.Core
dotnet new classlib -n TraceHunter.Capture      -o src/TraceHunter.Capture
dotnet new classlib -n TraceHunter.Normalization -o src/TraceHunter.Normalization
dotnet new classlib -n TraceHunter.Enrichment   -o src/TraceHunter.Enrichment
dotnet new classlib -n TraceHunter.Detection    -o src/TraceHunter.Detection
dotnet new classlib -n TraceHunter.Storage      -o src/TraceHunter.Storage
dotnet new classlib -n TraceHunter.Web          -o src/TraceHunter.Web
dotnet new console  -n TraceHunter.Host         -o src/TraceHunter.Host
```

**Step 2: Delete the auto-generated `Class1.cs` / `Program.cs` placeholders that have no purpose yet**

```bash
find src -name "Class1.cs" -delete
# Keep Program.cs in Host — but replace contents with a minimal stub
```

**Step 3: Replace `src/TraceHunter.Host/Program.cs` with a stub**

```csharp
namespace TraceHunter.Host;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("TraceHunter (scaffolding stub) — phase 0");
        return 0;
    }
}
```

**Step 4: Add all projects to the solution**

```bash
dotnet sln add src/TraceHunter.Core/TraceHunter.Core.csproj
dotnet sln add src/TraceHunter.Capture/TraceHunter.Capture.csproj
dotnet sln add src/TraceHunter.Normalization/TraceHunter.Normalization.csproj
dotnet sln add src/TraceHunter.Enrichment/TraceHunter.Enrichment.csproj
dotnet sln add src/TraceHunter.Detection/TraceHunter.Detection.csproj
dotnet sln add src/TraceHunter.Storage/TraceHunter.Storage.csproj
dotnet sln add src/TraceHunter.Web/TraceHunter.Web.csproj
dotnet sln add src/TraceHunter.Host/TraceHunter.Host.csproj
```

**Step 5: Verify build**

Run: `dotnet build`
Expected: build succeeds, 8 projects compiled, 0 warnings, 0 errors.

**Step 6: Commit**

```bash
git add src TraceHunter.sln
git commit -m "build: scaffold 8 src/ projects"
```

---

## Task 7: Wire project references

**Files:**
- Modify: each `.csproj` indirectly via `dotnet add reference`

**Reference graph (per design §11):**
- `Core` — depends on nothing
- `Capture` → `Core`
- `Normalization` → `Core`
- `Enrichment` → `Core`, `Normalization`
- `Storage` → `Core`
- `Detection` → `Core`, `Normalization`
- `Web` → `Core`, `Detection`, `Enrichment`, `Storage`
- `Host` → all of the above

**Step 1: Add references**

```bash
dotnet add src/TraceHunter.Capture       reference src/TraceHunter.Core
dotnet add src/TraceHunter.Normalization reference src/TraceHunter.Core
dotnet add src/TraceHunter.Enrichment    reference src/TraceHunter.Core src/TraceHunter.Normalization
dotnet add src/TraceHunter.Storage       reference src/TraceHunter.Core
dotnet add src/TraceHunter.Detection     reference src/TraceHunter.Core src/TraceHunter.Normalization
dotnet add src/TraceHunter.Web           reference src/TraceHunter.Core src/TraceHunter.Detection src/TraceHunter.Enrichment src/TraceHunter.Storage
dotnet add src/TraceHunter.Host          reference src/TraceHunter.Core src/TraceHunter.Capture src/TraceHunter.Normalization src/TraceHunter.Enrichment src/TraceHunter.Detection src/TraceHunter.Storage src/TraceHunter.Web
```

**Step 2: Verify build**

Run: `dotnet build`
Expected: build succeeds, no circular reference errors.

**Step 3: Commit**

```bash
git add src
git commit -m "build: wire project references per design"
```

---

## Task 8: Add NuGet package references

**Files:**
- Modify: each `.csproj` indirectly via `dotnet add package`

**Step 1: Add packages**

```bash
# Capture: ETW session library
dotnet add src/TraceHunter.Capture       package Microsoft.Diagnostics.Tracing.TraceEvent

# Normalization: same library for event types
dotnet add src/TraceHunter.Normalization package Microsoft.Diagnostics.Tracing.TraceEvent

# Storage: SQLite
dotnet add src/TraceHunter.Storage       package Microsoft.Data.Sqlite

# Detection: YAML parser + JSON Schema validator
dotnet add src/TraceHunter.Detection     package YamlDotNet
dotnet add src/TraceHunter.Detection     package JsonSchema.Net

# Web: ASP.NET Core (use FrameworkReference, not a NuGet package)
# We'll edit Web.csproj directly — see Step 2

# Host: hosting, configuration, command-line parsing
dotnet add src/TraceHunter.Host          package Microsoft.Extensions.Hosting
dotnet add src/TraceHunter.Host          package Microsoft.Extensions.Hosting.WindowsServices
dotnet add src/TraceHunter.Host          package System.CommandLine --prerelease
```

**Step 2: Edit `src/TraceHunter.Web/TraceHunter.Web.csproj` to use the ASP.NET Core framework reference and Blazor Server**

Replace the file contents with:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <UseStaticWebAssets>true</UseStaticWebAssets>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\TraceHunter.Core\TraceHunter.Core.csproj" />
    <ProjectReference Include="..\TraceHunter.Detection\TraceHunter.Detection.csproj" />
    <ProjectReference Include="..\TraceHunter.Enrichment\TraceHunter.Enrichment.csproj" />
    <ProjectReference Include="..\TraceHunter.Storage\TraceHunter.Storage.csproj" />
  </ItemGroup>
</Project>
```

(Note: switching SDK from `Microsoft.NET.Sdk` to `Microsoft.NET.Sdk.Web` brings the ASP.NET Core framework reference automatically; no explicit package add needed.)

**Step 3: Verify**

Run: `dotnet restore && dotnet build`
Expected: all packages restore; build succeeds.

**Step 4: Commit**

```bash
git add src
git commit -m "build: add NuGet dependencies (TraceEvent, SQLite, YAML, hosting)"
```

---

## Task 9: Create the 8 test projects

**Files:**
- Create: `tests/TraceHunter.Core.Tests/TraceHunter.Core.Tests.csproj`
- Create: `tests/TraceHunter.Capture.Tests/TraceHunter.Capture.Tests.csproj`
- Create: `tests/TraceHunter.Normalization.Tests/TraceHunter.Normalization.Tests.csproj`
- Create: `tests/TraceHunter.Enrichment.Tests/TraceHunter.Enrichment.Tests.csproj`
- Create: `tests/TraceHunter.Detection.Tests/TraceHunter.Detection.Tests.csproj`
- Create: `tests/TraceHunter.Storage.Tests/TraceHunter.Storage.Tests.csproj`
- Create: `tests/TraceHunter.Web.Tests/TraceHunter.Web.Tests.csproj`
- Create: `tests/TraceHunter.Integration.Tests/TraceHunter.Integration.Tests.csproj`

**Step 1: Create test projects**

```bash
dotnet new xunit -n TraceHunter.Core.Tests          -o tests/TraceHunter.Core.Tests
dotnet new xunit -n TraceHunter.Capture.Tests       -o tests/TraceHunter.Capture.Tests
dotnet new xunit -n TraceHunter.Normalization.Tests -o tests/TraceHunter.Normalization.Tests
dotnet new xunit -n TraceHunter.Enrichment.Tests    -o tests/TraceHunter.Enrichment.Tests
dotnet new xunit -n TraceHunter.Detection.Tests     -o tests/TraceHunter.Detection.Tests
dotnet new xunit -n TraceHunter.Storage.Tests       -o tests/TraceHunter.Storage.Tests
dotnet new xunit -n TraceHunter.Web.Tests           -o tests/TraceHunter.Web.Tests
dotnet new xunit -n TraceHunter.Integration.Tests   -o tests/TraceHunter.Integration.Tests
```

**Step 2: Delete auto-generated placeholder tests**

```bash
find tests -name "UnitTest1.cs" -delete
```

**Step 3: Add test projects to solution**

```bash
dotnet sln add tests/TraceHunter.Core.Tests/TraceHunter.Core.Tests.csproj
dotnet sln add tests/TraceHunter.Capture.Tests/TraceHunter.Capture.Tests.csproj
dotnet sln add tests/TraceHunter.Normalization.Tests/TraceHunter.Normalization.Tests.csproj
dotnet sln add tests/TraceHunter.Enrichment.Tests/TraceHunter.Enrichment.Tests.csproj
dotnet sln add tests/TraceHunter.Detection.Tests/TraceHunter.Detection.Tests.csproj
dotnet sln add tests/TraceHunter.Storage.Tests/TraceHunter.Storage.Tests.csproj
dotnet sln add tests/TraceHunter.Web.Tests/TraceHunter.Web.Tests.csproj
dotnet sln add tests/TraceHunter.Integration.Tests/TraceHunter.Integration.Tests.csproj
```

**Step 4: Wire each test project to its src counterpart**

```bash
dotnet add tests/TraceHunter.Core.Tests          reference src/TraceHunter.Core
dotnet add tests/TraceHunter.Capture.Tests       reference src/TraceHunter.Capture
dotnet add tests/TraceHunter.Normalization.Tests reference src/TraceHunter.Normalization
dotnet add tests/TraceHunter.Enrichment.Tests    reference src/TraceHunter.Enrichment
dotnet add tests/TraceHunter.Detection.Tests     reference src/TraceHunter.Detection
dotnet add tests/TraceHunter.Storage.Tests       reference src/TraceHunter.Storage
dotnet add tests/TraceHunter.Web.Tests           reference src/TraceHunter.Web
dotnet add tests/TraceHunter.Integration.Tests   reference src/TraceHunter.Host
```

**Step 5: Add FluentAssertions to every test project; bUnit to Web.Tests**

```bash
for proj in tests/TraceHunter.*.Tests; do
  dotnet add "$proj" package FluentAssertions
done
dotnet add tests/TraceHunter.Web.Tests package bunit
```

**Step 6: Verify build**

Run: `dotnet build`
Expected: solution builds, no errors.

**Step 7: Commit**

```bash
git add tests src TraceHunter.sln
git commit -m "test: scaffold 8 test projects with FluentAssertions and bUnit"
```

---

## Task 10: Add a placeholder smoke test in each test project

This proves the test runner is wired up before any real tests exist.

**Files:**
- Create: `tests/TraceHunter.Core.Tests/SmokeTests.cs`
- Create: `tests/TraceHunter.Capture.Tests/SmokeTests.cs`
- Create: `tests/TraceHunter.Normalization.Tests/SmokeTests.cs`
- Create: `tests/TraceHunter.Enrichment.Tests/SmokeTests.cs`
- Create: `tests/TraceHunter.Detection.Tests/SmokeTests.cs`
- Create: `tests/TraceHunter.Storage.Tests/SmokeTests.cs`
- Create: `tests/TraceHunter.Web.Tests/SmokeTests.cs`
- Create: `tests/TraceHunter.Integration.Tests/SmokeTests.cs`

**Step 1: Write the same smoke test in each (replace `<Name>` per project)**

```csharp
namespace TraceHunter.<Name>.Tests;

public class SmokeTests
{
    [Fact]
    public void TestRunnerIsAlive()
    {
        // This test exists to verify the test discovery + runner is wired.
        // Real tests replace it as logic lands in subsequent phases.
        true.Should().BeTrue();
    }
}
```

For example, `tests/TraceHunter.Core.Tests/SmokeTests.cs`:
```csharp
namespace TraceHunter.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void TestRunnerIsAlive()
    {
        true.Should().BeTrue();
    }
}
```

Add `using FluentAssertions;` and `using Xunit;` only if implicit usings don't pick them up — verify after writing.

**Step 2: Run all tests**

Run: `dotnet test`
Expected: 8 tests pass, 0 fail, 0 skipped.

**Step 3: Commit**

```bash
git add tests
git commit -m "test: add SmokeTests proving test runner wiring per project"
```

---

## Task 11: Create the GitHub Actions CI workflow

**Files:**
- Create: `.github/workflows/ci.yml`

**Step 1: Write `.github/workflows/ci.yml`**

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build-and-test:
    runs-on: windows-latest
    timeout-minutes: 15
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Cache NuGet packages
        uses: actions/cache@v4
        with:
          path: ~/.nuget/packages
          key: nuget-${{ runner.os }}-${{ hashFiles('**/*.csproj', 'Directory.Build.props') }}
          restore-keys: nuget-${{ runner.os }}-

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --configuration Release --no-restore

      - name: Test
        run: dotnet test --configuration Release --no-build --logger "trx;LogFileName=test-results.trx" --collect:"XPlat Code Coverage"

      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: '**/test-results.trx'
```

**Step 2: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add build and test workflow for windows-latest"
```

---

## Task 12: Create the CodeQL security workflow

**Files:**
- Create: `.github/workflows/codeql.yml`

**Step 1: Write `.github/workflows/codeql.yml`**

```yaml
name: CodeQL

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]
  schedule:
    - cron: '0 6 * * 1'

jobs:
  analyze:
    runs-on: windows-latest
    timeout-minutes: 30
    permissions:
      actions: read
      contents: read
      security-events: write
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Initialize CodeQL
        uses: github/codeql-action/init@v3
        with:
          languages: csharp

      - name: Build
        run: dotnet build --configuration Release

      - name: Perform CodeQL Analysis
        uses: github/codeql-action/analyze@v3
```

**Step 2: Commit**

```bash
git add .github/workflows/codeql.yml
git commit -m "ci: add CodeQL security analysis workflow"
```

---

## Task 13: Create dependabot config

**Files:**
- Create: `.github/dependabot.yml`

**Step 1: Write `.github/dependabot.yml`**

```yaml
version: 2
updates:
  - package-ecosystem: "nuget"
    directory: "/"
    schedule:
      interval: "weekly"
    open-pull-requests-limit: 10
    groups:
      microsoft-extensions:
        patterns: ["Microsoft.Extensions.*"]
      aspnetcore:
        patterns: ["Microsoft.AspNetCore.*"]
      test-deps:
        patterns: ["xunit*", "Microsoft.NET.Test.Sdk", "FluentAssertions", "bunit"]
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
```

**Step 2: Commit**

```bash
git add .github/dependabot.yml
git commit -m "ci: add Dependabot config for NuGet and Actions"
```

---

## Task 14: Create issue and PR templates

**Files:**
- Create: `.github/ISSUE_TEMPLATE/bug.yml`
- Create: `.github/ISSUE_TEMPLATE/detection-request.yml`
- Create: `.github/ISSUE_TEMPLATE/feature.yml`
- Create: `.github/ISSUE_TEMPLATE/config.yml`
- Create: `.github/PULL_REQUEST_TEMPLATE.md`

**Step 1: `.github/ISSUE_TEMPLATE/config.yml`**

```yaml
blank_issues_enabled: false
```

**Step 2: `.github/ISSUE_TEMPLATE/bug.yml`**

```yaml
name: Bug report
description: Something is wrong with TraceHunter
labels: [bug]
body:
  - type: textarea
    id: what-happened
    attributes:
      label: What happened?
      description: A clear description of the bug.
    validations:
      required: true
  - type: textarea
    id: reproduction
    attributes:
      label: Reproduction steps
      description: Steps to reproduce.
    validations:
      required: true
  - type: textarea
    id: expected
    attributes:
      label: Expected behavior
    validations:
      required: true
  - type: input
    id: version
    attributes:
      label: TraceHunter version
    validations:
      required: true
  - type: input
    id: windows-version
    attributes:
      label: Windows version
    validations:
      required: true
  - type: textarea
    id: logs
    attributes:
      label: Relevant logs
      render: shell
```

**Step 3: `.github/ISSUE_TEMPLATE/detection-request.yml`**

```yaml
name: Detection rule request
description: Request a new built-in detection rule
labels: [detection]
body:
  - type: textarea
    id: technique
    attributes:
      label: What technique should this detect?
      description: MITRE ATT&CK technique ID and a one-line description.
    validations:
      required: true
  - type: textarea
    id: telemetry
    attributes:
      label: What ETW events would catch it?
      description: Which providers and event types.
    validations:
      required: true
  - type: textarea
    id: references
    attributes:
      label: References
      description: Links to threat reports, existing Sigma rules, or sample artifacts.
```

**Step 4: `.github/ISSUE_TEMPLATE/feature.yml`**

```yaml
name: Feature request
description: Request a non-detection feature
labels: [enhancement]
body:
  - type: textarea
    id: problem
    attributes:
      label: Problem
      description: What problem does this solve?
    validations:
      required: true
  - type: textarea
    id: proposal
    attributes:
      label: Proposed solution
    validations:
      required: true
  - type: textarea
    id: alternatives
    attributes:
      label: Alternatives considered
```

**Step 5: `.github/PULL_REQUEST_TEMPLATE.md`**

```markdown
## What

<!-- Describe the change in 1-2 sentences. -->

## Why

<!-- Link to issue / design doc / threat report. -->

## Testing

- [ ] Unit tests added/updated
- [ ] Integration tests pass locally
- [ ] Manual smoke test (`scripts/smoke.ps1`) passes — once that lands

## Checklist

- [ ] Code follows the conventions in `.editorconfig`
- [ ] No warnings introduced (`TreatWarningsAsErrors=true`)
- [ ] Public API changes documented in `docs/`
- [ ] If this adds a detection rule: tagged with MITRE technique IDs and has paired test fixtures
```

**Step 6: Commit**

```bash
git add .github/ISSUE_TEMPLATE .github/PULL_REQUEST_TEMPLATE.md
git commit -m "docs: add issue templates and PR template"
```

---

## Task 15: Create baseline README.md

**Files:**
- Create: `README.md`

**Step 1: Write `README.md`**

```markdown
# TraceHunter

> Open-source .NET-native EDR for Windows. Programmable ETW threat hunting in a single self-contained executable.

**Status:** Phase 0 — bootstrap. Not yet functional. See [the design doc](docs/plans/2026-04-22-tracehunter-design.md) for the full v1.0 plan.

## What it is

TraceHunter turns Event Tracing for Windows (ETW) into a real-time threat-hunting platform. It captures kernel and user-mode events from seven providers, normalizes them, evaluates them against Sigma-format and native YAML rules, maintains a live process provenance graph, and exposes everything through an embedded local web UI — all in one `tracehunter.exe`.

## Why not just use Sysmon

- Sysmon's XML config is painful; TraceHunter speaks Sigma and a clean YAML DSL.
- Sysmon has no .NET runtime visibility; TraceHunter does (`Microsoft-Windows-DotNETRuntime`).
- Sysmon needs a driver; TraceHunter doesn't.
- Sysmon emits to the event log and stops there; TraceHunter ships rules, detections, and a UI in the box.

## Quickstart

> Coming in Phase 8. For now, this repo only builds and tests.

```
dotnet build
dotnet test
```

## Architecture

See [`docs/plans/2026-04-22-tracehunter-design.md`](docs/plans/2026-04-22-tracehunter-design.md) for the full design and [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the distilled overview (lands in Task 17).

## License

Apache 2.0 — see [LICENSE](LICENSE).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).
```

**Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add baseline README with status and pointers"
```

---

## Task 16: Create CONTRIBUTING.md, SECURITY.md, CODE_OF_CONDUCT.md

**Files:**
- Create: `CONTRIBUTING.md`
- Create: `SECURITY.md`
- Create: `CODE_OF_CONDUCT.md`

**Step 1: `CONTRIBUTING.md`**

```markdown
# Contributing to TraceHunter

Thanks for your interest. TraceHunter is in early-stage development; the design is locked but most code hasn't been written yet. The best contributions right now are detection rules, bug reports against the scaffolding, and feedback on the design doc.

## Before you open a PR

1. Read [`docs/plans/2026-04-22-tracehunter-design.md`](docs/plans/2026-04-22-tracehunter-design.md) — it captures every architectural decision and the v1.0 phase plan.
2. Check open issues — a maintainer may already be working on the area.
3. For non-trivial changes, open an issue first to discuss.

## Development setup

```
git clone <this repo>
cd tracehunter
dotnet build
dotnet test
```

Required: .NET 8 SDK (pinned via `global.json`), Windows 10/11 x64 or ARM64.

## Conventions

- All public APIs nullable-annotated.
- Warnings are errors (`TreatWarningsAsErrors=true`).
- Conventional Commits for messages (`feat:`, `fix:`, `docs:`, `test:`, `build:`, `ci:`, `chore:`).
- One logical change per commit; one logical change per PR.
- New detection rules require: a MITRE technique tag, a paired `*.events.json` test fixture, and a paired `*.expected.json` expected outcome.

## Code review

A maintainer reviews every PR. Expect questions; we want long-term contributors, not just merged PRs.
```

**Step 2: `SECURITY.md`**

```markdown
# Security Policy

## Reporting a vulnerability

Please **do not** open a public issue for security vulnerabilities.

Email: `rsoligan@gmail.com` with the subject line `TraceHunter security: <short title>`. Include reproduction steps, impact assessment, and suggested mitigation if you have one.

You'll get an acknowledgement within 72 hours and a status update within 14 days.

## Threat model (summary)

TraceHunter runs with elevated privileges to access kernel ETW providers. Its threat surface includes:

- Local code execution by an attacker with admin rights (out of scope — they already win).
- Malicious detection rules (in scope — rule loader will be hardened against directory-traversal and resource-exhaustion in v1.0).
- Web UI bound to `127.0.0.1` only (in scope — must never bind to a routable interface without explicit operator opt-in and authentication).
- Persistence of sensitive data (in scope — `data.db` may contain command lines and script blocks; documented in OPERATIONS.md).

Full threat model lands in `docs/SECURITY.md` in Phase 10.
```

**Step 3: `CODE_OF_CONDUCT.md`**

Use the [Contributor Covenant 2.1](https://www.contributor-covenant.org/version/2/1/code_of_conduct.txt) verbatim, with the contact line set to `rsoligan@gmail.com`.

**Step 4: Commit**

```bash
git add CONTRIBUTING.md SECURITY.md CODE_OF_CONDUCT.md
git commit -m "docs: add CONTRIBUTING, SECURITY, and CODE_OF_CONDUCT"
```

---

## Task 17: Create docs/ scaffolding

**Files:**
- Create: `docs/ARCHITECTURE.md`
- Create: `docs/RULE-AUTHORING.md`
- Create: `docs/OPERATIONS.md`

These are stubs that point at the design doc. They'll grow as features land.

**Step 1: `docs/ARCHITECTURE.md`**

```markdown
# TraceHunter Architecture

This document is a placeholder. The full architecture is defined in [`plans/2026-04-22-tracehunter-design.md`](plans/2026-04-22-tracehunter-design.md), specifically:

- §3 — High-level architecture diagram
- §4 — Component descriptions
- §5 — Data flow

A distilled architecture overview will replace this stub during Phase 10 (launch polish), once the implementation has settled and we know what's worth highlighting vs. what's incidental.
```

**Step 2: `docs/RULE-AUTHORING.md`**

```markdown
# Authoring TraceHunter Detection Rules

This document is a placeholder. Rule format design is defined in [`plans/2026-04-22-tracehunter-design.md`](plans/2026-04-22-tracehunter-design.md) §4.6.

Once the rule engine ships in Phase 5, this document will cover:

- Sigma YAML rules — supported logsources, modifiers, and TraceHunter mappings
- Native YAML DSL — full grammar reference with examples
- Testing rules — paired `*.events.json` / `*.expected.json` fixtures
- Loading and hot-reloading rules
- Best practices for low false-positive detections
```

**Step 3: `docs/OPERATIONS.md`**

```markdown
# Operating TraceHunter

This document is a placeholder. Operational details are defined in [`plans/2026-04-22-tracehunter-design.md`](plans/2026-04-22-tracehunter-design.md), specifically:

- §6 — Performance budget and tuning knobs
- §7 — Resilience model and what happens when things go wrong
- §9 — Distribution and install paths

Full operations guide lands in Phase 10 (launch polish), covering install, configuration, sink integration, and troubleshooting.
```

**Step 4: Commit**

```bash
git add docs/ARCHITECTURE.md docs/RULE-AUTHORING.md docs/OPERATIONS.md
git commit -m "docs: add architecture, rule-authoring, and operations stubs"
```

---

## Task 18: Create samples/ scaffolding

**Files:**
- Create: `samples/rules/.gitkeep`
- Create: `samples/scripts/.gitkeep`
- Create: `samples/README.md`

**Step 1: `samples/README.md`**

```markdown
# TraceHunter samples

Contents:

- `rules/` — bundled detection rules (Sigma format and native YAML DSL). Populated in Phase 9.
- `scripts/` — demo and smoke-test scripts. Populated in Phase 9 and 10.

Rules in this directory are loaded by default when TraceHunter starts. Add custom rules to `%PROGRAMDATA%\TraceHunter\rules\` (service mode) or `%LOCALAPPDATA%\TraceHunter\rules\` (interactive mode) — those locations are scanned in addition to the bundled set.
```

**Step 2: Create `.gitkeep` placeholders**

```bash
touch samples/rules/.gitkeep
touch samples/scripts/.gitkeep
```

**Step 3: Commit**

```bash
git add samples
git commit -m "docs: scaffold samples/ directory structure"
```

---

## Task 19: Final verification — full build + test + CI green

**Step 1: Clean build**

```bash
dotnet clean
dotnet build --configuration Release
```
Expected: build succeeds, 16 projects compiled (8 src + 8 tests), 0 warnings, 0 errors.

**Step 2: Run all tests**

```bash
dotnet test --configuration Release --no-build
```
Expected: 8 tests pass (the smoke test in each test project), 0 fail.

**Step 3: Confirm git tree is clean**

Run: `git status`
Expected: `nothing to commit, working tree clean`.

**Step 4: Confirm commit log is sensible**

Run: `git log --oneline`
Expected: ~19 commits all with Conventional Commits prefixes, walking from initial design doc through scaffolding to docs.

**Step 5: Push to origin once GitHub remote is created**

User action required (not part of execution): create the `tracehunter` repo on GitHub, then:

```bash
git remote add origin git@github.com:<user>/tracehunter.git
git push -u origin main
```

Once pushed, CI runs automatically. Verify the CI workflow goes green on GitHub Actions.

---

## Phase 0 — Done criteria

- `dotnet build` passes on a fresh clone (verified Task 19 Step 1).
- `dotnet test` passes (verified Task 19 Step 2).
- CI workflow is green on GitHub Actions (verified after push in Task 19 Step 5).
- 8 source projects + 8 test projects exist with the reference graph from design §11.
- Baseline docs are in place (README, CONTRIBUTING, SECURITY, CODE_OF_CONDUCT, ARCHITECTURE/RULE-AUTHORING/OPERATIONS stubs).
- Samples scaffolding exists.
- Issue and PR templates exist.
- Dependabot configured.

When all green, the next plan is **Phase 1 — Capture foundation** (kernel + user ETW session hosts, raw event channel, graceful privilege fallback). That plan gets written when this one ships, against the codebase as it actually exists at that point.
