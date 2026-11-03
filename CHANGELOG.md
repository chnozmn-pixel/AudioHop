# Changelog

All notable changes to AudioHop are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/).

## [Unreleased]

### Planned

- Equalizer.

### Documentation

- Code of conduct, contributing guide, security policy and accessibility
  statement, plus issue and pull request templates.

## [1.0.0] - 2026-11-03

First release.

### Added

- Tray icon listing every active playback and recording device, with the
  current default ticked; click one to switch.
- On-screen picker: a borderless, top-most panel opened by a hotkey, with
  keyboard (arrows, `1`-`9`, `Enter`, `Esc`), mouse and wheel control.
- Volume slider and mute in the picker, following the highlighted device, with
  `←` `→` (`Shift` for 1% steps), `M`, drag and wheel.
- Configurable global hotkeys: open the picker, step to the next connected
  output, or switch straight to a named device. None are assigned by default.
- Three-step first-run wizard: language, hotkeys, per-device hotkeys and
  "start with Windows". Reachable later from the tray menu.
- Free-combination finder that reserves candidates through `RegisterHotKey`
  before suggesting one, plus a live free/taken indicator.
- 15 built-in languages chosen from the Windows UI language, overridable in
  settings, with right-to-left layout for Arabic. User-supplied `.lang` files
  in `%APPDATA%\AudioHop\lang\` add or override translations.
- Start-with-Windows toggle (per-user `Run` entry).
- Settings stored as plain text in `%APPDATA%\AudioHop\settings.ini`.
- Optional trace log via the `AUDIOHOP_LOG` environment variable.
- Application artwork: the full tile for the executable and its windows, and a
  switch-only version for the tray, where the tile goes muddy at 16 px.
- Terminal installer and uninstaller, Scoop manifest and winget manifests.

### Notes

- Switching sets all three roles (Console, Multimedia, Communications).
- When Windows accepts a switch to a disconnected device but keeps playing
  through the previous one, AudioHop reports that instead of claiming success.

[Unreleased]: https://github.com/chnozmn-pixel/AudioHop/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/chnozmn-pixel/AudioHop/releases/tag/v1.0.0
