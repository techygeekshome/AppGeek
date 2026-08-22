# Contributing to AppGeek

Contributions are welcome — bug reports, fixes, catalogue corrections and translations
especially.

## Licensing of contributions

AppGeek is licensed under the **GNU General Public License v3.0** (see [LICENSE](LICENSE)).

**Inbound equals outbound.** By opening a pull request you agree that your contribution is
licensed under GPL-3.0, the same terms as the rest of the project. You keep the copyright in
what you wrote; you are granting the same licence everyone else gets.

There is no Contributor Licence Agreement to sign. That is deliberate: a CLA is what a
*proprietary* project needs, because it has to collect rights it would not otherwise have. An
open source project under a copyleft licence does not have that problem — which is one of the
practical reasons AppGeek is GPL-3.0.

**Please do not modify [LICENSE](LICENSE) in a pull request.** The GPL text is verbatim and
must stay that way; a change there will be rejected regardless of intent, and it makes the rest
of the diff harder to review.

## What is most useful

- **Catalogue corrections.** `catalogue.json` holds the browsable app list. Wrong winget IDs
  are the most likely defect — they were written from knowledge, not enumerated from a live
  winget repo.
- **Bugs in the install path.** Anything that installs, upgrades or uninstalls software is
  held to a higher standard than the rest of the code. Read the comments in
  `Services/InstallScopePolicy.cs` and `Services/ProcessRunner.cs` before changing either —
  both exist to prevent a specific way of damaging someone's machine.
- **The winget output parser.** `Services/WingetText.cs` parses a fixed-width console table.
  If you change it, test it — several real bugs have already been found there, including one
  that only appeared on non-English Windows.

## Ground rules

- The build must stay clean. CI fails on warnings.
- **Never kill a process that is installing software.** `ProcessAbortPolicy.NeverKill` exists
  because killing an installer mid-write leaves applications half removed and unable to start.
- Prefer an unmatched app over a wrongly matched one. `Services/PackageMatcher.cs` is
  deliberately strict for that reason.
- Explain *why* in comments, not *what*. The code says what it does.

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```cmd
dotnet build AppGeek.sln
build.cmd
```

The project sets `EnableWindowsTargeting`, so it also builds on Linux and macOS — it just
cannot be run there.

---

Questions: [open an issue](https://github.com/techygeekshome/AppGeek/issues) or reach us
through [techygeekshome.info](https://techygeekshome.info).
