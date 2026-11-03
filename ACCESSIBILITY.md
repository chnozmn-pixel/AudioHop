# Accessibility

AudioHop exists so that changing the sound device does not mean navigating
several screens of Settings. Doing that in one keypress helps everyone, and it
helps most if you find pointing and clicking tiring or difficult. This page
says plainly what works today and what does not.

## What works

- **Everything has a keyboard path.** The picker opens on a hotkey you choose
  and is driven with the arrow keys, `1`-`9`, `Enter` and `Esc`; volume is on
  `←` `→` (`Shift` for 1% steps) and mute on `M`. The mouse is never required.
- **Per-device hotkeys.** You can switch straight to a named device without
  opening anything at all - no menu, no panel, no pointer.
- **No time limits.** Nothing closes on a timer. The picker stays until you
  choose, press `Esc`, or click elsewhere.
- **No motion or flashing.** The only animation is a short fade when the picker
  appears; nothing blinks, scrolls or flashes.
- **The windows use standard controls.** The setup wizard and the hotkey
  settings are ordinary Windows dialogs, so Narrator, NVDA and JAWS read them,
  `Tab` moves through them, and they follow your Windows font size.
- **15 languages**, chosen from your Windows language, including right-to-left
  layout for Arabic. You can correct any wording yourself with a `.lang` file
  without rebuilding.
- **The tray menu** is reachable with the Windows tray keyboard shortcut and
  read by screen readers like any other context menu.

## Known limitations

These are real, and we would rather say so than imply otherwise:

- **The on-screen picker is not exposed to screen readers.** It is drawn by the
  application rather than built from Windows controls, so Narrator and NVDA
  cannot announce the rows or the volume slider. Until that is fixed, the tray
  menu and per-device hotkeys do the same job and are fully accessible.
- **The picker ignores your Windows theme.** It is always dark with a blue
  highlight, and does not follow High Contrast themes or your system accent
  colour. The dialogs do follow the system theme.
- **The picker does not scale with Windows text size.** Its rows are a fixed
  height, so on a high-DPI display at a large scaling factor it may look small.
- **The picker's text has not been contrast-audited** against WCAG ratios.
- **Hotkeys need a modifier** (Ctrl, Alt, Shift or Win) plus one key, pressed
  together. There is no sticky or single-key alternative beyond what Windows
  Sticky Keys already provides - that does work with AudioHop.
- **Balloon notifications** report a switch; if you have turned Windows
  notifications off, that confirmation is not spoken or shown.

## Environments we test in

Windows 10 and 11, x64, with the keyboard only and with the mouse. Screen
reader testing is limited; reports from people who use one daily are
particularly welcome.

## Telling us about a barrier

Open an issue at
https://github.com/chnozmn-pixel/AudioHop/issues and say what you were trying
to do, what got in the way, and what you use (screen reader, magnifier, voice
control, keyboard only, high contrast). Accessibility reports are treated as
bugs, not as feature requests.

If writing an issue is itself a barrier, say so in any form you can; we would
rather have a short, rough report than none.
