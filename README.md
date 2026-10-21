# Alien Invasion — Operation Deep Descent

A 3D FPS in C# (.NET 10) + OpenGL 3.3 (Silk.NET) + OpenAL. Fight down through 30 sublevels
of an overrun military bunker and destroy the alien portal at the bottom.
Every texture, model, sound effect and music track is generated procedurally at runtime — no asset files.

📖 **[Read the full game guide](GUIDE.md)**: controls, every weapon, upgrade, pickup, alien and boss, hazards, events and scoring.

## What's down there
- **Aliens:** skitters, grunts, spitters, troopers, cloaked stalkers, wraiths, brutes, overseers, plus
  **bloaters** (charge in and burst in acid — shoot them at range), **burrowers** (tunnel under the floor
  and erupt beside you) and **ceiling crawlers** (drop on you from above). Aliens also crawl out of
  **wall vents** — shoot a vent to seal it.
- **Mini-bosses** guard the elevator on sublevels 5/20 (Hive Queen), 10/25 (Warlord) and 15 (The Mind).
  The elevator stays locked until the boss dies; each drops a guaranteed weapon or upgrade.
- **Secret stashes** hold health, armor, ammo and goodies: damage amplifier, ammo backpack, night-vision
  and x-ray goggles, and **weapon upgrades** (one per weapon: magnum rounds, cluster grenades, twin rockets…).
- **Hazards:** red explosive barrels, steam vents, live cables, acid pools (deep levels).
- **Events:** lockdown ambush rooms (energy barriers + waves from the vents), power failures,
  and a self-destruct countdown on sublevels 28–29 once you hold every keycard.
- Each sublevel ends with a score breakdown and an S–D rank; best scores are kept in `scores.json`
  next to the save file.

## Run
    ./run.sh                  # builds if needed, then launches
    ./run.sh --level 10 --god # any flags below are passed through

## Controls
WASD move · mouse look · LMB/Ctrl fire · Shift sprint · E/Space use elevator · 1–0 weapons ·
wheel/Q/RMB cycle weapon · F flashlight · N night vision · X x-ray goggles · Tab automap · B bloom · M music · F11 fullscreen · Esc pause

## Debug / test flags
    --level N        start at sublevel N (with a depth-appropriate loadout)
    --god            invulnerable
    --weapon N       start holding weapon N
    --gentest        generate 600 levels and verify each is solvable
    --bot A B [--god]  headless bot plays levels A..B (keys, doors, combat, elevator/portal)
    --smoke SECS     automated input run, prints stats and exits
    --seed N         fixed run seed (same level layouts every launch)
    --noshadows      disable shadow maps (lights + flashlight)
    --nobloom        start with bloom off
    --goggles nv|xray  start with night vision / x-ray goggles switched on
    --yaw RADIANS    turn the starting view (for screenshots)
    --musictest DIR  render sample music tracks to WAV files
    --barreltest     set off barrels next to an alien/the player and shoot one; prints the results
    --featuretest    headless checks of aliens, vents, lockdowns, hazards, events, bosses, upgrades, gore, scoring
    --shot FILE.png [--shotframe N] [--shotmode play|map|gallery|secret|secretopen|beacon|beaconcrowd|beaconflash|behindwall|barrels|bossgallery|lockdown|vent|hazard|blackout|complete]   screenshot and exit

## Layout
- `Engine/` — GL wrappers + mesh builder, procedural textures, 2D/HUD renderer with built-in font,
  sound synthesis, procedural music (Music.cs, ported from the FPS2 project), OpenAL playback, PNG screenshot
- `Game/Level.cs` — generator: rooms + MST corridors, locked doors on path chokepoints, keys placed
  so each is reachable with the previous ones, elevator alcove, portal arena on level 30
- `Game/World.cs` — simulation: player, weapons, enemy AI (patrol → alert → chase/strafe/attack via
  flow field), projectiles, explosions, doors, pickups, portal/pylon finale; split into partial files:
  `WorldAliens.cs` (burrower/crawler/bloater), `WorldBoss.cs`, `WorldHazards.cs` (vents, lockdowns,
  hazards, power failure, self-destruct), `WorldFx.cs` (decals, gibs, hit markers, kill slow-mo)
- `Game/LevelDecor.cs` / `LevelHazards.cs` — props, secret rooms, explosive barrels, vents, hazards, lockdown rooms
- `Game/Scoring.cs` — end-of-level score, rank and best scores
- `Game/Renderer.cs` / `Models.cs` — lighting (32 dynamic point lights + flashlight + fog, rotating alarm
  beacons from sublevel 8, cube-map enemy shadows from up to 4 nearby lights, flashlight spot shadows, blinking console LEDs, HDR bloom in `Engine/Bloom.cs`), alien models,
  weapon view models, particles
- `Game/GameApp.cs` — window, input, game states, HUD, automap, menus, save/continue
