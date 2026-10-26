# Audio licenses

The game ships **no downloaded sound files**. Every sound effect (pickup, mine, craft, build, enemy killed,
player hurt, eat, run success / error / out of energy, achievement) is synthesized procedurally at runtime
by code in `Assets/_Game/Scripts/Audio/ProceduralSounds.cs` (simple waveforms and filtered noise with an
envelope, 44.1 kHz mono).

- Source: own work of the CodeRogue project, generated in code.
- License: CC0 1.0 Universal (public domain dedication) — https://creativecommons.org/publicdomain/zero/1.0/

If you override a sound with a clip in `AudioConfig`, put the file in this folder and add a line here:
file name, author, source URL, license (CC0 only, e.g. kenney.nl or freesound.org CC0).

| File | Author | Source | License |
|------|--------|--------|---------|
| — (procedural, no files) | CodeRogue | `ProceduralSounds.cs` | CC0 1.0 |
