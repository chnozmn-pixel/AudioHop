# Artwork

| File | What it is |
| --- | --- |
| `logo-original.svg` | The logo as delivered, untouched. |
| `logo.svg` | The same lockup, cropped to its bounding box. |
| `icon.svg` | The tile alone - the app icon. |
| `tray.svg` | The switch alone, used for the tray, where the dark tile turns to mush at 16 px. |
| `wordmark.svg` | The "audioHOP" lettering alone, white. |
| `AudioHop.ico` | Built from `icon.svg`: 16, 20, 24, 32, 40, 48, 64, 128, 256. |
| `tray.ico` | Built from `tray.svg`: 16 to 48. |

The `.ico` files are generated - run `tools\make-icon.ps1` after changing an
SVG. `build.cmd` embeds `AudioHop.ico` as the executable icon and `tray.ico` as
a resource the tray reads at the size Windows asks for.

The wordmark is white, so it needs a dark background; `docs/logo.png` is the
lockup on the dark plate used in the readme.
