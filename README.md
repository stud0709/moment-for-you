# A moment for you

A gentle Windows tray app that reminds you to step away from screen work, move around, and rest your eyes.

[Download the latest release](https://github.com/stud0709/moment-for-you/releases/latest) · [Build status](https://github.com/stud0709/moment-for-you/actions/workflows/build-release.yml)

![The current break screen, with a Done button and reminders to move and look into the distance](docs/images/break-screen.png)

Every **20 minutes** by default, an opaque break window covers each connected monitor. It asks you to stand up and move around, then look at something at least **6 metres away for at least 20 seconds**.

There is **no countdown**. The **Done** button is available immediately, so you can take your time and dismiss the screen when you return. Clicking Done on any monitor closes all break windows and starts a fresh reminder interval. **Esc** or **Alt+F4** also dismisses the break.

The **Project homepage** link in the lower-right corner also dismisses all break windows before opening the project in your browser.

## Get started

1. Open [Releases](https://github.com/stud0709/moment-for-you/releases/latest), download the **win-x64.zip**, and extract it.
2. Run **A moment for you.exe** from the extracted folder. The small Windows x64 executable uses **.NET Framework 4.8**, included with Windows 10 version 1903 and later and Windows 11. It needs no installer, administrator access, or separate .NET 10 installation.
3. Click **Take a break now** to try it, or set an interval between **1 and 240 minutes** and click **Save**.

The app is unsigned, so Windows may show its usual reputation warning. Exit any older copy from its tray menu before opening a new version.

You can also download the standalone **A.moment.for.you.exe**. GitHub uses dots instead of spaces in release asset names; the ZIP preserves **A moment for you.exe**. Both contain the same app. Only the EXE is required to run it; screenshots and documentation in the ZIP are optional. Older Windows 10 installations may need .NET Framework 4.8 installed first.

## Settings and tray controls

![The current settings window showing the interval and tray behavior](docs/images/settings.png)

Closing the settings window keeps the app running in the notification area. The eye icon may be under the Windows tray overflow arrow. Double-click it to reopen settings, or right-click it for:

| Command | What it does |
| --- | --- |
| **Set interval…** | Choose 1–240 minutes and save. A full new interval starts immediately. |
| **Take a break now** | Show the break screen on every connected monitor. |
| **Pause reminders / Resume reminders** | Pause automatic reminders; resuming starts a full new interval. |
| **Exit** | Dismiss every break window and quit. |

The countdown stops while Windows is locked, a screensaver is running, or the PC is asleep. When you return, it starts over with a full new interval; if both a lock and screensaver are active, both must end first. A manual pause stays in effect until you choose Resume reminders. If a break is already showing when you leave, it is dismissed and a full interval waits for your return.

Connecting or disconnecting a monitor during a break rebuilds the windows while keeping the break open. The app supports per-monitor scaling and monitors positioned to the left of the primary screen.

This is a break reminder, not a Windows security lock. Windows shortcuts, the secure desktop, and some privileged or exclusive fullscreen windows can still take precedence.

Your interval is stored locally in `%LOCALAPPDATA%\EyeProtect\settings.json`. The original settings folder is retained so earlier versions keep their saved interval. The app does not send telemetry or make network requests.

Enable **Autostart with Windows** in settings to launch quietly in the tray whenever you sign in. The checkbox saves immediately, applies only to your Windows account, and needs no administrator access. Uncheck it to remove autostart. It is off by default.

Keep the portable executable in the folder you intend to use. If you move it or replace it with a copy in a different folder, turn autostart off and on again in that copy to update its location. You can also launch quietly yourself with the `--tray` argument.

## Build and test

Requires Windows, a **.NET 10 SDK** for compilation, and PowerShell 7. The app itself targets **.NET Framework 4.8**. Its reference assemblies are restored automatically from the build-only `Microsoft.NETFramework.ReferenceAssemblies` NuGet package; no NuGet runtime dependencies are shipped.

```powershell
./scripts/Build.ps1
```

This builds the x64 app, copies only its EXE to an isolated folder, and runs the automated checks without any companion DLL or configuration file. It also rejects an executable of 1 MiB or larger to prevent runtime bundling from returning. The executable, ZIP, and `SHA256SUMS.txt` are written to `artifacts/release/<version>/`.

For a quick development build:

```powershell
dotnet build -c Release
dotnet run -c Release --no-build -- --self-test
dotnet run -c Release --no-build -- --render-preview artifacts/break-preview.png
```

`--self-test` verifies that the app runs on the Windows Framework CLR rather than modern .NET, then checks scheduling, immediate dismissal and long breaks, pause/resume, lock/screensaver/sleep behavior and overlapping conditions, clock gaps, settings compatibility with earlier releases, the WPF Done button and homepage action, native window bounds on every connected monitor, tray menu actions, autostart, and footer placement at common screen sizes and scaling settings. It writes `artifacts/self-test-results.txt` and returns a nonzero exit code on failure. It briefly creates a tray icon but never displays a break. Autostart tests use isolated registry entries and do not enable Windows startup on the test PC.

`--render-preview` renders the actual WPF break content and settings window to PNG without covering your desktop. The screenshots above come from this renderer.

For manual verification, use **Take a break now** with multiple monitors and mixed scaling. Check that each screen, including its taskbar, is covered and Done is available immediately. Confirm that clicking Done on a secondary monitor closes every break window, Esc works, and monitor changes during a break are handled. Set a 1-minute interval to test automatic reminders and confirm pause suppresses them.

## Release pipeline

[GitHub Actions](.github/workflows/build-release.yml) builds and tests Windows x64 on pushes to `main`, pull requests, and manual runs. Successful builds provide downloadable workflow artifacts.

To publish a release:

1. Update the version in `MomentForYou.csproj` and `app.manifest`.
2. Add release notes at `docs/releases/vX.Y.Z.md`, commit, and push to `main`.
3. Push an annotated tag matching the project version:

```powershell
git tag -a v1.1.0 -m "Release v1.1.0"
git push origin v1.1.0
```

The tag workflow validates the version, builds the app, runs its checks, packages it, and verifies checksums. Only after those steps succeed does it publish a GitHub release with the executable, ZIP, and checksum file. No personal access token or extra repository secret is required; the release job uses the repository's `GITHUB_TOKEN`.

© stud0709 · [Project homepage](https://github.com/stud0709/moment-for-you)
