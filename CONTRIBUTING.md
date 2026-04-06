# Contributing to DevBrain

Welcome! We're thrilled that you're interested in contributing to DevBrain. Whether you're fixing a typo, reporting a bug, or building a major feature, every contribution matters. This guide will help you get started.

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Please report unacceptable behavior to the email listed in the Code of Conduct.

## How to Contribute

There are many ways to contribute to DevBrain:

- **Report bugs** — Found something broken? [Open a bug report](.github/ISSUE_TEMPLATE/bug_report.yml).
- **Suggest features** — Have an idea? Start a [discussion](https://github.com/devbrain-tool/devbrain/discussions).
- **Submit pull requests** — Fix a bug, improve docs, or add a feature.
- **Improve documentation** — Help others understand the project better.
- **Review pull requests** — Provide feedback on open PRs.

## Development Setup

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js 20+](https://nodejs.org/) (for the dashboard)
- Git

### Clone and Build

```bash
git clone https://github.com/devbrain-tool/devbrain.git
cd devbrain
```

**Build the daemon and CLI:**

```bash
dotnet build DevBrain.slnx
```

**Build the dashboard:**

```bash
cd dashboard
npm install
npm run build
```

### Run Tests

```bash
dotnet test DevBrain.slnx
```

**Dashboard tests:**

```bash
cd dashboard
npm test
```

## Project Structure

DevBrain is organized as a .NET solution with a React dashboard. For a detailed architectural overview, see [ARCHITECTURE.md](ARCHITECTURE.md).

A brief overview:

- `src/` — Core daemon, CLI, and library projects
- `dashboard/` — React web dashboard
- `tests/` — Unit and integration tests
- `scripts/` — Build and utility scripts
- `docs/` — Documentation

## Coding Standards

### C#

- Use **records** for immutable data models and DTOs.
- Use **async/await** throughout — avoid `.Result` or `.Wait()`.
- Enable **nullable reference types** — no suppression operators (`!`) without justification.
- Use **file-scoped namespaces** (`namespace Foo;` not `namespace Foo { }`).
- Prefer **pattern matching** and **switch expressions** where they improve clarity.
- Follow the [.NET naming conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions).

### TypeScript

- Use **strict mode** (`"strict": true` in tsconfig).
- Prefer **functional components** with hooks in React.
- Use **named exports** over default exports.
- Define **explicit types** — avoid `any`.

## Commit Messages

We follow [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<optional scope>): <description>

[optional body]

[optional footer(s)]
```

**Types:**

| Type       | Description                          |
|------------|--------------------------------------|
| `feat`     | A new feature                        |
| `fix`      | A bug fix                            |
| `docs`     | Documentation changes                |
| `test`     | Adding or updating tests             |
| `refactor` | Code changes that are not fixes/features |
| `chore`    | Build, CI, tooling changes           |
| `perf`     | Performance improvements             |

**Examples:**

```
feat(capture): add clipboard monitoring to capture pipeline
fix(storage): handle concurrent SQLite writes gracefully
docs: update README with installation instructions
test(agents): add unit tests for Linker agent
```

## Pull Request Process

1. **Branch from `main`** — create a feature branch with a descriptive name (e.g., `feat/clipboard-capture`, `fix/sqlite-lock`).
2. **Write tests** — all new functionality must include tests. Bug fixes should include a regression test.
3. **Update documentation** — if your change affects public APIs, CLI commands, or user-facing behavior, update the relevant docs.
4. **Follow the PR template** — fill out all sections of the [pull request template](.github/PULL_REQUEST_TEMPLATE.md).
5. **Keep PRs focused** — one logical change per PR. Split large changes into smaller, reviewable pieces.
6. **Ensure CI passes** — all checks must be green before merge.

## Reporting Bugs

Use the [bug report issue template](.github/ISSUE_TEMPLATE/bug_report.yml). Please include:

- Steps to reproduce the issue
- Expected vs. actual behavior
- Your environment (OS, .NET version, DevBrain version)
- Relevant logs or error messages

## Suggesting Features

Feature suggestions should be posted as [GitHub Discussions](https://github.com/devbrain-tool/devbrain/discussions) rather than issues. This allows the community to discuss and refine ideas before they become actionable work items.

## First-Time Contributors

New to DevBrain? Look for issues labeled [`good first issue`](https://github.com/devbrain-tool/devbrain/labels/good%20first%20issue). These are specifically curated to be approachable for newcomers and include enough context to get started.

## Getting Help

If you have questions about contributing, using DevBrain, or anything else:

- **Use [GitHub Discussions](https://github.com/devbrain-tool/devbrain/discussions)** — this is the best place for questions and general conversation.
- **Do not open issues for questions** — issues are reserved for actionable bug reports and tracked work.
