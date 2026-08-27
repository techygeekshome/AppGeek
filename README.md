<div align="center">

<img src="https://raw.githubusercontent.com/techygeekshome/AppGeek/main/icons/appgeek.png" alt="AppGeek logo" width="96" height="96">

# AppGeek

**Update everything on your PC in one run, and install new software from a curated list — free, open source, and built on winget.**

[![Version](https://img.shields.io/github/v/release/techygeekshome/AppGeek?label=version&color=4c9bff)](https://github.com/techygeekshome/AppGeek/releases)
[![Platform](https://img.shields.io/badge/platform-Windows-0078d4)](#%EF%B8%8F-download--run)
[![License](https://img.shields.io/badge/license-GPL--3.0-3fb950)](LICENSE)
[![Made by TechyGeeksHome](https://img.shields.io/badge/made%20by-TechyGeeksHome-b191f2)](https://techygeekshome.info)
[![Support on Ko-fi](https://img.shields.io/badge/support-Ko--fi-ff5e5b)](https://ko-fi.com/techygeekshome)

[Download](#%EF%B8%8F-download--run) · [Features](#-what-it-does) · [Safety](#-what-it-will-not-do-to-your-machine) · [Build from source](#-build-from-source) · [License](#-license)

</div>

---

Keeping a Windows PC up to date is a chore made of many small chores. Every application has its own updater, its own nag window, its own idea of when to interrupt you. AppGeek does the lot in one pass: it reads everything installed, works out what has a newer version, shows you exactly what would change, and updates the ones you tick.

It also carries a browsable catalogue of common Windows software — browsers, runtimes, media tools, developer tools — so setting up a new machine is a case of ticking a list and walking away.

No bundled offers, no telemetry, no account, no Pro tier. It is free for everyone, including at work.

## ⬇️ Download & run

| What it is | Get it |
| --- | --- |
| **Installer** *(recommended)* — Start menu entry, desktop shortcut, proper uninstall | [**AppGeekSetup.exe**](https://github.com/techygeekshome/AppGeek/releases/latest) |
| **Portable** — one `.exe`, no prerequisites, nothing written outside its own folder | [**AppGeek.exe**](https://github.com/techygeekshome/AppGeek/releases/latest) |
| **Portable, light** — needs the .NET 8 Desktop Runtime already installed | [**AppGeek-light.exe**](https://github.com/techygeekshome/AppGeek/releases/latest) |

The installer puts AppGeek in Program Files and registers an uninstall entry like any other application. The portable builds install nothing — download and run.

> **Windows will warn you the first time.** AppGeek isn't code signed, because a certificate is a recurring cost we'd rather not put behind a free tool. Click **More info → Run anyway**. Every release publishes SHA-256 checksums so you can verify what you downloaded, and the source is right here so you can see exactly what it does.

## ✨ What it does

- 🔍 **Finds every update in one scan.** Reads the installed-programs registry and the Store package list, matches them against winget, and shows you what is out of date.
- ✅ **Updates only what you tick.** Nothing installs itself, ever. Scheduled scans can find and notify; installing is always a deliberate click.
- 📦 **Installs from a catalogue** of 67 common applications, sorted by category, each verified against the live winget repository.
- 📌 **Pins versions that must not move.** Every IT person has a line-of-business app that breaks on a newer runtime. Right-click, pin, and it is never offered again.
- 🔴 **Flags security-relevant updates** — browsers, PDF readers, Java and the like are marked so an out-of-date browser reads as more urgent than an out-of-date archiver.
- 🚦 **Tells you when an app is open** rather than failing halfway through with a hex code — and can skip it, or ask it to close first, whichever you prefer.
- ⏰ **Scans on a schedule you choose** — daily, weekly, at startup, or never. A background scan finds what is available and tells you; it never installs anything.
- 🔁 **Handles restarts honestly.** When an installer needs one, AppGeek says so and asks. Set it to restart automatically if you want that, and it still refuses to do so after a run that went wrong.
- 💾 **Optional restore point** before a run, and an optional shutdown after it.
- 📄 **Exports a full report** of everything installed as CSV or HTML — handy for an audit or a rebuild.
- 🔒 **Private.** No telemetry, no account, no data leaves your machine.

### On the one network call

AppGeek itself makes exactly one network request, and only when you click **Check for updates**. It asks GitHub's public releases API whether a newer tag exists. The request carries a user agent naming AppGeek, its version and this site, because GitHub rejects requests without one — and nothing else. No machine identifier, no list of your installed software, no usage data. It never downloads or installs anything on its own: if there is a newer version it offers to open the release page, and that is all. Don't press it and AppGeek makes no request of its own at all, including at startup.

It is worth being precise about the part that is *not* AppGeek's: scanning for updates and installing anything runs **winget**, and winget talks to Microsoft's package sources to do its job. That traffic is the Windows Package Manager's, under Microsoft's terms, and it happens whether you drive winget from AppGeek or from a command prompt. AppGeek does not add anything to it.

## 🛡️ What it will not do to your machine

An app that runs installers with administrator rights has to be careful in ways an ordinary utility does not. These are deliberate design decisions, not incidental behaviour:

**It never moves an application you already have.** Running elevated, winget defaults to the machine-wide installer. Point that at an app that was installed per-user and it does not upgrade it — it installs a second copy elsewhere and strands the original, leaving dead shortcuts and an app that will not start. AppGeek reads how each application is actually installed and pins winget to that scope. If no installer exists at the right scope, it refuses and says so.

**It never kills an installer.** Stopping a run prevents the *next* package from starting; it cannot interrupt the one in flight. There is no timeout that kills either. A long install is slow, not broken, and half-installed software does far more damage than waiting does.

**It never kills your applications either.** Set "when an app is running" to *Always close* and AppGeek asks the app to close, exactly as clicking its X would — so it can still prompt you about unsaved work. If it is still open twenty seconds later, AppGeek skips that update and tells you. Nothing in AppGeek force-terminates anything.

**It never restarts your PC quietly.** A restart is only ever proposed when Windows or an installer has actually asked for one. *Prompt at the end* is the default. Even on *Restart automatically* there is a two-minute countdown you can cancel with `shutdown /a`, and a run that failed or was stopped downgrades itself to a prompt rather than restarting on top of a half-finished job.

**It refuses an uncertain match.** Whatever package ID gets attached to an installed application is what gets handed to `winget upgrade`. Bind the wrong one and it cheerfully installs unrelated software over something that was working. Matching demands an exact name, or a winget-truncated name with a long surviving prefix, with versions corroborating. An unmatched app is a far better outcome than a wrongly matched one.

**It never uses `Win32_Product`.** Querying that WMI class triggers an MSI consistency check on every installed product — slow, and it can silently reconfigure software. AppGeek reads the uninstall registry keys, which is what Windows itself does.

**It writes down what it did.** Every run logs the detected install scope, the exact winget command line, a heartbeat while an installer is working, and winget's own output on failure. Settings has an **Export diagnostics** button that zips the lot.

## 💻 What your PC needs

- Windows 10 version 1809 (build 17763) or later, or Windows 11
- Administrator rights
- An internet connection when installing or updating

**Nothing third-party.** No Node.js, no Python, no runtimes to install first — those appear in the catalogue as things you *can* install, not as dependencies. AppGeek shells out only to `winget.exe`, `powershell.exe` and `cmd.exe`, all of which ship with Windows, and the standalone build bundles the .NET runtime inside the executable.

The one piece that can be missing is Microsoft's **App Installer**, which provides winget. It is present by default on Windows 11 and modern Windows 10, but absent on LTSC builds, freshly imaged machines and Windows Sandbox. AppGeek detects that at startup and offers to fix it — re-registering the existing package, opening the Store listing, or running Microsoft's official repair — rather than failing with a cryptic error.

## 🔧 Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet build AppGeek.sln -c Release
```

To produce the shipping builds:

```powershell
build.cmd            # standalone, no prerequisites
build.cmd light      # framework-dependent, needs the .NET 8 Desktop Runtime
build.cmd installer  # standalone build, then dist\AppGeekSetup.exe
```

The installer step needs [Inno Setup 6](https://jrsoftware.org/isdl.php) (free); the script itself is `installer/AppGeek.iss`.

To run the test suite:

```powershell
dotnet run --project tests/AppGeek.Tests -c Release
```

Releases are built by GitHub Actions rather than on a developer's machine — push a tag (`git tag v1.0.0 && git push origin v1.0.0`) and `release.yml` publishes the installer, both portable builds and `SHA256SUMS.txt`. That matters beyond tidiness: it means the binary you download provably comes from the source in this repository.

The project sets `EnableWindowsTargeting`, so it also builds on Linux and macOS CI agents — it just cannot be run there.

### Project layout

| Path | What's there |
| --- | --- |
| `src/AppGeek/Services` | Every piece of real work — winget, the registry, matching, install runs. No UI dependencies |
| `src/AppGeek/ViewModels` | MVVM layer, one per page plus the shell |
| `src/AppGeek/Views` | XAML, one UserControl per page |
| `src/AppGeek/Themes/Dark.xaml` | TechyGeeksHome palette and every control style |
| `tests/AppGeek.Tests` | Console test harness — no third-party packages, run on every push |
| `catalogue.json` | The published catalogue the app fetches for updates |
| `tools/make-icon.py` | Generates the icon set from the brand tokens |

## ☕ Support

AppGeek is free and always will be. If it saved you an afternoon, you can [buy us a coffee on Ko-fi](https://ko-fi.com/techygeekshome) — welcome, but never expected.

## 🐛 Support & contributing

Found a bug or have a request? [Open an issue](https://github.com/techygeekshome/AppGeek/issues) or [get in touch](https://techygeekshome.info/contact/).

Pull requests are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for how contributions are licensed and what is most useful. Anything touching the install path is held to a higher standard than the rest of the code, for the reasons above.

## 📄 License

AppGeek is free software, licensed under the [GNU General Public License v3.0](LICENSE). You are free to use it, study it, change it and pass it on. Anything you distribute that is built from this code has to carry the same freedoms, which is what keeps it free for everyone downstream.

The AppGeek name, logo and TechyGeeksHome branding are not covered by that licence and remain ours.

**AppGeek is currently unsigned.** Windows SmartScreen will warn on first run. Verify the SHA-256 published with each release rather than disabling the warning.

© 2026 TechyGeeksHome | Andrew Armstrong.

---

<div align="center">

Made with ❤️ by [**TechyGeeksHome**](https://techygeekshome.info)

[Website](https://techygeekshome.info) · [YouTube](https://www.youtube.com/channel/UCtEuFj1SMLiuRoucD1hv8dA) · [X](https://x.com/TechyGeeks1) · [Instagram](https://www.instagram.com/andrewarmstrongtgh/)

</div>
