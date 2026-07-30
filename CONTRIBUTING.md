# Contributing to Bridge

Thank you for your interest in contributing to Bridge.

---

## Standards We Follow

These are **mandatory practices** for all contributions.

### Documentation

- All public APIs should be documented in code
- Update relevant documentation when changing functionality
- Fix any documentation inconsistencies you encounter
- Keep the CHANGELOG updated for user-facing changes

### Testing

- All new functionality should include tests
- Bug fixes should include a test that would have caught the bug
- Run `dotnet test` before submitting a PR
- Tests must pass in CI

### Error Handling

- Never swallow exceptions silently (no empty catch blocks)
- Always log errors with `ILogger` — no `Debug.WriteLine` (para excepción en proyectos chicos, ver nota en Logging)
- User-facing errors should notify the user appropriately
- Use custom exceptions for domain-specific errors

### Logging

- Use `ILogger<T>` in all services and ViewModels
- Log at appropriate levels:
  - `LogInformation` — normal operations
  - `LogWarning` — recoverable issues
  - `LogError` — failures
- Include relevant context in log messages (e.g., IDs, names)

> **Nota para proyectos chicos:** Si el proyecto es muy pequeño o de un solo desarrollador, `Debug.WriteLine` es aceptable como decisión consciente — pero documentalo explícitamente si lo elegís, que no sea por descuido. El estándar por defecto sigue siendo `ILogger<T>`.

### Async Patterns

- Always use `async/await` — never `.Result` or `.Wait()`
- Pass `CancellationToken` to cancellable operations

### Security

- Never commit secrets, API keys, or credentials
- Use environment variables for sensitive configuration
- Follow secure coding practices
- Enable and address security warnings

### Naming Conventions

| Scope | Convention | Example |
|-------|-----------|---------|
| Public methods, properties, classes, events | PascalCase | `GetUserById`, `UserName` |
| Private fields (instance and static) | `_camelCase` | `_logger`, `_context`, `_defaultConfig` |
| Local variables, parameters, method args | `camelCase` | `userName`, `itemId` |
| Async methods | PascalCase + `Async` suffix | `GetDataAsync`, `SaveAsync` |
| Interfaces | PascalCase + `I` prefix | `IUserService`, `IRepository` |
| Constants | PascalCase | `AppName`, `DefaultTimeout` |
| Boolean members | `Is`/`Has`/`Can` prefix | `IsEnabled`, `HasItems` |
| Test methods | `MethodName_Scenario_ExpectedResult` | `GetUser_WhenNotFound_ReturnsNull` |

---

## Optional / Conditional Practices

These practices are **recommended** under certain conditions. Apply them when relevant.

| Practice | Activation Condition |
|---|---|
| **Mocking framework** (e.g., Moq) | If services have external dependencies that need mocking in tests |
| **Pre-commit hooks** | Only if working in a team |
| **Code style analyzers** (e.g., StyleCop, Roslyn analyzers) | Only if working in a team or if strict style consistency is desired |
| **Structured logging** | Recommended for most projects; consider if logs need machine-parseable output |
| **Rate limiting on API calls** | Only if the project consumes an external API with rate limits |
| **`IProgress<T>` for long-running operations** | If there are operations that benefit from progress reporting |
| **`packages.lock.json` commit** | Only if working in a team or if deterministic restores are needed |
| **Code signing** | Future consideration; no free solution available today |
| **Accessibility** | If the project targets a broad audience; at minimum ensure keyboard navigation, focus indicators, and screen reader labels on controls |
| **Separate branch for major redesigns** | Only if the change is a new major version that risks destabilizing main |

---

## Workflow

### 1. Discuss Before Coding

For significant changes, open an issue first to discuss the approach.

### 2. Create a Branch

```bash
git checkout -b feat/your-feature-name
# or
git checkout -b fix/bug-description
```

### 3. Make Your Changes

- Write code following the standards above
- Add/update tests as needed
- Update documentation if required

### 4. Commit

Use conventional commit format:

```bash
git commit -m "feat: add new feature"
# or
git commit -m "fix: resolve issue"
```

### 5. Run Checks Locally

```bash
dotnet build -c Release
dotnet test -c Release
```

### 6. Submit a Pull Request

- Fill in the PR template
- Link any related issues
- Wait for review

### 7. Address Feedback

- Make requested changes
- Re-run checks
- Push to your branch

---

## Common Contribution Types

### Bug Fixes

1. Write a test that reproduces the bug
2. Fix the bug
3. Ensure the test passes
4. Update CHANGELOG if user-facing

### New Features

1. Discuss the feature in an issue
2. Implement with tests
3. Update documentation
4. Add entry to CHANGELOG

### Refactoring

1. Ensure tests cover the code being refactored
2. Make small, incremental changes
3. Don't mix refactoring with behavior changes

### Documentation

1. Fix typos or unclear explanations
2. Add missing information
3. Keep code examples accurate

---

## Questions?

Open an issue for clarification. I'll respond as soon as possible.

---

*By contributing, you agree to follow these guidelines.*
