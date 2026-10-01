# KayKit Adventurers — mage source

Author: Kay Lousberg. Pack: **KayKit: Adventurers Character Pack 1.0**.

- Official repository: https://github.com/KayKit-Game-Assets/KayKit-Character-Pack-Adventures-1.0
- Official pack page: https://kaylousberg.itch.io/kaykit-adventurers
- Pinned repository revision: `672074b73ba276876a19e8816ecdc5241817ab47`
- Retrieved: 2026-09-30.
- License: CC0; the unmodified upstream `LICENSE.txt` is retained alongside this file. Attribution is optional; original authorship remains Kay Lousberg.

## Imported source files

| Local file | Upstream path | Purpose |
| --- | --- | --- |
| `Models/Mage.fbx` | `addons/kaykit_character_pack_adventures/Characters/fbx/Mage.fbx` | Generator input only; official animated mage source. |
| `Textures/mage_texture.png` | `addons/kaykit_character_pack_adventures/Textures/mage_texture.png` | Original mage atlas, used by the generated Built-in Standard material. |
| `LICENSE.txt` | `LICENSE.txt` | Original asset license. |

Only this character and its atlas are imported. No other characters, separate weapon assets, or full package archives are included in `Assets`. The original FBX is retained intact, including its authored animation takes and four embedded accessory objects. It is approximately 19.5 MB and is a reproducible authoring source, not a runtime dependency.

## Project adaptation

`Assets/Scripts/BoxLab/Editor/MageArtSetup.cs` generates the empty-handed pushing unit. It imports and samples the ordinary `Idle` animation at time zero; `Unarmed_Idle` is a raised-fist combat pose and is not used. The independent `Spellbook`, `Spellbook_open`, `1H_Wand`, and `2H_Staff` objects are removed from the temporary instance before baking. The eight character meshes, including the hat and cape, are retained.

The hat is uniformly reduced to 90% of its source size. The complete sampled figure is then uniformly fitted to a maximum 0.65-cell horizontal footprint and approximately 0.90-cell height, placed on the floor, and oriented South toward the fixed camera. Its original atlas and UVs are preserved. No external custom shader is required.

The generator creates a fresh static hierarchy, independently copies or bakes every mesh, and removes skin weights and bind poses. The result contains only transforms, MeshFilters, and MeshRenderers. There is no Animator, Animation component, Avatar, AnimationClip, SkinnedMeshRenderer, source mesh, or FBX reference in the generated prefab. Generation explicitly checks its asset dependencies and fails if a source FBX remains reachable.

Generated runtime files:

- `Assets/BoxLabArt/Resources/BoxLabArt/Pusher.prefab`
- `Assets/BoxLabArt/Materials/KayKitMage.mat`
- `Assets/BoxLabArt/Meshes/MagePusher/Mage_ArmLeft.asset`
- `Assets/BoxLabArt/Meshes/MagePusher/Mage_ArmRight.asset`
- `Assets/BoxLabArt/Meshes/MagePusher/Mage_Body.asset`
- `Assets/BoxLabArt/Meshes/MagePusher/Mage_Head.asset`
- `Assets/BoxLabArt/Meshes/MagePusher/Mage_LegLeft.asset`
- `Assets/BoxLabArt/Meshes/MagePusher/Mage_LegRight.asset`
- `Assets/BoxLabArt/Meshes/MagePusher/Mage_Hat.asset`
- `Assets/BoxLabArt/Meshes/MagePusher/Mage_Cape.asset`

The atlas is a runtime texture dependency; `Mage.fbx` is generator-only. The full art generation command calls `MageArtSetup.Build()` before creating palette thumbnails so the editor and game show the same character. No source animation plays at runtime: gameplay movement is the existing deterministic grid movement of this static visual.
