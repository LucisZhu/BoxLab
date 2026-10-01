# KayKit Dungeon: selected CC0 source assets

Creator: Kay Lousberg — https://kaylousberg.com/

Current pack page: https://kaylousberg.itch.io/kaykit-dungeon-pack

Downloaded from the creator's public Dungeon Remastered repository (the successor to the separate legacy Dungeon pack):
https://github.com/KayKit-Game-Assets/KayKit-Dungeon-Remastered-1.0

Pinned revision: `b0ca9bd96a8072ab36a3a5464f00ed1e06a16d07`.
Retrieved on 2026-09-30. These are the publicly distributed 1.0 model sources at that revision, not a claim that the complete current 1.1 or paid Extra/Source editions are included.

## Included files

All model and texture paths below are relative to `addons/kaykit_dungeon_remastered/Assets/` in the pinned upstream repository.

| Local path | Original path | Used by |
|---|---|---|
| `Models/floor_tile_small.fbx` | `fbx/floor_tile_small.fbx` | Floor and SurfaceFloor prefabs |
| `Models/floor_tile_small_broken_A.fbx` | `fbx/floor_tile_small_broken_A.fbx` | FragileFloor prefab, genuine modelled stone cracks |
| `Models/barrier_half.fbx` | `fbx/barrier_half.fbx` | LowWall prefab |
| `Models/box_small.fbx` | `fbx/box_small.fbx` | Crate prefab |
| `Models/wall_doorway.fbx` | `fbx/wall_doorway.fbx` | PortalFrame prefab, an actual open arch |
| `Textures/dungeon_texture.png` | `texture/dungeon_texture.png` | Shared original gradient atlas |
| `License.txt` | Repository root `LICENSE.txt` | Original license, retained verbatim |

The original files are unmodified. No complete pack, unused candidate models, Pro/Extra content, or render-pipeline package is included. `wall_doorway` has an actual stone opening, but its FBX also includes a separate `wall_doorway_door` wooden-door child that is absent from the OBJ preview. `ArtSetup` explicitly removes that child before fitting the stone frame or deriving its widened mesh. No material slot is removed: the door and the stone both use the same atlas.

## Unity presentation

`ArtSetup` generates optional prefabs with the Built-in Standard shader and the original atlas/UVs. Both stone slabs are uniformly scaled from 2 × .15 × 2 to .86 × .0645 × .86; their tops are at y=.043. Functional inlays are separate objects, retaining the textured stone border. The low stone barrier is fitted to .95 × .46 × .23. The closed wooden box is uniformly scaled to .64 cubed.

The portal frame is fitted to .86 × 1.10 × .215, with its base at y=.043. Its sole generated derived mesh (`PortalFrame-aperture-0.asset`) widens the jamb opening from the source's half-width opening to .70, keeping the .86 outside width and all original UVs. A horizontal cross-section of the source triangles confirms the curved arch still has about .665 clearance at the .64 box's top (y=.687), preventing its upper corners from clipping the arch. The generator also performs nine two-sided triangle-ray checks on the actual imported and derived FBX to verify the box-sized opening is unobstructed. Source FBX geometry remains unmodified. A separate transparent emissive field and floor arrow indicate the portal's role and front direction. All these transforms affect presentation only.

The KayKit Adventurers mage and Kenney Particle Pack retain their own source directories and licenses. Deleting the complete `Assets/BoxLabArt` directory activates the game's procedural art fallback.

## License

Creative Commons Zero (CC0 1.0). See the unmodified `License.txt`.
https://creativecommons.org/publicdomain/zero/1.0/

Credit is not required; attribution is retained here for provenance.
