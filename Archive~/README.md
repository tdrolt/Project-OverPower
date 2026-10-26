# Archive~ (Unity ignores folders ending in ~)

Assets that nothing in the game referenced on 26 Oct 2026 (GUID scan from both scenes, every Resources folder, project
settings and the path strings in our code and tests), kept because they are our own early work or sounds picked for the
game. Each file keeps its `.meta`, so copying it back under `Assets/` restores the same GUID.

| Folder | What | Why kept |
|---|---|---|
| `Prefabs/Map/` | Buildings, Mountains, Nature, Props, Terrain1, Walls: the first map, built from the LowPolyFPS2 pack | the original level, before the primitive arena (their pack parts are deleted, so they open with missing meshes) |
| `Prefabs/prefabs/` | the first prototype: Enemy, EnemyBullet, SinglePlayer, PBRCharacter, Shell, lobby/menu/score/leaderboard canvases, a second `chat manager` | early single-player and UI work |
| `Prefabs/Shell.fbx` | the old shell casing model | used by the prototype's Shell prefab |
| `Sources/Rhinos.fbx` | a rhino model, never placed | unknown origin, small |
| `Sources/audio/`, `Audio/` | `capture 2`, `success capture 1`, two magic impact sounds, the dark atmosphere track | sounds chosen for the game but not wired in (see the sound list in the design folder) |
| `textures/` | `Circle.png`, `ColorPalette.png` | small, own |

Deleted without a copy (third-party, re-downloadable, nothing referenced them): the Vefects Zap VFX pack (78 MB), the
Vibrant Effect Textures pack (all but texture 28, which the old bullet explosion uses), 215 of the 224 LowPolyFPS2 files,
the TextMesh Pro examples nothing used, the Photon PUN/Chat/Realtime demos, the SerializableDictionary demo, Unity's
TutorialInfo and Readme, two unused team materials (`PBR team 2/3`) and an old flame-tongue mesh. All of it is in git
history before the commit "Unity project: remove unreferenced assets".
