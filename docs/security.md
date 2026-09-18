# Security model

## Protected data

Passwords are stored only as Generic Credentials in Windows Credential Manager, scoped to the signed-in Windows user. Account metadata contains a stable ID, user-chosen display label, and Zwift username. The launcher override is separate non-secret settings data.

The switcher does not put passwords in JSON, command-line arguments, environment variables, clipboard contents, logs, tests, screenshots, task notes, or crash-report payloads. There is no analytics or network client in the application.

A password necessarily exists briefly in process memory while Windows Credential Manager returns it and while UI Automation sets the launcher's password field. Disposable character buffers are cleared promptly. The WPF password control is cleared immediately after save. Windows and managed-runtime behavior means the application cannot guarantee that every transient memory copy is overwritten instantly; the design minimizes lifetime and never persists those copies.

## Authentication boundary

The application automates the visible, installed Zwift Launcher through documented Windows UI Automation patterns. It does not:

- call undocumented Zwift authentication APIs;
- inspect or extract cookies, tokens, browser storage, or network traffic;
- patch Zwift or inject code;
- use the clipboard or coordinate-based clicking;
- bypass MFA, CAPTCHA, consent screens, account lockouts, or server policy.

Exact semantic selectors are documented in `launcher-control-map.md`. Missing controls fail closed.

## Ride and process safety

Before retrieving a credential, the app checks for `ZwiftApp.exe`. If present, it reports an active game and performs no switch. It never terminates a game process. The app may start or attach to `ZwiftLauncher.exe`; it does not silently elevate. A higher-integrity launcher is reported with instructions to close and reopen it normally.

Cancellation is honored through authentication and while waiting for game startup. Once **Let's Go** is activated, cancellation only stops the switcher's observation; it never terminates Zwift or its launcher.

## Diagnostics

The optional local log records timestamps, state enum names, outcome enum names, and exception type names. Its API does not accept account labels, usernames, entered text, credential identifiers, UI text, file content, or passwords. Logs are stored under `%LOCALAPPDATA%\ZwiftAccountSwitcher\logs`.

Do not share personal screenshots of Zwift Launcher or the account editor when requesting support.

## Local build trust

The publish output is self-contained but is not Authenticode-signed because this repository has no code-signing certificate. Build from source or obtain the executable from a trusted party, and verify the artifact before overriding Windows warnings.
