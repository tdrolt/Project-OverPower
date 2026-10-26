# Fire sheets (Unity ignores this folder)

`make_fire.py` draws the Flamethrower's and the Rocket Cursor's fire: `Fire Body.png` and `Fire Core.png` in `Shared/FX/Textures`
(64 frames, 8x8). Run `python make_fire.py` here (numpy + Pillow); it overwrites both sheets. `params.json` is the bake-off
renderer's view of the fire; in the game the same numbers live in UiTheme (Fire Body / Core Over Life, divided by the glows and
written as sRGB) and the Arena Look (Flame Glow, Flame Body Glow, Flame Core Size, Flame Flicker Fps, Flame Lean and the jet and
burning-ground numbers).
