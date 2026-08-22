# AppGeek

**Scan, update and install Windows applications — part of the TechyGeeksHome "Geek" series.**

AppGeek does two jobs on the PC it is running on:

1. **Update scanning.** Reads everything installed, works out what has a newer version available, shows you exactly what would change, and installs the ones you tick.
2. **Catalogue installing.** A browsable, categorised list of common Windows software. Tick what you want, press install once, walk away.

Network and domain features (discovering other machines, remote inventory, pushing installs) are deliberately **not** in v1. See [Roadmap](#roadmap).

---

## What a user's machine actually needs

**Nothing third-party. No Node.js, no Python, no runtimes to install first.**

Node.js and Python appear in `catalogue.json` as *apps a user can choose to install* — they are entries in the catalogue, not dependencies of AppGeek. AppGeek is a single .NET executable and shells out to exactly three things, all of which ship with Windows:

| What it runs | Why | Where it comes from |
| --- | --- | --- |
| `winget.exe` | Install and update packages | Microsoft **App Installer** — in the box on Windows 11 and modern Windows 10 |
| `powershell.exe` | Store app inventory, restore point | Windows PowerShell 5.1 — in the box on every supported Windows |
| `cmd.exe`, `shutdown.exe` | Hand off to an app's own uninstaller; optional shutdown after a run | In the box |

Plus the .NET 8 Desktop Runtime, which the **standalone build bundles inside the exe**. Users of that build install nothing.

### Requirements, stated plainly

- Windows 10 version 1809 (build 17763) or later, or Windows 11
- Administrator rights, prompted once per install run
- An internet connection when installing or updating

### The one thing that can be missing: App Installer

winget comes from Microsoft's App Installer package. It is present by default on Windows 11 and on modern Windows 10, but it can be absent on LTSC builds, freshly imaged or sysprepped machines, Windows Sandbox, and accounts that have only just logged in for the first time.

AppGeek does not fail with a cryptic error when this happens. `WingetBootstrapper` detects it at startup and shows a banner with a **Fix this for me** button that escalates through three routes:

1. **Re-register the existing package** (`Add-AppxPackage -RegisterByFamilyName`). Offline, instant, and resolves the most common case — the package is on the machine but not registered for this account.
2. **Open the Microsoft Store** to the App Installer listing. The right answer for most home users.
3. **Official PowerShell bootstrap** (`Repair-WinGetPackageManager`). Needs internet and takes a minute or two, so it is offered explicitly rather than run automatically.

The same check also catches a winget that is present but **older than 1.4**, which is the release that added `--disable-interactivity`. AppGeek passes that flag on every call to stop winget prompting inside a redirected console, so anything older would hang.

After a successful repair the winget probe is re-run in place — no restart needed.

---

## Design principles

These are the decisions that shape the code, and the reasons behind them.

**Nothing installs itself.** Scheduled scans find updates and can notify, but an install always needs a deliberate tick and click. The tools people uninstall in frustration are the ones that decided for them.

**One UAC prompt per run, not one per app.** AppGeek starts unelevated (`asInvoker` in the manifest) and only relaunches elevated when a run actually begins.

**Pinning is a first-class feature.** Every IT person has a line-of-business app that breaks on a newer runtime. Right-click → pin, and that version is never offered again.

**Running apps are surfaced, not silently failed.** If Chrome is open, the update row says so and is left unticked rather than failing halfway through with a cryptic exit code.

**Never `Win32_Product`.** Querying that WMI class triggers an MSI consistency check on every installed product — slow, and it can silently reconfigure the user's software. AppGeek reads the uninstall registry keys, which is what Windows itself does.

---

## Architecture

```
.github/workflows/   CI build and tagged release pipeline
catalogue.json       the published catalogue, fetched by the app for updates
icons/               generated icon set: PNG export sizes, appgeek.png, appgeek.ico
tools/make-icon.py   the icon generator — edit the glyph here, never the PNGs
src/AppGeek/
├── Models/          Plain data: InstalledApp, UpdateCandidate, CatalogueApp, RunItem, AppSettings
├── Services/        All the real work — no UI references
├── ViewModels/      MVVM layer, one per page plus the shell
├── Views/           XAML, one UserControl per page
├── Themes/Dark.xaml TechyGeeksHome palette and every control style
├── Converters/      Value converters used by the XAML
└── Assets/          catalogue.json (embedded in the exe)
```

### Services worth knowing about

| Service | What it does |
| --- | --- |
| `RegistryInventoryService` | Reads HKLM + HKCU uninstall keys across both registry views, filters out system components and Windows updates |
| `AppxInventoryService` | Optional MSIX/Store inventory via `Get-AppxPackage` |
| `WingetClient` | Wraps the winget CLI: list, upgrade, search, install |
| `WingetText` | Parses winget's fixed-width table output — the fiddliest part of the app, see below |
| `InventoryService` | Merges the above and produces the update list with exclusions applied |
| `InstallRunner` | Executes a queue of jobs sequentially, streaming progress and writing a run log |
| `CatalogueService` | Loads the embedded catalogue, optionally refreshes it from a URL |
| `ReportExporter` | CSV and branded HTML inventory reports |
| `AppInfo` | One place for the app's identity — name, tagline, URLs, credits, version read from the assembly |
| `UpdateChecker` | Manual-only GitHub releases check. Reports and offers to open the release page; never downloads or installs |

### About the winget parser

winget has no stable machine-readable output for `list` and `upgrade`, so the CLI's fixed-width table has to be parsed. `WingetText.ParseTable` does this defensively:

- Column boundaries are read from the **header row**, not assumed, so column widths can change without breaking it.
- Columns are matched by name where possible (`Name`, `Id`, `Version`, `Available`, `Source`) and **positionally otherwise**, so it keeps working on non-English Windows where the headers are translated.
- The animated progress output winget writes before the table (carriage returns, spinner characters, block glyphs, ANSI escapes) is stripped.
- Parsing stops at the first blank line, so the "requires explicit targeting" second table is not merged into the first.

**If winget ever ships stable JSON output, or the COM API becomes the better route, only `WingetClient` needs to change.** The service boundary was drawn there on purpose — see [Roadmap](#roadmap).

---

## App chrome

AppGeek carries the standard TechyGeeksHome sidebar and About dialog used across the range:

- **Brand block** — icon, app name, and "by TechyGeeksHome" at the top of the sidebar.
- **Real buttons, not text links** — "Check for updates" and "About AppGeek" as full-width
  buttons at the bottom of the sidebar, with the version beneath them.
- **Status strip** — where the update check reports back, along the bottom of the window.
- **About dialog** — icon, name, tagline, version line, what-it-is card, the four link buttons
  (Website / Product page / Source on GitHub / Report a problem), the red Ko-fi button, a
  credits list and an inline update check.

### Why this is a WPF rebuild rather than the shared library

The shared `TechyGeeksHome.Common` project in the PDFGeek repo depends on Avalonia, so it cannot
be referenced from a WPF app. What is matched here is the **design**, not the code — same
layout, same buttons, same behaviour. This is the same call Ultimate Settings Panel had to make
from the other direction, being Go and WebView2.

If the range ever consolidates on one UI framework, `Views/AboutWindow.xaml` and
`Services/AppInfo.cs` are the two files to delete in favour of the shared version.

### The update check never updates anything

It asks the GitHub releases API whether a newer tag exists, reports the answer, and offers to
open the release page. It is manual only and it never downloads or installs. An updater that
silently self-updates is precisely the behaviour AppGeek refuses to inflict on other people's
software, so it would be odd to do it to its own.

## Icon

AppGeek uses the TechyGeeksHome **app-icon** system — the blue-gradient badge with a solid white
pictorial glyph shared by the rest of the range. Note this is deliberately *not* the web/brand
system (navy with a `#38BDF8` "TG" monogram); that one is for favicons and social avatars. The
two are easy to mix up and doing so produces an icon that does not belong to the range.

| Token | Value |
| --- | --- |
| Gradient top | `#6BA3F7` |
| Gradient bottom | `#2563EB` |
| Glyph | `#FFFFFF`, solid |
| Gloss | white at ~12%, soft ellipse over the upper half |
| Corner radius | 22% of icon size |
| Lettering | none, at any size |

**The glyph** is a 2×2 grid in which three cells are app tiles and the fourth is a download
badge — "applications" plus "install / update". Putting the badge *in* a grid cell rather than
pasting it over the corner is what keeps it tidy as it scales.

### Regenerating

```cmd
python tools\make-icon.py
```

Writes the full export set to `icons/`: PNG at 1024, 512, 256, 128, 96, 64, 48, 32 and 16, the
canonical `icons/appgeek.png` at 256, and a multi-resolution `icons/appgeek.ico`
(256/128/64/48/32/16) which the csproj embeds via `<ApplicationIcon>`. The same PNG is linked
into the project as a WPF resource and used for the window icon, the sidebar mark and the
first-run screen, so `icons/` stays the single source of truth.

`make-icon.py` follows the shared script structure: everything except `draw_glyph()` and
`draw_secondary_badge()` is common to the range, so the next Geek app's icon is made by copying
the file and replacing those two functions.

### Detail levels

The glyph simplifies as it shrinks rather than being blurred down:

| Size | What is drawn |
| --- | --- |
| 48px and up | Circle badge with separation ring, arrow and tray line |
| 32–47px | Circle badge and arrow, no tray line |
| 24–31px | Bare arrow, no circle |
| Below 24px | Four plain tiles, no badge, geometry snapped to whole pixels |

At 16px there is roughly 4px per element, so an arrow renders as a smudge. Four crisp tiles read
better and still say "applications". The tiny sizes are drawn directly at their target
resolution rather than downsampled, because fractional pixel edges are what make small icons
look soft.

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```cmd
build.cmd            :: self-contained single exe, no prerequisites  (~63 MB)
build.cmd light      :: framework-dependent exe, needs .NET 8 Desktop Runtime  (~0.4 MB)
```

Or open `AppGeek.sln` in Visual Studio 2022 and press F5.

Releases are built by GitHub Actions rather than from a developer's machine — see
`.github/workflows/release.yml`. Push a tag (`git tag v1.0.0 && git push origin v1.0.0`) and
it publishes both builds plus `SHA256SUMS.txt`. That matters beyond tidiness: it means the
binary you download provably comes from the source in this repository.

The project sets `EnableWindowsTargeting`, so it also builds on Linux/macOS CI agents (it just cannot be run there).

**Which build to ship?** The self-contained one for the website download — a freeware utility that fails on launch because the user has no .NET runtime generates support emails. Keep the light build for anyone who already has the runtime, or for deployment to a managed estate where the runtime is already present.


## Known limitations in v1

| Limitation | Notes |
| --- | --- |
| Scheduled scanning is a stored preference only | The Windows scheduled task is not created yet — the UI setting is wired to the model, the task registration is not implemented |
| "Always close running apps" is not implemented | The policy is stored and the Ask path works; automatic closing is not wired up |
| Reboot policy is stored but not acted on | winget's reboot-required exit code is detected and logged, but AppGeek does not yet prompt |
| Chocolatey source is a toggle only | No Chocolatey provider implemented |
| No offline install mode | Without winget there is no install engine. The bootstrapper makes that recoverable rather than fatal, but AppGeek cannot install software with App Installer absent |
| Reports export as HTML/CSV, not PDF | Deliberate: browser "Print to PDF" covers it and it avoids a PDF dependency in v1 |
| Uninstall hands over to the app's own uninstaller | It is not silent, and it does not wait for completion |

None of these are hidden — they are the honest edges of a first version.

---

## Data and privacy

Everything stays on the machine. AppGeek makes exactly two kinds of outbound request:

- winget talks to its own package sources when installing (as it always does)
- the catalogue refresh downloads `catalogue.json` from the configured URL

No inventory, telemetry or machine identifier is ever sent anywhere.

Files it writes:

```
%APPDATA%\AppGeek\settings.json      preferences, exclusions and pins
%APPDATA%\AppGeek\activity.json      the recent-activity list
%APPDATA%\AppGeek\catalogue.json     cached catalogue download
%APPDATA%\AppGeek\Logs\              daily log plus one log per install run
%PROGRAMDATA%\AppGeek\Cache\         downloaded installers, if that option is on
```

---

## Licence

**GNU General Public License v3.0.** See [LICENSE](LICENSE).

You may use, study, share and modify AppGeek freely. If you distribute a modified version,
you must publish your source under the same licence. You cannot take this code, close it,
and sell it as your own product.

This is a change of position for the TechyGeeksHome range, which previously shipped as
proprietary freeware. The reason is concrete: free code signing for open source projects
requires an OSI-approved licence, and an unsigned installer that asks for administrator
rights is a genuinely poor thing to hand to people.

**AppGeek is currently unsigned.** Windows SmartScreen will warn on first run. Verify the
SHA-256 published with each release rather than disabling the warning.

## Roadmap

**v1.1 — finish the edges.** Scheduled task registration, reboot prompting, automatic closing of running apps, application icon.

**v1.2 — winget COM API.** Replace the CLI wrapper in `WingetClient` with `Microsoft.Management.Deployment`. Gives structured results and real progress events instead of parsed console text, and removes the localisation risk entirely.

**v2 — the fleet tier.** Discovery via Active Directory, subnet sweep or CSV import; agentless inventory over WinRM/CIM; a drift report against a defined baseline; optionally CVE enrichment via the NVD API.

Note for whoever builds v2: the obvious approach — PS Remoting into a machine and running `winget install` — mostly does not work. winget is a per-user MSIX package and misbehaves under SYSTEM and in sessions with no loaded user profile. The routes that do work are resolving the installer centrally and pushing it, creating a scheduled task on the endpoint, or shipping a small agent. Budget for that properly rather than discovering it late.

---

*TechyGeeksHome · techygeekshome.info*
