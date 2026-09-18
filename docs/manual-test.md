# Manual validation

Real-account steps require the account owner to be present. Never paste credentials into a terminal, test fixture, issue, chat, screenshot, or task note.

## Automated and no-credential checks

Run:

```powershell
.\.dotnet\dotnet.exe test --configuration Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

The deterministic suite covers missing installation, active-game refusal, recovery from a windowless background launcher, cancellation (including while waiting for game startup), early game-process exit, stable startup detection, changed selectors, authentication rejection, MFA/CAPTCHA classification, timeouts, one-operation locking, storage rollback, disposable Credential Manager entries, and selector contracts. These checks exercise safety outcomes without starting or risking an active ride.

Confirm the publish folder contains `ZwiftAccountSwitcher.exe` and no account metadata, settings, log, test-result, screenshot, or credential material.

## Target-PC UI check without credentials

1. Close Zwift normally and verify `ZwiftApp.exe` is absent.
2. Run the published executable.
3. Verify the account list, Add account editor, masked password control, Browse control, status region, and buttons are keyboard accessible.
4. Cancel the editor and verify the password field clears.
5. Do not enter a real account during this check.

## User-present real-account smoke test

Stop before this section unless the user is present and explicitly ready.

1. Verify no ride is active and close Zwift Launcher normally.
2. Add account A in the app. The user types the credential directly into the masked editor.
3. Add account B the same way.
4. Close and reopen the switcher. Confirm both display labels remain.
5. Inspect `%LOCALAPPDATA%\ZwiftAccountSwitcher\accounts.json`, `settings.json`, and logs without copying their contents into notes. Confirm no password or reversible representation is present.
6. Select **Ride as…** for account A.
7. If MFA, CAPTCHA, consent, update, or elevation appears, stop automation and complete only the legitimate manual prompt. Do not capture it.
8. After Zwift starts, confirm the intended rider identity before joining or starting a ride, then close Zwift normally.
9. Repeat steps 6–8 for account B and confirm the identity changed.
10. Reopen the launcher and verify **Remember me** remains disabled. If the launcher changed this behavior, stop and report a compatibility issue without sharing identity details.

## Recovery cases

- **Launcher update/elevation:** finish the update, close the launcher, reopen it normally, and retry. Never run this switcher as administrator merely to match an elevated launcher.
- **MFA/CAPTCHA:** complete it manually in Zwift Launcher. The switcher does not bypass it.
- **Rejected login:** edit the account and re-enter the credential; do not put the failed value in diagnostics.
- **Changed controls:** stop. Compare only non-sensitive UI Automation metadata with `launcher-control-map.md` and update/test selectors before retrying.
- **Partial storage failure:** follow the status message, retry edit/delete, and use Windows Credential Manager for final cleanup if necessary.
