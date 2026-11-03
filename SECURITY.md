# Security policy

## Supported versions

Only the latest release is supported. Fixes go into a new version rather than
back into older ones.

| Version | Supported |
| --- | --- |
| 1.0.x | Yes |

## Reporting a vulnerability

Please do not open a public issue for a security problem.

Report it through
[GitHub's private vulnerability reporting](https://github.com/chnozmn-pixel/AudioHop/security/advisories/new).
Only the maintainer can see it, and you can be credited in the advisory if you
want to be.

Useful to include: what an attacker could do, the steps to reproduce it, the
AudioHop version (`AudioHop.exe` properties) and your Windows build.

This is a small project run by one person: expect a first reply within a week.
If a fix is needed it ships as a new release, with an advisory describing the
problem once the fix is out.

## What AudioHop does on your machine

Worth knowing when judging a report:

- It runs as your own user. It never asks for administrator rights.
- It writes only to `%APPDATA%\AudioHop` (settings and any `.lang` files you
  add) and, if you turn on "start with Windows", one value under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- It makes no network connections of any kind. No telemetry, no update check.
  The installer script is the only thing that reaches the internet, and only
  to api.github.com and the release download.
- It changes the default audio endpoint through `IPolicyConfig`, the same
  undocumented COM interface the Windows sound panel uses, and reads volume
  through `IAudioEndpointVolume`. No drivers, no hooks, no injection.
- Global hotkeys use `RegisterHotKey`. AudioHop does not install a keyboard
  hook and does not see what you type.
- The released `AudioHop.exe` is not code-signed. Verify it against the SHA256
  in `packaging/scoop/audiohop.json` for that version if you want certainty
  about what you downloaded.

## Out of scope

- Another program claiming a hotkey first. AudioHop reports it and carries on.
- Windows accepting a switch to an unplugged device and ignoring it; that is
  Windows behaviour, and AudioHop tells you when it happens.
- Anything that needs administrator rights, or physical access to an unlocked
  machine, to exploit.
