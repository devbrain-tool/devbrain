# Security Policy

## Supported Versions

| Version | Supported          |
|---------|--------------------|
| 1.x     | Yes                |
| < 1.0   | No                 |

## Reporting a Vulnerability

**Do not open a public issue for security vulnerabilities.**

Instead, please [report a vulnerability via GitHub Security Advisories](https://github.com/devbrain-tool/devbrain/security/advisories/new) with:

1. A description of the vulnerability
2. Steps to reproduce
3. Potential impact
4. Suggested fix (if you have one)

### Response Timeline

- **48 hours** — acknowledgment that we received your report
- **7 days** — initial assessment and severity classification
- **30 days** — fix released (for critical/high severity)

We will credit you in the release notes unless you prefer to remain anonymous.

## Security Model

DevBrain is a **local-only** developer tool. Its security model is designed around this constraint:

### What DevBrain Does

- Runs as a daemon on `127.0.0.1` only — no external network exposure
- Stores all data in `~/.devbrain/` with restricted file permissions (`700`)
- Automatically redacts secrets (API keys, tokens, passwords) before storage
- Never sends raw code to cloud LLMs — only compressed summaries
- No telemetry, no analytics, no phone-home behavior

### Threat Model

| Threat | Mitigation |
|---|---|
| Local data theft | File permissions (`700`). Optional encryption at rest (roadmap). |
| API key leakage | Keys loaded from env vars, never stored in config. Capture pipeline auto-redacts key patterns. |
| Cloud LLM data exposure | Only summaries sent to cloud. User controls which tasks use cloud via `settings.toml`. |
| Accidental secret capture | Multi-layer redaction pipeline. `.devbrainignore` for per-project exclusions. |

### What Counts as a Security Issue

- Secret redaction bypass (a secret pattern that isn't caught)
- Data sent to cloud LLM that shouldn't be
- File permission issues allowing unauthorized access
- Arbitrary code execution via crafted input
- Authentication/authorization bypass (future, when team sync ships)

### What Does NOT Count

- Local denial of service (the daemon runs on your own machine)
- Cosmetic issues in the dashboard
- Issues requiring physical access to the machine
- Theoretical attacks that require pre-existing local access

## Security Best Practices for Users

1. Keep DevBrain updated to the latest version
2. Use `.devbrainignore` to exclude sensitive project directories
3. Set `privacy_mode = "strict"` in `settings.toml` for maximum privacy
4. Review `~/.devbrain/settings.toml` — ensure `api_key_env` points to env vars, not raw keys
5. If using cloud LLM, review which agent tasks are configured for cloud in `[llm.cloud].tasks`
