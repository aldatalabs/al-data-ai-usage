# Privacy Policy

AI Usage Monitor for Claude & Codex (`al-data-ai-usage`) runs entirely on your computer.

## What it reads

- The credential file that Claude Code keeps in your user profile, to obtain the access token
  it already holds.
- The session logs that Codex CLI keeps in your user profile, for the last rate-limit snapshot.
- Your local Claude Code and Codex session logs, only when you build the usage report.

## Where data goes

- The Claude access token is sent to Anthropic's usage endpoint over HTTPS, in the same way
  Claude Code itself uses it. That is the only network request the widget makes.
- Nothing is sent to the author or to any other party. There is no telemetry, no analytics,
  no crash reporting and no account.

## What it stores

- Settings, the last successful fetch and the generated report, in your local application
  data folder. Uninstalling the app removes them.

## Contact

Questions go to the issue tracker of the source repository:
<https://github.com/aldatalabs/al-data-ai-usage/issues>. The Feedback menu opens it in your
browser with the app version, Windows version, skin and language filled into the form; you see
them before you submit, and the app itself sends nothing.
