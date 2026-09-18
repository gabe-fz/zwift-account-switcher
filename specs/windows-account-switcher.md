# Windows Zwift account switcher

## Outcome
A Windows user can securely save multiple separate Zwift accounts and press one clearly labeled button to switch the Zwift Launcher to the selected account, sign in, and launch Zwift.

## Desired behavior
- The application lists saved accounts by a user-chosen display name and provides add, edit, delete, and **Ride as…** actions.
- Each account has its own Zwift username and password, but only non-secret metadata is stored in application files.
- Pressing **Ride as…** starts one bounded login operation: detect Zwift, open the installed launcher, choose **Change User** when needed, enter the selected credentials, disable **Remember me**, submit login, activate **Let's Go**, and confirm that `ZwiftApp.exe` starts.
- The user sees the current step, can cancel before game launch, and receives an actionable error if launcher UI, authentication, elevation, installation, or timeout prevents completion.
- Closing and reopening the switcher retains account labels and credentials through Windows-native storage.

## Constraints
- Target Windows 10/11 with a .NET 8 WPF application published self-contained for x64.
- Store passwords in Windows Credential Manager under application-specific targets. Do not place passwords in JSON, command-line arguments, environment variables, clipboard contents, crash reports, or logs.
- Store only account ID, display name, username, and non-secret preferences in `%LOCALAPPDATA%\ZwiftAccountSwitcher\accounts.json`, using atomic replacement.
- Use documented Windows process and UI Automation facilities. Do not call undocumented Zwift authentication APIs, extract launcher tokens, patch Zwift, or bypass MFA/CAPTCHA.
- Clear or uncheck the launcher's **Remember me** state so selecting one account cannot silently reuse another.
- Permit only one login operation at a time. Do not terminate an active Zwift ride automatically; require the user to close Zwift before switching.
- Resolve `ZwiftLauncher.exe` from a saved override, known install locations, or user browse selection, and verify the selected file name before launch.
- Redact usernames, passwords, UI-entered text, credential targets, and personal Zwift data from diagnostic logs.
- Preserve behavior outside this boundary; profile switching, preferences, FIT routing, and Strava synchronization are separate work.

## Acceptance
- AC1: A user can add at least two named accounts, restart the application, and still see both accounts; inspection of application files and logs reveals no password or reversible password representation.
- AC2: Add, edit, and delete operations keep Windows Credential Manager and metadata consistent, including rollback or a clear recoverable error when either storage operation fails.
- AC3: Selecting **Ride as…** for account A or B makes the launcher use that selected account rather than its previously remembered account, leaves **Remember me** disabled, and starts `ZwiftApp.exe` after successful authentication.
- AC4: Already-running launcher, already-running game, launcher update/elevation, missing installation, changed/missing UI controls, rejected credentials, MFA/CAPTCHA, timeout, and cancellation produce bounded behavior and an actionable status without exposing secrets or killing an active ride.
- AC5: Automation logic has deterministic tests through process, clock, credential-store, and UI-automation abstractions; storage and state-machine checks pass without requiring real Zwift credentials.
- AC6: A self-contained Windows x64 build launches on the target PC without a separately installed .NET runtime, documents installation/use/removal, and passes a user-observed smoke test against the locally installed signed Zwift Launcher.

## Non-goals
- Sharing one Zwift subscription between riders or modifying weight, FTP, gender, achievements, or other server-side profile data.
- Strava authorization, FIT-file routing, workout management, or per-account `prefs.xml` isolation.
- Multiple simultaneous Zwift instances.
- Automatic handling or bypass of MFA, CAPTCHA, account lockouts, consent screens, or materially changed launcher flows.
- macOS, Linux, mobile, Apple TV, unattended service operation, or remote credential access.
- Reusing or redistributing ZwiftHacks source code.

## Observed behavior
- The target PC has a validly signed `ZwiftLauncher.exe` version 1.1.16.0 at `C:\Program Files (x86)\Zwift\ZwiftLauncher.exe` and a signed `ZwiftApp.exe` beside it.
- No .NET SDK or AutoHotkey installation is currently available; PowerShell 5.1 and Node.js are available. A self-contained .NET build therefore needs SDK bootstrapping during development but no runtime installation for end users.
- ZwiftHacks documents a proven multi-user flow that always chooses **Change User**, disables **Remember me**, enters one of several stored accounts, and presses **Let's Go**. It also documents launcher elevation and WebView2 UI changes as recurring automation failure modes.
- The current ZwiftHacks implementation stores passwords in an INI file and explicitly warns that they are not secure in shared/public settings; this project must not reproduce that design.

## Evidence
- Local executable metadata and Authenticode inspection performed during planning.
- https://zwifthacks.com/zwift-login/
- https://zwifthacks.com/zwift-login-v26-multi-user-support/
- https://zwifthacks.com/handling-administrator-mode-in-the-zwift-launcher-with-zwift-login/
- https://zwifthacks.com/zwift-login-v36-streamlining-and-improving/

## Unknowns
- Which current WebView2 launcher controls expose stable UI Automation names, automation IDs, and control types. This requires a read-only inspection/live spike before final selectors are chosen.
- Whether the launcher sometimes requires matching elevation after an update; the implementation must detect and explain this rather than silently elevating with credentials in memory.
- Whether MFA/CAPTCHA is enabled for any intended account. Such screens remain manual intervention points.
- Whether the user wants desktop shortcuts per rider in addition to the main account buttons; this is deferred unless requested.
