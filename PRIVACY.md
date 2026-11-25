# Privacy Policy

AI Usage Limits (`al-data-ai-usage`) runs entirely on your computer.

## What it reads

- The credential file that Claude Code keeps in your user profile, to obtain the access token
  it already holds.
- The session logs that Codex CLI keeps in your user profile, for the last rate-limit snapshot.
- Your local Claude Code and Codex session logs, only when you build the usage report.

## Where data goes

- The Claude access token is sent to Anthropic's usage endpoint over HTTPS, in the same way
  Claude Code itself uses it. That is the only network request the widget makes on its own.
- In the Store app, Check for updates asks the Microsoft Store whether a newer version exists,
  when you click it.
- The app sends nothing to the author or to any other party. There is no telemetry, no
  analytics, no crash reporting and no account. Feedback you choose to send is described under
  Contact.

## What it stores

- Settings, the last successful fetch and the generated report, in your local application
  data folder. Uninstalling the app removes them.

## Contact

The Feedback menu offers three ways to reach the author, and the app itself sends nothing in
any of them:

- **The feedback form** at <https://aldatalabs.com/ai-usage-monitor/feedback>, which needs no
  account. The menu opens it in your browser. The link carries the kind of report (bug
  or idea), the app version, Windows version, whether it is the Store app or a build from
  source, the skin and the language; they stay in your browser, shown and editable, and nothing
  is sent until you press Send. The email field is optional and used only to reply. The page is
  served by Cloudflare like the rest of the site, and the message arrives in our mailbox at
  Zoho; see <https://aldatalabs.com/privacy.html>.
- **Email** to <support@aldatalabs.com>. The menu copies the address to the clipboard,
  replacing what was there, and opens your mail program with the app version in the subject and
  the same version and environment lines in the draft; you send it, or not.
- **The issue tracker** of the source repository,
  <https://github.com/aldatalabs/al-data-ai-usage/issues>, which needs a GitHub account and is
  public.
