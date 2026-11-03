## What this changes

<!-- One or two sentences. Link the issue if there is one: Fixes #123 -->

## Why

<!-- What was wrong or missing. Skip if the "what" already says it. -->

## How it was tested

<!-- AudioHop has no automated tests, so say what you actually did. -->

- Windows version:
- Devices used:

<!-- Tick what applies; delete the rest. -->

- [ ] Built with `build.cmd` without warnings
- [ ] Switched between outputs from the tray menu
- [ ] Opened the picker by hotkey and switched with the keyboard
- [ ] Changed volume and mute from the picker
- [ ] Ran through the first-run wizard with settings removed
- [ ] Checked a right-to-left language (ar)
- [ ] Rebuilt the icons with `tools\make-icon.ps1`

## Checklist

- [ ] No new dependencies, and the executable is still around 260 KB
- [ ] C# 5 syntax only (the build uses the in-box .NET Framework compiler)
- [ ] New or changed strings go through `Languages.cs`, with English filled in
- [ ] `README.md` and `README.tr.md` both updated, if behaviour changed
- [ ] `CHANGELOG.md` updated under Unreleased
