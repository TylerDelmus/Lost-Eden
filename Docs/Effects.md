# Nano / gfx effects: reverse-engineering notes and port guide

This is the working reference for porting Anarchy Online's effect system (the `gfxtweak` controls
that draw nano casts, projectiles, hits and buffs) into Lost Eden. It collects what has been
recovered from the stock client, how the port is laid out, and how to add the next control. Read it
before touching `Assets/Scripts/Effects/`.

Last updated 2026-09-22.

---

## 1. Ground rules

- **Stock is the only truth.** Behaviour is recovered from the original client binaries
  (`Gamecode.dll`, `DisplaySystem.dll`) and its data (`gfxtweak.bin`, the RDB). Anything in the port
  that was written before a control was re-derived from stock is a lead, not a fact. The previous
  developer's code has several confident but wrong guesses (see §9).
- **Faithful, not "looks about right".** Keep stock's field meanings, its per-call constants, its
  float truncation (`_ftol`) and its quirks, even odd ones such as a negative sprite size or a
  1-frame stale sprite record. When the port has to deviate (Unity vs D3D7, HDRP, GPU skinning),
  say so in the code comment and in §9.
- **Put stock maths in a Unity-free `*Sim` class** so `Tests/Effects` can lock it down with plain
  xUnit. The `GfxControl*` class only binds it to a locator and draws it.
- **Cite addresses.** Every recovered rule in a doc comment names the stock function or instruction
  it came from (`100fa9e4`, `FUN_100517f3` and so on), so the next person can re-check it.
- **Verify live.** A control only counts as *Verified* (§8) once it has unit tests and has been
  watched in GfxTest against the expected stock behaviour, with numbers from a probe.

---

## 2. Where things are

### Stock client
Paths below are relative to your own Anarchy Online install, written `<AO>` (whatever folder
holds `Gamecode.dll`).

| What | Where |
|---|---|
| Effect templates | `<AO>/Setupf/gfxtweak.bin` (`int count`, then per record `int id, int typeCode, int n, n × 4-byte fields`) |
| Control logic | `<AO>/Gamecode.dll` (image base 0x10000000) |
| Visuals / rendering | `<AO>/DisplaySystem.dll` |
| RDB (nano stats, anims) | read through `Assets/Packages/AODB.1.0.6`, pointed at `<AO>` |

### Tooling
- **Ghidra** (12.1.3 was used) with the **ReVa** MCP plugin. ReVa serves MCP only while the Ghidra GUI
  is open, on the port set in its plugin options. Import `<AO>/Gamecode.dll` and
  `<AO>/DisplaySystem.dll`; ReVa addresses them by project path (`/Gamecode.dll`,
  `/DisplaySystem.dll`). Without PyGhidra, ReVa's `run-script` fails.
- **Disassemble locally with Python capstone + pefile.** It's faster than the decompiler and correct for
  x87. The decompiler garbles x87 operand order, so take every load-bearing operand from the
  disassembly. A throwaway wrapper that prints a range from either DLL is enough:
  `pefile.PE(path).get_memory_mapped_image()` + `capstone.Cs(CS_ARCH_X86, CS_MODE_32)`, starting at
  `address - 0x10000000`. Annotating `qword/dword ptr [const]` operands with their float value helps a lot.
- **gfxtweak dumper.** Parse the format above and walk children (Meta fields 0-9, Sequencer
  `2+3k`, Delay field 2, Scatter field 10, Spell1 fields 27-32).
- **AODB console tool** (net8, referencing `AODB.dll` + `AODB.Common.dll` +
  `System.Text.Encoding.CodePages`): `new RdbController(<AO>)`,
  `Get<NanoObject>(id).Stats` for nano stats, `RecordTypeToId[1010003]` for every CATAnim (notes in
  `AnimationIdentifiers`). Redirect stdin from `/dev/null` when running it from Bash.
- **RTTI to find a class.** Search the image for `.?AV<ClassName>@@`; the type descriptor is 8
  bytes before it; the complete-object locator points at the descriptor at +0xc; the dword after a
  COL pointer in `.rdata` is the vftable. Import names (`pe.DIRECTORY_ENTRY_IMPORT`) resolve the
  DisplaySystem calls Gamecode makes.
- **Unity probes.** Through the Unity MCP `RunCommand`: an `EditorApplication.update` callback that
  finds controls in `GfxTest_DEV.Handler.LiveControls`, logs per frame to a file, and
  `ScreenCapture.CaptureScreenshot` for pictures. See §7.

---

## 3. Stock architecture

### 3.1 Controls and the handler
- `_EffectHandler_t::RunFunction` (`Gamecode 100d2423`) calls every control's Process once per
  engine frame, then deletes the ready ones. A control is never drawn on the frame it becomes ready.
- `_GfxControl_t::Process` (`100d2a86`) is called at the top of every subclass Process:
  - The **first call only arms**: age 0, dt 0. The subclass body still runs after it, with age 0.
  - After that, age += dt (dt is clamped to 0.033 on the first real frame, which is a clamp and not
    a frame rate).
  - **Expiry**: when `0 <= duration < age` the control is marked ready. Some subclasses catch that
    and extend themselves (Stars case 3, Electra mode 0/1).
- Common vftable slots (compare vftables via RTTI to confirm per class):

  | Slot | Meaning |
  |---|---|
  | 0 | destructor |
  | 1 | **Process** |
  | 4 / 5 | locator helpers (often `101064f7` / `1010654f` on `+0x2c`) |
  | 6 | **TerminateGracefully** (class-specific: ready now, start a fade, stop spawning, …) |
  | 8 | **SetDuration** (`+0x10 = seconds`; a no-op stub `10079931` on some classes) |
  | 10 | **NextState** (base `100a76f9` = TerminateGracefully; Spell1 overrides) |
  | 11 / 12 | SetStartColor / SetStopColor (A,R,G,B floats) |
  | 13 | set colour from packed ARGB |

- Common control fields: `+0x0c` age, `+0x10` duration, `+0x14` ready flag, `+0x2c`/`+0x30`
  locator or visual pointers (varies by class).
- `CreateEffect2` has many overloads (`100d1f1b`…`100d23ce`). The dynel one (`100d2070`) returns 0
  for effect id **49999 (0xc34f), stock's "no effect"**. `CreateEffect2(id, caster, target, attach)`
  only builds Spell1 (`100d1705`).
  `CreateGfxControl(id, hitLocation)` builds hit-location controls (Tracer1 `100fecea`, Plasma
  `100ec5ae`, Tracer4 `101002e0`, Stars `100f7d4a`).

### 3.2 Locators
- Template field 0 holds the locator flags: **bit 0 track** (follow the attach every frame),
  **bit 1 local mode**, **bit 2 rotation order** (ZYX). Fields 1-3 are the offset and 4-6 the rotation.
- Two position getters, used together so the result is the same in both modes:
  - `GetPosition` `10106306`: the locator position **in local mode**, zero in world mode. This is
    what the *visual* is placed at.
  - `FUN_1010640a`: zero in local mode, the locator position **in world mode**. This is what
    *sprite positions* add.
- Attach ids (`EffectAttachIds`): 0 = CAT mesh RRefFrame (not the feet), 1000 = Bip01 Pelvis
  (bones are `1000 + n`), 1003 = Bip01 Spine2_ac, 1006 = Bip01 Head_ac, 2000 = right hand
  attractor, 2001 = left hand attractor (`Attractor03_lefthand`), 2002 = head attractor. The string
  tables are at `0x102c63f8` / `0x102c63a8` (`FUN_10105c44`).
- Template field 7 is the template's own attach id. `CreateEffect2(id, dynel, 0)` leaves it to the
  template.

### 3.3 Hit locations
- The nano path calls `NewHitLocation(caster, 2001 LHand, target, 1006 Head, true)`.
- Getters: start `101054fe` (start locator → `1010640a`), end `10105534` (end locator, plus extra
  logic on `+0x20`/`+0x50` that hasn't been traced).
- Controls keep either the endpoints (Tracer1 / Tracer4 read them once) or the hit location's id
  and look it up again (`100cdfd9`, Stars case 19 on every spawn; Plasma every Process).
- Port: `EffectHitLocation.TryGetEndpoints`. Its fallbacks and "hold" model are the previous
  developer's. Stock's hit-location lifetime (when it is deleted) is **not traced** (§9).

### 3.4 Visuals (DisplaySystem)
All the visuals seen so far draw **FVF 0x142** (XYZ | DIFFUSE | TEX1): position, one D3DCOLOR,
one UV. They use SRCALPHA/ONE additive blending unless noted, **no Z write, no culling**, and
texture × vertex colour.

| Visual | Used by | Notes |
|---|---|---|
| `GfxVisualDiaBill` | Stars | 0x20-byte `Sprite_t` (pos, w, h, D3DCOLOR, frame, visible). A camera-facing quad of full size w×h, atlas cell `frame % cols, frame / cols` from the top-left. Additive unless starType 25. Build `FUN_1001105e`. |
| `GfxVisualFlareType0` | Flare, Tracer1 | Sprite pool: `Init 10013c2b`, `InitSpriteDefault 10012fd0`, `NewSprite 100130a4`, `ProcessSprites 100131f3`, quad `1001364b`. Sprites are segments with two ends. |
| `GfxVisualSprite2Type0` | Sparks, Nano0/1 | Pooled sprite emitter visual. |
| `GfxVisualSprite2Type2` | Spell1 window 2 | Clears its list after drawing (`10027df8`), so it is rebuilt every frame. |
| `GfxVisualPlasma` | Plasma | ctor `1001b8f7`, render `1001bbf2`. A 75-segment camera-facing strip. |
| `GfxVisualCord4` | Cord, Tracer4 | A link ribbon: render `1000f072`, geometry `1000f040`, side vector `1000ee68`. See `Cord4Strip`. |
| `GfxVisualElectra` | Electra | ctor `100116ba`, `Init 10011cd5`, build `100118d3`. 0x30-byte sprite: pos, axis A, axis C, D3DCOLOR, frame, visible. Quad at `pos ± A/2 ± C/2` (oriented in 3D, not camera-facing). Its third ctor argument picks the blend: true gives SRCALPHA/ONE, false gives SRCALPHA/INVSRCALPHA. |

**UVs:** D3D's `v` runs down from the top row; Unity's runs up. An atlas cell (col, row) is
`u ∈ [col/cols, (col+1)/cols]`, `v_unity ∈ [1-(row+1)/rows, 1-row/rows]`. Material
cols/rows come from `EffectMaterialTable` (stock `_MaterialManager_t::GetMaterial`, indexed by
template field 9; −1 = untextured).

### 3.5 Numbers and conventions
- **`_ftol` (`1013f240`) truncates toward zero.** Frames, spawn counts and colour channels all go
  through it. **FISTP** (Tracer4's colour) rounds to nearest-even; for example `FISTP(c·255 − 0.49999)`.
- **Colour ramp** `FUN_10108663` (loaded by `FUN_101085de` from 8 fields, start A,R,G,B then end
  A,R,G,B): per channel `_ftol((start + t·(end−start))·255)`, packed ARGB with plain shifts. It is
  **not clamped**, so t outside 0..1 spills into neighbouring channels. Port: `StockColorRamp.Eval`.
- **Float constants stored as doubles.** Many constants are floats widened into `qword`s
  (0.899999976…, 15.989999771…). Use the exact double when replicating.
- **x87:** `fcompp` + `test ah, 5` + `jp` jumps when ST0 ≥ ST1. The `fsubp st(1)` / `fsubrp`
  mnemonics as printed by capstone: check with a known-sign result (a size that must be positive,
  say) before trusting a direction.
- **`rand()`** is the MSVC CRT LCG (`state = state·214013 + 2531011; return (state >> 16) & 0x7fff`),
  `StarsCase3.MsvcRand`. It's imported at `[0x10154ae0]`.
- `_GfxControl_t::GetRandomPointInSphere` `100d316a`: each axis is `(rand() & 0x7fff)/16384 − 1`,
  redrawn until inside the unit ball.
- `100d3005`: a **random unit vector from a shared 2048-entry table** built once from `rand()`
  (rejection-sampled in the ball, normalised), walked with `idx = (idx + seed) & 0x7ff;
  seed = (seed + 7) ^ idx`. It can't be reproduced exactly, so the port draws a fresh vector with
  the same distribution.
- `_GfxControl_t::FindPerpendicular` `100d3363` is `Tracer1Sim.FindPerpendicular`.
- **Quaternion multiply `1007c5e5(a, b)` computes `b ⊗ a` (Hamilton)**, reversed. So
  `Q.mul(q).mul(Q⁻¹)` is `Q⁻¹ q Q`, which rotates by **−angle**.
- Vector helpers: `100873ae(out, s, v) = v·s` (cdecl), `10023b81` add, `10003974` subtract,
  `10004734` add (DisplaySystem), `1003e057` in-place cross `this = this × arg`, `100439aa`
  set length, `1003e0fd` 1/length.

### 3.6 Frame rate
- Stock has no frame cap (only VSync) and **many Process bodies take no delta**. Spawn budgets,
  springs and step counts are per call. Ported verbatim, they speed up with Unity's frame rate.
- The port replays those bodies **call for call at `EffectFrameRate.StockProcessHz` = 30**, with
  `TakeFixedSteps` (carry, cap at 8 steps per frame). This rate is **UNVERIFIED** (§9).
- The drawing is **interpolated between the last two replayed steps** (`StarsCase3.Blend`: position
  and size, not when a slot was recycled). Without it, a 30 Hz swarm looks steppy at 140 fps.
- Bodies driven by age (Flare, Electra spawn counts, Tracer1, Plasma) just run every frame with
  the real age.

---

## 4. The nano effect flow

A nano has five effect stats. GfxTest and coverage show them as the dots C, T, I, H, B.

| Stat | Name | When |
|---|---|---|
| 428 | `casteffecttype` | on the caster, while casting (a Spell1) |
| 419 | `tracereffecttype` | the projectile, caster to target |
| 414 | `impacteffecttype` | on the target when the release clip ends |
| 361 | `hiteffecttype` | on the target, right after the impact |
| 413 | `effecttype` | **the buff's own effect**, on the recipient while the buff runs |

Related stats: 8 `timeexist` (the buff time in centiseconds, 1800 = 18 s), 0 `flags`
(bit 0x10000 lets a buff effect be created), 377/378 (cast target/self anim, whose default isn't
traced). **49999 = no effect.**

### 4.1 Cast, tracer, impact, hit
1. Cast starts: Spell1 (stat 428) on the caster (`CreateEffect2(id, caster, target, attach)`),
   and the cast clip plays. Stock sets `SetDuration(6000)` (`CharCastNano_t`, `1007b6d7`); Spell1's
   SetDuration is a stub anyway.
2. The cast's result arrives (`CharCastNano_t` `1007b1e3`): **NextState** on the cast effect
   (Spell1: re-times its windows, or does nothing when field 33 is set), then the **release clip**
   (spell-dir / spell-self) plays.
3. **Anim notes:** each CATAnim carries named notes (`AnimationIdentifiers`, time in ms).
   DisplaySystem `FUN_10075c84` maps a name to an event id by case-sensitive `strncmp` prefix,
   first match wins (port: `AnimNoteIds`). **`effect1start` = 0x42** in charstate 5
   (`CharCastNano_t`) launches the tracer (`FUN_100452d3` → `FUN_1004f989`):
   `NewHitLocation(caster, 2001, target, 1006, true)` then `CreateEffect2(tracer, hitLoc)`.
   Stock gives the tracer **no duration**; it runs its own course.
4. When the release clip ends: impact (414), then hit (361), each `CreateEffect2(id, target, 0)`.

Port: `CatAnimPlayer.NoteReached` → `EffectHandler.OnCastNote`; `FinishNanoCast` does the NextState;
`PlayfieldFactory.OnFinishNanoCasting` plays the release clip and then `PlayNanoHit`.
`EffectHandler.IsStockTracer` lists the stock-built tracers that get no duration and no cut-off.

### 4.2 Buffs
Traced in `Gamecode`:
- **CharacterAction 98 `SetNanoDuration`** (message: Identity = recipient, Target = nano,
  Parameter1 = caster instance, Parameter2 = **time in centiseconds**). The CharacterAction observer
  `FUN_10072959` → `FUN_1005d61f` (event 98 → `1005dd25`) → **`FUN_100517f3` (add buff)**:
  1. Time 0 → nothing (unsigned compare).
  2. End buffs the new one conflicts with (`FUN_1004e9cc` check, `FUN_10051041` removal). The rule
     isn't traced; the port only replaces the same nano.
  3. If the nano's `flags` stat lacks bit 0x10000 → no effect.
  4. `CreateEffect2(stat 413, dynel, 0)` (effect category 4) and `SetDuration(time / 100)`, integer
     division.
  5. The handle goes into the recipient's buff entry (+0x1c).
  Event 177 takes the same path with a different flag, but isn't identified yet.
- **Buff message** (`BuffIIR_c`, `100726e5`): when its first field (`Unknown1`) is **0**, the nano
  **wore off**. `FUN_10051041` prints the chat line, then `FUN_10050b90` →
  `TerminateEffectGracefully(entry+0x1c)`.
- A dynel that becomes visible with buffs already running recreates their effects
  (`FUN_1004f2bd`, duration = remaining/100.0 as float). `FUN_10051c0c` adds buffs from a list
  (full char update) with the same 0x10000 flag check. **Neither is ported.**

Port: `EffectHandler.AddNanoBuff` / `RemoveNanoBuff` (+ `*Visual` variants for GfxTest),
`PlayfieldFactory.OnSetNanoDuration` / `OnBuff`, `NanoEffectResolver.TryResolveBuff`,
`HasBuffEffect`, `TimeExist`. GfxTest applies the buff to the cast target after the hit, with the
nano's `timeexist` standing in for the server's time.

### 4.3 Worked examples (all verified in GfxTest)
| Nano | Cast | Tracer | Impact / Hit | Buff |
|---|---|---|---|---|
| 28612 | 46116 Spell1 | 45694 Tracer1 | hit 47299 → Stars #3 swarm 43168 | — |
| 28638 | Spell1 | 17600 Plasma (violet wavy strand, 1 s) | hit 45001 Stars #7 (ring expanding to 20 m in 1.9 s) | — |
| 266281 | 46240 Spell1 | 45502 Tracer4 (3 lightning ribbons, 1 s) | hit 45057 Deformer mode 1 (the body ripples ~3 s) | — |
| 150501 Nullity Sphere | 46188 Spell1 | 45597 Stars #19 (red spark trail, 1 s) | hit 49999 = nothing | 43452 Electra mode 1 (orange spark bubble, 18 s + 0.5) |

---

## 5. Control reference

Status legend (matches `EffectCoverage` and the GfxTest window):
- **Verified**: rebuilt from stock and checked live.
- **Unverified**: an earlier port, not rechecked.
- **Approx**: a stand-in.
- **Missing**: not drawn.

`EffectTypeCatalog` lists every typeCode with its stock class; `EffectTypeTags` has the constants.

### 5.1 Status table
| typeCode | Stock class | Port | Status |
|---|---|---|---|
| 0x7d7 | `_GfxControlMeta_t` | `GfxControlMeta` | Verified |
| 0xbbc | `_GfxControlSequencer_t` | `GfxControlSequencer` | Unverified |
| 0xbc5 | `_GfxControlDelay_t` | `GfxControlDelay` | Unverified |
| 0xbd5 | `GfxControlScatter_t` | `GfxControlScatter` + `ScatterSchedule` | Unverified |
| 0x3ed | `_GfxControlFlare_t` | `GfxControlFlareType0` + `FlareType0Sim` | Verified |
| 0x3ee | `_GfxControlFlare1_t` | `GfxControlFlare` | Unverified |
| 0x3f2 | `_GfxControlSpell1_t` | `GfxControlSpell1` + `Spell1Trail` | Verified when field 33 ≠ 0 |
| 0x3eb | `_GfxControlCord_t` | `GfxControlCord` | Verified only when it can't link (field 0 bit 1 clear, invisible in stock) |
| 0x3fa | `_GfxControlSparks_t` | `GfxControlSparks` | Unverified |
| 0x3ef/0x3f0 | `_GfxControlNano0_t` / `Nano1_t` | `GfxControlNano` | Approx |
| 0x3fb | `_GfxControlTracer1_t` | `GfxControlTracer1` + `Tracer1Sim` | Verified |
| 0x400 | `_GfxControlTracer4_t` | `GfxControlTracer4` + `Tracer4Sim` + `Cord4Strip` | Verified |
| 0x7d2 | `_GfxControlPlasma_t` | `GfxControlPlasma` + `PlasmaSim` | Verified |
| 0x7d4 | `_GfxControlStars_t` | `GfxControlStars` + `StarsCase3` / `StarsRing` / `StarsCase19` | Verified for starTypes 3, 7, 8, 19; others Unverified |
| 0xbb9 | `_GfxControlDeformer_t` | `GfxControlDeformer` + `DeformerSim` + `CatMeshDeformHost` | Verified mode 1; modes 0/4 Missing |
| 0x7d6 | `_GfxControlElectra_t` | `GfxControlElectra` + `ElectraSim` | Verified mode 1; modes 0/2 Missing |
| 0x7db | `_GfxControlHighlight_t` | `GfxControlHighlight` | Unverified |
| 0x7d5 | `_GfxControlSuns_t` | — | Missing |
| 0x7d1 | `_GfxControlSpiral_t` | — | Missing |
| 0xbbb / 0xbda | `_GfxControlShield_t` / `Shield2_t` | — | Missing |
| others | see `EffectTypeCatalog` | — | Missing / Approx |
| 0xfa0, 0x3e9, 0x3ea, 0 | audio, buff placeholders, none | — | stock draws nothing |

### 5.2 Meta (0x7d7)
Up to 10 child ids in fields 0-9, all spawned at construction. Field 9 = −1 means no child and an
infinite parent.

### 5.3 Flare (0x3ed): `FlareType0Sim`
ctor `100dd63c`, Process `100ddc6d`, loader `FUN_100dcedb`.
- Fields:
  - 0 flags (0x100 swaps the end scales, 0x200 readies once the pool is empty)
  - 8 duration
  - 10 sprites per second
  - 12/13 size0, 14/15 size1
  - 16-19 / 20-23 start/end colour
  - 24 `rand()` mask gating a spawn call
  - 25-28 angle ranges, 29/30 length
  - 31 burst / minimum pool
  - 32/33 end scales
  - 34/35 life range
- Each sprite is a **segment**: both ends start at the emitter and slide apart along one random
  direction.
- Field 0 bit 1 (local) keeps the sprites in the locator's frame; otherwise they stay where they
  were emitted.
- Colour can be overridden by a parent through slots 11/12 (Spell1 does this).

### 5.4 Spell1 (0x3f2), the cast effect
ctor `100f39ec`, vftable `1016dbec`, Process `100f3f59`, loader `100f2789`.
- Locators: A = caster left hand 2001, B = right hand 2000 (the attach override replaces both),
  C = target Spine2 1003, else head attractor 2002. If any of them fails, the control is ready at once.
- Fields:
  - 0 flags, 8 duration, 9 material
  - 10-13 colour A, 14-17 colour B
  - 18-26 window times
  - 27-32 child ids
  - 33 "no late windows"
- **Window 1** [f18, f20] builds children f29/f27 on hand A and f30/f28 on hand B, pushing colour A/B into
  them as start/stop colour (`100f29ea`), moves them to the hands every frame, terminates them from f19,
  and deletes them past f20.
- **Window 2** [f21, f22] is a sprite trail between the hands, redrawn every frame (`Spell1Trail`,
  `FUN_100f3d22` / `FUN_100f3205`).
- **Windows 3/4** run only when field 33 is 0. They're UNVERIFIED (`100f2d63` / `100f3070` / `100f2e49` /
  `100f31b1`).
- SetDuration is a stub. Slot 6 readies the control. NextState (slot 10, `100f2617`) re-times the
  windows, or does nothing when field 33 is set.

### 5.5 Tracer1 (0x3fb): `Tracer1Sim`
vftable `1016df3c`, loader `FUN_100fe3b0`, build `FUN_100fe9f9`, Process `100fe7e1` / `100fe706`.
- Start and end are read from the hit location **once**.
- `speed = min(100, 5·dist)`, so the flight takes `max(0.2, dist/100)` s. A segment shorter than 0.01 readies it.
- The locator is a fixed matrix with rows `cross(p, dir), p, dir, start` (p = FindPerpendicular),
  plus the template rotation and offset (`10106903` / `10106193`).
- Visual (`FUN_100fe845`): a FlareType0 of N×M sprites, one per link per ring (`FUN_100fe545`, fields
  19/20 and point pairs from field 21), ring r turned about local Y by 2πr/M. The sprites never move
  or fade and live 1 s.
- Process: `d = speed·age`; the control is ready once `d ≥ dist`; the visual sits at `start + dir·d`.
- Slot 8 is a no-op. Slot 6 sets duration = age.

### 5.6 Plasma (0x7d2): `PlasmaSim`
vftable `1016d7e4`, ctor `100ec5ae`, loader `100ec4c8`, Process `100ec2e6`.
- Fields: 9 material, 10-13 / 14-17 colour, 18 duration (field 8 is read and then overwritten).
- The hit location is re-read every Process, so the strip follows both ends. The colour ramp runs at age/duration.
- Visual: 75 segments (76 centre points), each pushed sideways by the sum of four cubed sine waves.
  The side axis is `cross(q, step)`, length 0.1, where q is the point one unit in front of the camera.
- A `rand()` with bits 0x7c0 clear nudges the phase of wave 0 or 2 by half a radian.
- Slot 6 sets duration 0.

### 5.7 Tracer4 (0x400): `Tracer4Sim`, `Cord4Strip`
vftable `1016e04c`, loader `100ffcac`, build `100fff4c`, Process `100ffa7b`.
- Three GfxVisualCord4 ribbons × 20 links.
- The locator rows are `cross(dir, p), dir, p`.
- Per ribbon k: `phase[k] += dt·12.56`. One `rand()` in four flips the twist step. Both are **statics
  shared by every Tracer4** (0 / 2.0734 / 4.1469, step 0.314).
- Per link, with y running from 0 in steps of 0.05:
  - `r = (sin(y·π·20/19) + 0.2)·f12`
  - the link sits at `(cos a·r, len·1.1·y, sin a·r)`
  - one `rand()` in four jitters it by up to r/2.
- Link size is f11. Colour is f13-16, FISTP-packed.
- Slot 6 readies it. Slot 8 is a no-op.
- Cord4 drawing: links newest to oldest, the oldest link gets no vertex, `side = right·d.y − up·d.x` in
  camera space, `v = 1 − life/lifeScale` (with flag 4) else 0.25.

### 5.8 Stars (0x7d4)
ctor `100f74ad`, Process `FUN_100f8491`, loader `FUN_100f72a9`, lazy init `FUN_100f7fb8`.
- Field 10 = **starType**. The Process jump table is at `0x100fc390` (29 cases); the lazy-init
  table is at `0x100f8454` / `0x100f8474`.
- Duration is **field 26** (field 8 is read, then overwritten).
- Visual: DiaBill, 128 slots.
- When expired: types whose early-table entry at `0x100fc374` is 0 get **terminating + 5 s drain**
  (`100f84ed`). The others (types 2, 7, 8, 10, 13, 16-20, and 26 and up) are just ready.
- Shared tail `100fc2f0`: terminating with nothing alive → ready.
- Stars never creates a light.

**Case 3** (`100f89dd`, `StarsCase3`): the orbiting swarm.
- Fields: 18-25 ramp, 28 size curve, 29 X/Z radius, 30 life ms.
- Per call:
  - spawn ≤ 2
  - spring `v += (o−p)·0.1; p += v·0.1`
  - size `(t+0.2)·c·(1−t²)`
  - frame `15 − _ftol((timer−age)·16/life)`, clamped
- The spawn call leaves the sprite record as it was.
- Replayed at 30 Hz and interpolated.

**Cases 7/8** (`100f9371`, `StarsRing`): 128 fixed directions.
- Type 7 is a flat ring `(sin a, 0, cos a)`. Type 8 spirals over a sphere `(sin 30t·sin t, cos t, cos 30t·sin t)`.
- `u = f31·age/dur`, `frac = u − floor u`, `s = √frac`.
- Size `f28·s + f29`. Position `locator + dir·f30·0.01·s`. Colour is the ramp at frac.

**Case 19** (`100fa9e4`, `StarsCase19`): the tracer spark trail. It's built from a hit location (ctor
`100f7d4a` stores the hitloc id at +0x34 and a locator at the world origin).
- Per call:
  - `p = age/dur`
  - 15 spawns
  - each spark is **static** at `end·p + start·(1−p) + ball·f29`
  - life `f30/1000·((rand()&0x7ff)·1e-4 + 0.9)`
  - size `f28·(1 − (1−2t)²)` (can be slightly negative early; drawn at |size|)
  - frame `15 − _ftol(t·15.99)`
  - colour is the ramp at t
- No hit location → no spawns.
- All 173 type-19 records have field 0 = 5 (world mode).
- Handled as a stock tracer (no SetDuration).

Other starTypes are still the previous developer's approximations. By nano count, the biggest are:
tracer types 17/18/16 (~1k nanos each) and hit types 22/6/10/11.

### 5.9 Deformer (0xbb9) mode 1: `DeformerSim`, `CatMeshDeformHost`
vftable `1016c6bc`, loader `100d823f`, Process `100d7caa`, vertex callback `FUN_100d776f`, slot 6 `100d7742`.
- Fields: 8 duration, 10 mode, 11 peak, 12 fade-in, 13 fade-out, 14 rate, 15 amplitude.
- **Envelope:**
  - Rises `peak·age/fadeIn`.
  - Once `0 < dur && dur − fadeOut < age`, it terminates itself: the envelope becomes the peak and fades
    over fadeOut.
  - The control is ready when the envelope reaches ≤ 0.
- **Mode 1 wobble:** each skinned vertex moves along its skinned normal by
  `sin²(((b.x + 13T)·13 + (b.y − 11T)·17 + b.z·T·19)·11 + T)·amp·env`, with `T = rate·age` and b the
  vertex's source position.
- Port:
  - Stock skins on the CPU and exposes a vertex callback (`CATRender_t::RegisterVertexProcessCallback`).
    Unity skins on the GPU.
  - So `CatMeshDeformHost` (`[DefaultExecutionOrder(20300)]`) BakeMeshes each SkinnedMeshRenderer,
    pushes the vertices, draws with `Graphics.DrawMesh` and sets `forceRenderingOff` on the original.
    BakeMesh works on the non-readable CAT meshes.
  - Bind positions come from `CatMeshSourceVertices`, added by `CatMeshLoader`.

### 5.10 Electra (0x7d6) mode 1: `ElectraSim`
vftable `1016cbbc`, loader `100d97ba`, visual build `100d98e0`, Process `100d9de5` (mode dispatch:
0 → `100da452`, 1 → `100da0c0`, 2 inline).
- Fields:
  - 0 flags, 9 material (2 = `Electricify.png` 4×4), 10 mode
  - 11-17 (not read by mode 1)
  - 18-25 ramp, 26 duration
  - 28 spark size, 29 shell radius, 30 spark life ms, 31
- Modes above 2 get duration 0.
- A per-body 48-byte table (`0x1016c7c8`, picked by the host's Breed/Sex/Fatness, stats 4/59/47) is
  set up but **not used by mode 1**.
- **Mode 1:**
  - `owed = _ftol(age·28/life)`, spawns = owed − previous, at most 2 per call. That's 56/s at 0.5 s life,
    about 28 alive.
  - A spark takes a random unit d (`100d3005`), stretches y ×1.5, sits at `locator + d·f29`, and
    normalises the stretched d.
  - Its first axis is `w' = FindPerpendicular(d)` turned by −r about d, with `r = (rand()&0x7fff)/9990.2`.
    Its second axis is `u = w' × d`.
  - Axes are scaled by f28; the first is negated on a coin flip.
  - Frame `_ftol(t·16)`. Colour is the ramp at **age/duration** (all sparks share it).
- **Expiry:** the first expiry, if not terminating, sets terminating and adds one spark life to the duration;
  the second ends it. Slot 6 only stops the spawns.
- All 21 Electra records sit at attach 1000 (pelvis) in world mode; 19 are mode 1.

---

## 6. Port architecture

| Area | Files |
|---|---|
| Handler, creation, nano flow | `EffectHandler.cs` (`CreateControl` switch, `Create*` builders, cast/tracer/hit/buff flow, `IsStockTracer`), `EffectHandle.cs` |
| Base control | `GfxControl.cs`. The first `Process` only arms (**the body is skipped on that call**), age += dt, the locator is resolved into `WorldMatrix`, and after `OnProcess` it readies at `age ≥ duration`. Controls with stock expiry quirks keep their own duration and set the base one to `InfiniteDuration`. |
| Locators / hit locations | `EffectLocator.cs` (`OnDynel`, `OnVisual`, `OnHitLocation`, `WorldPoint`, `Beam`, `TryGetHighlightRoot`), `EffectHitLocation.cs`, `EffectAttachIds.cs` |
| Stock sims (Unity-free, unit-tested) | `FlareType0Visual`, `FlareType0Sim`, `Tracer1Sim`, `PlasmaSim`, `Tracer4Sim`, `Cord4Strip`, `StarsCase3`, `StarsRing`, `StarsCase19`, `DeformerSim`, `ElectraSim`, `StockColorRamp`, `Spell1Trail`, `ScatterSchedule`, `SpriteEmitterMath`, `TracerMath`, `EffectFrameRate`, `EffectTypeCatalog`, `EffectCoverage`, `AnimNoteIds` |
| Drawing | `EffectBillboardBatch.cs`:<br>• `Quad`: camera-facing, one texture per frame via `EffectAtlasFrames.GetFrame`.<br>• `Strip`: a dynamic mesh, one colour, one texture with UVs, both windings.<br>• `Strip.Quads = true`: every 4 vertices form an independent quad.<br>Controls hand geometry over in `CollectBillboards` / `CollectStrips`. |
| Mesh deform | `Rendering/CatMesh/CatMeshDeformHost.cs`, `CatMeshSourceVertices.cs` |
| Anim notes | `Rendering/CatMesh/AnimNoteIds.cs`, `CatAnimRuntimeClip.Notes`, `CatAnimPlayer.NoteReached` |
| Network hooks | `Playfield/PlayfieldFactory.cs`: `OnCharacterAction` (FinishNanoCasting, SetNanoDuration), `OnBuff`, `OnCastNanoSpell` |
| Test scene / tool | `DEV/GfxTest_DEV.cs` (a caster/target duo, `SpawnNano`, `Handler`, `ClearEffects`) and the editor window **Lost Eden → GFX Test** (`Assets/Editor/GfxTest/GfxTestWindow.cs` + `.uss`: effects/nanos browser, status chips, C/T/I/H/B dots, click to select, double-click to spawn or cast) |
| Tests | `Tests/Effects/*.cs` + `LostEden.Effects.Tests.csproj`, which **links** the Unity-free sources. Run `dotnet test` in `Tests/Effects`, then delete `bin/` and `obj/`. |

Additive brightness: `EffectBillboardBatch.AdditiveHdrBoost = 3` multiplies additive RGB so HDRP
bloom catches it. **Not stock** (§9).

---

## 7. How to port a new control

1. **Pick the target.** Choose it by nano count: the GfxTest window's Nanos tab, a status chip, and the
   gaps in the tooltip.
   Dump the records with the gfxtweak dumper and survey every record of that type (field 0, mode,
   attach, material), so you know which branches actually occur in the data.
2. **Find the class.** Locate the vftable via RTTI and compare its slots with a known class
   (Stars `100fc404…`) to spot Process, TerminateGracefully (slot 6) and SetDuration (slot 8). Find the
   constructor(s) by searching for writes of the vftable address (`c7 06 <vft>`) and read the loader
   (a run of `10106de9` float / `10106dc8` int field reads into `+offset`).
3. **Read Process in disassembly**, not the decompiler. Note the early-expiry handling, the
   per-call constants and every `_ftol`. Check each helper call (and the DisplaySystem import it
   ends in) rather than guessing.
4. **Read the visual** in DisplaySystem: the ctor (render states: `0x13`/`0x14` blend,
   `0x0e` Z write, `0x16` cull), and the geometry builder (vertex layout, UVs, corner order).
5. **Write `XxxSim.cs`** (Unity-free). Keep stock's float/double evaluation where it matters, cite
   addresses in the doc comment, and link the file in `Tests/Effects/LostEden.Effects.Tests.csproj`.
6. **Write `XxxSimTests.cs`** using a real record's fields (and `StarsCase3.MsvcRand` for determinism).
   Cover spawn counts, geometry invariants, expiry/terminate paths and any quirk you kept.
7. **Write `GfxControlXxx.cs`.** Handle stock expiry (own duration if it drains or extends), slot 6
   (`OnTerminateGracefully`), slot 8 (`SetDuration`), slots 11/12 if the class has colours, and
   `CollectBillboards` / `CollectStrips`.
8. **Wire it:**
   - `EffectTypeTags` constant.
   - `EffectTypeCatalog`: set Ported.
   - `EffectTypeCatalogTests.PortedSetMatchesTheControlsWeActuallyImplement`.
   - `EffectHandler.CreateControl` case, gated on the verified mode.
   - `EffectCoverage`: `NodeStatus` (Missing for unported modes) and `IsVerified`.
   - `EffectHandler.IsStockTracer` if stock gives it no duration.
9. **Verify live:** exit Play, `AssetDatabase.Refresh()` + `RequestScriptCompilation()`, check the
   console, enter Play, `SpawnNano(id)` from a probe, log the sim state per frame and take screenshots.
   Compare timings and counts with what the RE predicts.
10. **Record it:** add the class to §5 of this file, update the status table and §9, and refresh the
    coverage numbers (§8).

### Probe recipe (Unity MCP RunCommand)
- `System.Reflection` is forbidden and AODB types aren't visible. Expose what you need as public
  read-only members (e.g. `GfxControlStars.StockCase19`, `CatMeshDeformHost.Deformers`).
- Pattern: in `Execute`, `test.ClearEffects(); test.SpawnNano(id);` and register a static
  `EditorApplication.update` callback. Dedupe on `Time.frameCount`, find the control in
  `test.Handler.LiveControls`, append to a log file, and call `ScreenCapture.CaptureScreenshot(path)` at
  chosen ages. Unregister on exit.
- Right after a recompile the first RunCommand can fail with "Could not find type …
  RunCommandMacroEvaluatorEntryPoint". Just run it again.
- Editing a script during Play reloads the domain and wipes GfxTest state: exit Play first.
- `Object.GetInstanceID()` is obsolete here (use `GetEntityId`).
- Reading the coverage counts: find the `GfxTestWindow` via `Resources.FindObjectsOfTypeAll<EditorWindow>()`,
  send `NavigationSubmitEvent` to the "Nanos" tab button, poke the `ToolbarSearchField`
  (`"x"` then `""`), then read the chip `Button.text`s ("Verified  1,293" and so on).
- `Graphics.DrawMesh` output (Deformer host, strips) exists only for the frame it was issued in.
  Screenshot while playing, not paused.

### Editing gotchas
- Many files are **CRLF**. Scripted replacements must convert `\n` to `\r\n` first.
- Long Bash heredocs containing quotes can fail with "unexpected EOF". Write the edit script to a
  file with the editor tool and run it.
- Never `git rm --cached`. Delete from the working tree only. Nothing is committed without the
  user's say-so.

---

## 8. Coverage

`EffectCoverage` walks each nano's effects (the 5 stats, and children the way the port's composites
spawn them). A tree is as good as its worst record. Two scan pitfalls that have bitten before:
Stars field 30 is the spark life, not a child id; and Spell1 spawns fields 31/32 only when field
33 == 0.

As of 2026-09-22, with the buff slot counted: **7,756 nanos have effects. 1,293 are verified,
4,530 unverified, 179 approx, 1,754 missing.** Missing rose when buff effects started being counted.

Next targets, by nano count:
- Stars starTypes 17 / 18 / 16 (tracers, ~1k each) and 22 / 6 / 10 / 11 (hits)
- Suns 0x7d5 (~378)
- Spiral 0x7d1 (~282)
- Shield 0xbbb (~206)
- Deformer modes 0/4; Electra modes 0/2
- The buff-slot gaps, which haven't been ranked yet: sort the Nanos tab by the B dot.

---

## 9. Open questions and known deviations

| Item | Detail |
|---|---|
| **Stock frame rate** | `EffectFrameRate.StockProcessHz = 30` is an assumption. Stock runs Process once per frame, uncapped. Get the number from `/framerate` (Ctrl+Alt+F) in the real client while 28612's hit plays. |
| **Additive ×3** | `AdditiveHdrBoost = 3` is not stock (stock draws colour × texture unscaled, no bloom). At ×1 template colours read as intended. It's the user's call. HDRP also blends in linear space; D3D blended in gamma. |
| **Buff message semantics vs the server** | Stock: CharacterAction 98 adds a buff, and a Buff message with `Unknown1 == 0` removes it. The previous code played the buff on Buff and treated 98 (`AnimKindIds.AttackSwingAction = 0x62`) as an attack swing. Not yet checked against what the server actually sends. |
| **Buffs already running** | When a character appears with active nanos, stock recreates their effects (`FUN_1004f2bd` / `FUN_10051c0c`). Not ported. |
| **Buff conflicts** | Stock's stacking rule (`FUN_1004e9cc`) is not traced; the port only replaces the same nano. |
| **Hit-location lifetime** | When stock deletes a hit location isn't traced. Stars 19 would stop spawning once it's gone; the port never deletes one. |
| **Release clip choice** | Stock picks the release clip from stats 377/378 and a default that isn't traced; the port uses kind names spell-dir / spell-self. Unfired-note flush on clip removal isn't ported. |
| **Random tables** | `100d3005`'s shared unit-vector table and walk are replaced by fresh draws with the same distribution. |
| **Deformer source positions** | Stock feeds the wave the CATTriVertex (0x44-byte) source position from randy31.dll; the port uses mesh bind positions (believed equal, unconfirmed). |
| **Spell1 windows 3/4** | Still the earlier model (only matters when field 33 == 0). |
| **Game path untested live** | The FinishNanoCasting / SetNanoDuration / Buff handlers compile and follow stock, but have only been exercised through GfxTest, not against a server. |
