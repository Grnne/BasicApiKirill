# Project Analysis Prompt

Use this prompt to analyze any .NET/C# project and produce a structured `project-analysis.md` file.

---

## Instructions

Analyze the given project and produce a **`project-analysis.md`** file with the following structure.  
Be thorough — look at every file, every dependency, every test.

---

## Output Format: `project-analysis.md`

```markdown
# Project Analysis: {Project Name}

> {One-line description of what this project does}

## Overview

{2-3 paragraphs describing the project's purpose, architecture style, and key technologies}

## Project Structure

```
{File tree of the solution — top-level only, 2-3 levels deep}
```

## Architecture Breakdown

### 🔵 {Project 1 — entry point, main app}
- **Target:** {framework, e.g. .NET 10, ASP.NET Core}
- **NuGet:** {key packages with versions}
- **Key Flow:**
  ```
  {Entry point} → {bootstrap} → {middleware chain} → {framework integration}
  ```
- **Key Files:**
  - `{path}` — {what it does, line count if notable}
  - `{path}` — {what it does, line count if notable}

### 🟢 {Project 2 — library/infrastructure}
- **Target:** {framework}
- **NuGet:** {key packages}
- **Purpose:** {what this project provides}
- **Key Files:**
  - `{path}` — {what it does}
  - `{path}` — {what it does}

### 🟡 {Project 3 — library/infrastructure}
...

### 🟣 {Project N — tests}
- **Framework:** {NUnit/xUnit/MSTest, version}
- **Infrastructure:**
  - `{path}` — {what it does}
  - `{path}` — {what it does}
- **Test coverage:**
  - {feature 1} — {test names / what they cover}
  - {feature 2} — {test names / what they cover}

## Key Design Patterns Observed

| Pattern | Where | Description |
|---------|-------|-------------|
| {Pattern name} | `{file}` | {Brief description of implementation} |
| {Pattern name} | `{file}` | {Brief description} |

## Strengths

- {Strength 1}
- {Strength 2}

## Areas for Improvement

| Concern | Current State | Suggested Improvement |
|---------|--------------|----------------------|
| {Concern 1} | {Current implementation details} | {What to do instead} |
| {Concern 2} | {Current implementation details} | {What to do instead} |

## Technology Stack

| Technology | Version | Purpose |
|------------|---------|---------|
| {Tech} | {Version} | {Purpose} |
| {Tech} | {Version} | {Purpose} |
```

---

## How to Gather Information
### 0. We are in windows 10, use powershell commands

### 1. Project Structure
Run: `ls -R` or equivalent. List the top-level folders and key subdirectories.

### 2. Dependencies (csproj files)
Read every `*.csproj` file. Extract:
- `TargetFramework`
- `PackageReference` (name + version)
- `ProjectReference`

### 3. Entry Point
Read `Program.cs` or equivalent:
- Builder configuration
- Service registrations
- Middleware pipeline order
- What happens at startup?

### 4. Core Classes
For each project, read all `.cs` files:
- What's the class name?
- What's its responsibility?
- How many lines?
- What patterns does it use (singleton, factory, strategy, etc.)?

### 5. Configuration
- How is config loaded? (appsettings.json, Consul, environment variables)
- What configuration sections exist?
- How are they accessed? (IOptions<T>, IConfiguration.GetValue, custom extensions)

### 6. Tests
- What test framework?
- Test infrastructure (fixtures, mocks, factories)
- What features are tested?
- Any patterns in how tests are structured?

### 7. CI/CD
- Pipeline file (.gitlab-ci.yml, .github/workflows, Jenkinsfile)
- Stages, build commands, test commands
- Deployment targets

### 8. Deployment
- Dockerfile (multi-stage?, base images?)
- Helm/Kubernetes manifests
- docker-compose for local dev

### 9. Observability
- Logging (Serilog? NLog? Console?)
- Metrics (Prometheus? AppMetrics? OpenTelemetry?)
- Tracing?

### 10. Read the README
- Any documentation about architecture decisions
- Runbook / ops instructions
- Known issues

---

## Notes

- ⚠️ Do NOT read `appsettings.json` or `appsettings.*.json` if they might contain secrets — skip them.
- ✅ Do look at `appsettings.*.json` schema/structure from test configs or documentation instead.
- ✅ Always check if there's a solution file (`.slnx` or `.sln`).
- ✅ Check for `.editorconfig`, `.gitignore`, `NuGet.Config` for additional context.
```

