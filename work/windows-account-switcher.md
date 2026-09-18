# Windows account switcher delivery
Spec: ../specs/windows-account-switcher.md
Mode: implement

## Context
- ../specs/windows-account-switcher.md — approved behavior, security boundary, and live-validation constraints
- ../src/ZwiftAccountSwitcher.App/ZwiftAccountSwitcher.App.csproj (new) — WPF composition root and account-card UI
- ../src/ZwiftAccountSwitcher.Core/ZwiftAccountSwitcher.Core.csproj (new) — account model, orchestration state machine, and ports
- ../src/ZwiftAccountSwitcher.Windows/ZwiftAccountSwitcher.Windows.csproj (new) — Credential Manager, process discovery, and Windows UI Automation adapters
- ../tests/ZwiftAccountSwitcher.Core.Tests/ZwiftAccountSwitcher.Core.Tests.csproj (new) — deterministic state-machine tests
- ../tests/ZwiftAccountSwitcher.Windows.Tests/ZwiftAccountSwitcher.Windows.Tests.csproj (new) — metadata, credential lifecycle, and selector-contract tests

## Baseline limitations
- This is a new project directory containing only planning documents; there is no source baseline or Git repository yet.
- The target PC has no .NET SDK. SDK installation/bootstrap and restore are implementation prerequisites, while the shipped application must be self-contained.
- Real login verification requires user participation and credentials that must never be recorded in fixtures, command output, screenshots, or task notes.
- Current launcher control identifiers are unknown until a UI Automation inspection is performed. The plan does not assume selectors from historic ZwiftHacks releases remain valid.

## Tasks

### T1 — Establish the Windows application and test seams
Acceptance: AC5, AC6
Files: ZwiftAccountSwitcher.sln (new), Directory.Build.props (new), src/ZwiftAccountSwitcher.App/ZwiftAccountSwitcher.App.csproj (new), src/ZwiftAccountSwitcher.Core/ZwiftAccountSwitcher.Core.csproj (new), src/ZwiftAccountSwitcher.Windows/ZwiftAccountSwitcher.Windows.csproj (new), tests/ZwiftAccountSwitcher.Core.Tests/ZwiftAccountSwitcher.Core.Tests.csproj (new), tests/ZwiftAccountSwitcher.Windows.Tests/ZwiftAccountSwitcher.Windows.Tests.csproj (new), .gitignore (new)
Check: dotnet restore; dotnet build --configuration Release; dotnet test --configuration Release

Initialize a Git repository and a minimal .NET 8 WPF solution. Keep process control, clock/timeouts, credentials, metadata, and UI Automation behind narrow interfaces so login behavior can be tested without Zwift or real secrets. Configure nullable analysis, warnings, and secret/build-artifact exclusions.

### T2 — Implement secure multi-account storage
Acceptance: AC1, AC2
Depends on: T1
Files: src/ZwiftAccountSwitcher.Core/Accounts/AccountRecord.cs (new), src/ZwiftAccountSwitcher.Core/Accounts/IAccountRepository.cs (new), src/ZwiftAccountSwitcher.Core/Secrets/ICredentialStore.cs (new), src/ZwiftAccountSwitcher.Windows/Accounts/JsonAccountRepository.cs (new), src/ZwiftAccountSwitcher.Windows/Secrets/WindowsCredentialStore.cs (new), tests/ZwiftAccountSwitcher.Windows.Tests/AccountStorageTests.cs (new), tests/ZwiftAccountSwitcher.Windows.Tests/CredentialStoreTests.cs (new)
Check: dotnet test --configuration Release --filter "FullyQualifiedName~AccountStorage|FullyQualifiedName~CredentialStore"; rg -n -i "password|secret" "$LOCALAPPDATA/ZwiftAccountSwitcher" -g '*.json' -g '*.log'

Implement stable account IDs, unique display names, atomic non-secret JSON metadata, and generic credentials in Windows Credential Manager. Coordinate create/update/delete operations so partial failures are rolled back or surfaced as recoverable cleanup instructions. Tests use isolated temporary metadata and uniquely prefixed disposable test credentials, then remove them in teardown. Record only redacted evidence.

### T3 — Characterize the current launcher and implement bounded automation
Acceptance: AC3, AC4, AC5
Depends on: T1
Files: src/ZwiftAccountSwitcher.Core/Launch/LoginState.cs (new), src/ZwiftAccountSwitcher.Core/Launch/LoginOrchestrator.cs (new), src/ZwiftAccountSwitcher.Core/Launch/LauncherPorts.cs (new), src/ZwiftAccountSwitcher.Windows/Launch/ZwiftInstallationLocator.cs (new), src/ZwiftAccountSwitcher.Windows/Launch/ZwiftProcessController.cs (new), src/ZwiftAccountSwitcher.Windows/Launch/ZwiftLauncherAutomation.cs (new), tests/ZwiftAccountSwitcher.Core.Tests/LoginOrchestratorTests.cs (new), tests/ZwiftAccountSwitcher.Windows.Tests/InstallationLocatorTests.cs (new), docs/launcher-control-map.md (new)
Check: dotnet test --configuration Release --filter "FullyQualifiedName~LoginOrchestrator|FullyQualifiedName~InstallationLocator"; dotnet test --configuration Release

With Zwift closed and without entering credentials, inspect the installed launcher UI Automation tree and record only non-sensitive control names/types/IDs and fallback relationships. Implement a cancellation-aware state machine with explicit deadlines: preflight, launch/wait, change user, locate fields, disable Remember me, retrieve and enter credentials, submit, handle manual-intervention screens, activate Let's Go, and confirm `ZwiftApp.exe`. Prefer semantic UI Automation selectors over coordinates and blind sleeps. Detect an active game and elevation/integrity mismatch without killing or silently elevating processes. Model changed controls, authentication rejection, MFA/CAPTCHA, timeout, and cancellation as typed outcomes.

### T4 — Deliver account buttons and safe operation feedback
Acceptance: AC1, AC2, AC3, AC4
Depends on: T2, T3
Files: src/ZwiftAccountSwitcher.App/App.xaml (new), src/ZwiftAccountSwitcher.App/App.xaml.cs (new), src/ZwiftAccountSwitcher.App/MainWindow.xaml (new), src/ZwiftAccountSwitcher.App/MainWindow.xaml.cs (new), src/ZwiftAccountSwitcher.App/ViewModels/MainViewModel.cs (new), src/ZwiftAccountSwitcher.App/ViewModels/AccountEditorViewModel.cs (new), src/ZwiftAccountSwitcher.App/Logging/RedactingLogger.cs (new), tests/ZwiftAccountSwitcher.Core.Tests/AccountWorkflowTests.cs (new)
Check: dotnet build --configuration Release; dotnet test --configuration Release

Build accessible account cards with Add/Edit/Delete and **Ride as…** buttons, masked password entry, confirmation for deletion, one-operation-at-a-time locking, progress text, cancellation before game launch, and actionable typed errors. Keep the password out of view models longer than necessary, clear editable secret fields after use, and ensure logs contain state transitions but no usernames or secrets.

### T5 — Package, document, and verify on the target PC
Acceptance: AC3, AC4, AC6
Depends on: T4
Files: README.md (new), docs/security.md (new), docs/manual-test.md (new), scripts/publish.ps1 (new), .github/workflows/windows.yml (new)
Check: dotnet test --configuration Release; powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1; inspect the publish directory for the expected self-contained x64 executable

Document installation, account management, Credential Manager behavior, normal removal/credential cleanup, launcher path override, security limitations, MFA/CAPTCHA handling, and recovery from launcher updates. Publish a self-contained Windows x64 artifact and add a Windows CI build/test workflow. With the user present, smoke-test one account and then a second account against the installed launcher, confirming the selected identity before entering a ride; do not record credentials or personally identifying screenshots. Exercise missing-install, active-game, cancellation, and invalid-selector behavior without risking an active ride.
