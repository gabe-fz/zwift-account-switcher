# Zwift Account Switcher for Windows

A portable Windows application that stores multiple Zwift accounts and provides one **Ride as…** button per account. It switches the current Zwift Launcher user, disables **Remember me**, signs in, selects **Let's Go**, and waits for `ZwiftApp.exe`.

This is an unofficial community project and is not affiliated with or endorsed by Zwift, Inc. Zwift and related marks belong to their respective owner.

## Requirements

- Windows 10 or 11, x64
- Zwift Launcher installed
- The same non-administrator desktop session for this application and Zwift Launcher

The published application is self-contained; users do not need to install .NET.

## Install and run

1. Obtain the `win-x64` publish artifact.
2. Put `ZwiftAccountSwitcher.exe` in a folder owned by your Windows user.
3. Run it. If Windows displays an unrecognized-app warning, verify where the file came from before continuing; local builds are not code-signed.
4. Choose **Add account**, enter a display label, Zwift username, and password, then choose **Save**.
5. Repeat for each rider.
6. Ensure no Zwift ride is active, then choose **Ride as…** for the intended rider.

Only the display label and username appear in the app metadata. Passwords are stored as application-specific Generic Credentials in Windows Credential Manager.

## Launcher location

The switcher checks standard Zwift installation locations. If it cannot find Zwift, choose **Browse…** and select a file named `ZwiftLauncher.exe`. The override is stored in `%LOCALAPPDATA%\ZwiftAccountSwitcher\settings.json` and contains no credential.

## Account management

- **Edit** changes a label or username. Leave the password box blank to retain the existing password.
- **Delete** removes both account metadata and its Windows Credential Manager entry.
- **Cancel login** is available through authentication and while waiting for game startup. Cancelling only stops the switcher's wait; it never terminates Zwift.
- If Zwift is already running, the switcher stops before opening or changing the launcher. Close Zwift normally after finishing the ride.

MFA, CAPTCHA, consent, lockout, and unfamiliar launcher screens are manual intervention points. The application never bypasses them.

## Build

A local .NET 8 SDK is bootstrapped under `.dotnet` for development. From PowerShell:

```powershell
.\.dotnet\dotnet.exe restore
.\.dotnet\dotnet.exe test --configuration Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

The self-contained single-file x64 output is written to `artifacts\publish\win-x64`.

## Remove

1. Delete every account in the app so its Windows credentials are removed.
2. If the executable is no longer available, open **Credential Manager → Windows Credentials** and remove Generic Credentials labeled for Zwift Account Switcher.
3. Delete the executable and `%LOCALAPPDATA%\ZwiftAccountSwitcher`.

See [security](docs/security.md), [launcher control map](docs/launcher-control-map.md), and [manual validation](docs/manual-test.md).
