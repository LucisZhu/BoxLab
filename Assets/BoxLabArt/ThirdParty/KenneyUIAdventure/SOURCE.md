# Kenney UI Pack - Adventure

- Author: Kenney, https://kenney.nl/
- Official asset page: https://kenney.nl/assets/ui-pack-adventure
- Download: https://kenney.nl/media/pages/assets/ui-pack-adventure/9a877376bc-1723597274/kenney_ui-pack-adventure.zip
- Retrieved: 2026-09-30.
- ZIP SHA-256: `982e8ab66842509ee9214f9c2038e594cbe029f19d1dc177fecaad3b41a67ed3`.
- Original `License.txt` identifies UI Pack - Adventure 1.1, created 2024-08-13, Creative Commons Zero (CC0). The official page labels its first release 1.0.
- License: https://creativecommons.org/publicdomain/zero/1.0/ ; the original license is preserved alongside this file.

Only these nine unchanged PNGs from the ZIP's `PNG/Default/` directory are included:

| Original filename | Use |
| --- | --- |
| `panel_brown_dark_corners_a.png` | Primary wood buttons, including the metal corner brackets from the official sample |
| `panel_grey_bolts.png` | Secondary stone/metal buttons |
| `panel_grey_bolts_red.png` | Destructive action buttons with a red accent |
| `panel_grey_bolts_dark.png` | Window/panel framing |
| `panel_brown_corners_a.png` | Optional parchment panels |
| `panel_grey_dark.png` | Recessed inputs and cards |
| `checkbox_brown_empty.png` | Unchecked settings control |
| `checkbox_brown_checked.png` | Checked settings control |
| `scrollbar_grey.png` | Scrollbar and slider handles |

Runtime addresses are `BoxLabUI/Adventure/<filename without extension>`.
`WorkshopTheme.cs` draws panel corners with IMGUI nine-slice borders; it creates a small set of tinted copies at runtime for readable dark panels and normal/hover/pressed/disabled button states. It preserves the source linework, highlights, rivets and corner brackets. Original PNG files are unmodified. No shader or rendering-pipeline package is required.

The complete original ZIP and extracted candidate assets stay outside Unity's Assets directory in `ArtResearch/KenneyUIAdventure/`. Preview and sample images are not imported. No third-party font is imported; the app supplies its Chinese-capable font to the theme.

Deleting this package is supported: the theme falls back to simple generated panels and buttons. The normal shipped appearance uses the nine original Adventure PNGs.
