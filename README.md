# Lost Eden Client

Open source [Anarchy Online](https://www.anarchyonline.com/) client, rebuilt in Unity.

## Requirements

- **Unity 6000.4.9f1** (Unity 6)
- High Definition Render Pipeline (HDRP)
- A local Anarchy Online client install (for game data)

## Design philosophy

Lost Eden does not ship Anarchy Online art. Every AO asset — meshes, textures, terrain, animations — is loaded at runtime from the player's installed client.

Playfield visuals are customized at load/runtime through tweak files, not baked into the client. Tweaks can change how content is interpreted (for example, treating water as ocean vs pool) or add geometry and other presentation overrides without modifying the original game data.

## Progress

Lost Eden can connect, load outdoor playfields, and move around. Core rendering and locomotion are in place; combat, indoor zones, and several movement modes are still ahead.

| Area | Status |
|------|--------|
| Outdoor terrain & statics | Terrain loads for all outdoor playfields; statics done. Terrain mipmap shader still needed. Outdoor collision shells stream via SurfaceResource (cell+2). |
| Indoor playfields | Not started |
| Characters | Meshes and attachments done; custom animator still needs work |
| Player movement | Strafe, run, turn, and jump done. Flying, roots, lounge, sneak, and crawl not started |
| NPC movement | Done |
| Combat | Not started |

Detailed feature tracking lives in [`PROGRESS.md`](PROGRESS.md).
