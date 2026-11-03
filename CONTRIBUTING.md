# Contributing to AudioHop

Thanks for taking an interest. AudioHop is deliberately small: one tray
utility, two source files, no dependencies beyond what Windows already ships.
Changes that keep it that way are the easiest to accept.

By taking part you agree to the [Code of Conduct](CODE_OF_CONDUCT.md).

## Ways to help

- **Report a bug.** Use the bug report template. The one thing that helps most
  is the log: set `AUDIOHOP_LOG=1`, reproduce the problem, and attach
  `%TEMP%\audiohop.log`.
- **Suggest a feature.** Open a feature request and say what you are trying to
  do, not only what to build. Small, keyboard-friendly additions fit best.
- **Fix or translate.** Translations are especially welcome - see below.
- **Report a vulnerability.** Not through an issue, please: see
  [SECURITY.md](SECURITY.md).

## Building

No SDK, no NuGet, no MSBuild - it builds with the C# compiler that comes with
Windows:

```bat
build.cmd
```

That compiles `AudioHop.cs` and `Languages.cs` into `AudioHop.exe` with
`csc.exe` from `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319`, embeds
`assets\AudioHop.ico` as the executable icon and `assets\tray.ico` as a
resource. Target: .NET Framework 4.0+, WinForms, x64.

If you changed the artwork, run `tools\make-icon.ps1` first to rebuild both
`.ico` files from the SVGs in `assets/`.

## Testing a change

There is no test suite; the app talks to real audio hardware, so changes are
checked by hand. Before opening a pull request, please confirm:

- The tray menu lists your playback and recording devices and ticks the one in
  use.
- Switching from the menu actually moves the sound, and the balloon says so.
- The picker opens on its hotkey, closes on `Esc`, toggles on a second press,
  and closes when you click elsewhere.
- Arrow keys, `1`-`9`, `Enter`, the volume keys and `M` all behave.
- The setup wizard runs: delete `%APPDATA%\AudioHop\settings.ini` to see it
  again.

Say in the pull request which of these you exercised, and on which Windows
version.

## Translations

Every string lives in `Languages.cs`, in one table per language. To add a
language, copy the English block, translate the values, and add the code to the
list of available languages. Keys you leave out fall back to English, so a
partial translation is still useful.

You do not have to rebuild to try wording: drop a UTF-8 file named
`<code>.lang` into `%APPDATA%\AudioHop\lang\` and it overrides the built-in
text.

Right-to-left languages go in the RTL list as well, so the windows mirror.

## Code style

Match what is already there rather than any external style guide:

- Four spaces, Allman braces, `var` where the type is obvious.
- Comments explain *why*, not what the line does. Keep them short.
- British or American spelling - just be consistent within a file.
- C# 5 language level (the compiler that ships with Windows); no newer syntax.
- Keep the executable small and the startup fast. No new dependencies.

## Pull requests

- One change per pull request.
- Say what it does and why, and how you tested it.
- Note anything you deliberately left out.
- Update `README.md`, `README.tr.md` and `CHANGELOG.md` when behaviour changes.

Both readmes are kept in step; if English is your only language, say so and the
Turkish side will be handled.

## Releases

Maintainer notes, for the record: `release.ps1 -Version x.y.z` rebuilds the
executable, computes its SHA256 and stamps the Scoop and winget manifests.
Tagging and uploading stay manual.
