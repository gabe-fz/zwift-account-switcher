# Zwift Launcher UI Automation control map

Characterized read-only on the target PC against the signed Zwift Launcher 1.1.16.0. Zwift was not running, no credentials were entered, and no screenshots or personal control text were retained.

## Host and WebView2 boundary

| Relationship | Control type | Name | Automation ID | Class / framework |
|---|---|---|---|---|
| Main window | Window | `Zwift Launcher` | empty | `Window` / WPF |
| Main window descendant | Pane | empty | `wb_LauncherWebPage` | `HwndHost` / WPF |
| Raw-view descendant | Document | `Zwift` or page title | `RootWebArea` | Chrome |

The actionable HTML controls are visible in the **raw UI Automation view**, not reliably in the control view. Selectors must be scoped to the launcher window and prefer the IDs below; class names and accessible names are validation/fallback signals only.

## Remembered-account page

| Purpose | Type | Accessible name | Automation ID |
|---|---|---|---|
| Force account selection | Button | `CHANGE USER` | `change-user-btn` |
| Launch after authentication | Button | `LET'S GO` | `lets-go-btn` |

The remembered-account page also exposes profile text and images. The switcher must never read, retain, or log those names or values. It always selects **Change User** before entering a selected account when that page is present.

## Login page

| Purpose | Type | Accessible name | Automation ID | Required pattern |
|---|---|---|---|---|
| Username | Edit | `Email` | `username` | Value |
| Password | Edit | `Password` | `password` | Value; `IsPassword=true` |
| Persistence | CheckBox | `Remember me` | `rememberMe` | Toggle |
| Submit | Button | `LOG IN` | `submit-button` | Invoke |

`rememberMe` is explicitly toggled to Off and verified before either field is populated. The current UI Automation provider reports the old toggle state briefly after `Toggle()` returns (observed at about 130 ms), so verification polls for up to two seconds and fails closed if Off is not observed. The switcher does not use coordinates, keystroke timing, the clipboard, browser internals, or network/authentication APIs.

## Fallback and failure rules

1. Find the launcher top-level window by process and scope every search beneath it.
2. Find controls by exact automation ID and validate control patterns/type semantics. The IDs and required patterns above remain valid on Launcher 1.1.16.0; the compatibility failure characterized in September 2026 was an asynchronous toggle-state reporting change, not a selector change.
3. If the login controls are already present, do not require the remembered-account page.
4. If the remembered-account page is present, invoke `change-user-btn`, then wait with a deadline for all four login controls.
5. Missing or structurally incompatible controls fail closed as a launcher-change error; they do not fall back to coordinates.
6. UI Automation access denial is treated as an elevation/integrity mismatch. The switcher does not self-elevate.
7. Text indicating MFA, CAPTCHA, or identity verification is classified as manual intervention. It is not bypassed or copied.

Post-submit error and MFA/CAPTCHA controls were intentionally not characterized because doing so would require a real login flow. Detection is semantic and fail-closed; final verification requires the user-present smoke test in `manual-test.md`.
