# Security

## What the widget touches

| What | Access | Why |
|---|---|---|
| `%USERPROFILE%\.claude\.credentials.json` | read-only | the Claude Code OAuth access token and the plan name |
| `https://api.anthropic.com/api/oauth/usage` | HTTPS GET | the only network request the widget makes; the token goes in the `Authorization` header |
| `%USERPROFILE%\.codex\sessions\**\*.jsonl` | read-only | the last rate-limit snapshot written by Codex CLI |
| `%USERPROFILE%\.claude\projects\*\*.jsonl` | read-only | token counts and session titles, only when you build the analytics report |
| `%LOCALAPPDATA%\al-data-ai-usage\` | read/write | `config.json`, `cache.json`, `analytics.html` (the Store app writes the report to its own package folder) |
| `shell:startup` | write | a shortcut, only when you enable autostart in a build from source; the Store app uses a startup task that Windows owns |

The token is never written to disk, logged or sent anywhere except
`api.anthropic.com`. Only a truncated SHA-256 fingerprint is kept in memory, to
notice that Claude Code has refreshed the token. The refresh token is not used.

There is no telemetry. The widget downloads nothing: the Store app is updated by
the Microsoft Store, and "Check for updates" only asks the Store through
Windows. TLS certificates are verified with the system trust store.

`analytics.html` contains your session titles and project folder names. It
stays on your machine; do not share it if those are sensitive.

## Reporting a vulnerability

Please do not open a public issue. Use GitHub's private reporting instead:
the **Security** tab of this repository → **Report a vulnerability**.
