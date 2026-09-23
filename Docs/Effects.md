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
  | 13 | **SetColor** from packed ARGB (handler `100ce34e`, port `EffectHandle.SetColor`). Base `10079931` does nothing. Fire, Smoke, Sparks, BuffPlaceHolder, BPHFSM and Spell1 take it as start = the colour, stop = the colour at alpha 0 (`GfxControl.SetColorAsStartAndFadeOut`). Meta, MetaStatic and Delay forward 11, 12 and 13 to their children. Stock's senders are game code (effect 2000 with fixed colours, colour tables, item effects), none of it ported yet. |

- Common control fields: `+0x0c` age, `+0x10` duration, `+0x14` ready flag, `+0x2c`/`+0x30`
  locator or visual pointers (varies by class).
- `CreateEffect2` has many overloads (`100d1f1b`…`100d23ce`). The dynel one (`100d2070`) returns 0
  for effect id **49999 (0xc34f), stock's "no effect"**. `CreateEffect2(id, caster, target, attach)`
  only builds Spell1 (`100d1705`).
  `CreateGfxControl(id, hitLocation)` builds hit-location controls (Tracer1 `100fecea`, Plasma
  `100ec5ae`, Tracer4 `101002e0`, Tracer3 `100ff996`, Stars `100f7d4a`). A third factory, `100cea4b`,
  builds a control at a bare position (Nano0 `100e73ea`, for Tracer3's child).

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
- How stock resolves an attach id (`10105e6d`, called from the locator update at `1010673f`):
  - Attractors (2000 + n) and bones (1000 + n) are looked up by name; a missing one falls back to
    `Attractor01_head` (`10105f93`, `1010601b`).
  - 3000 is a weapon's muzzle. It casts the host to `WeaponItem_t` and asks it (`1009c337`), so it
    resolves only on a weapon item. 3001 asks an `n3VisualDynel_t`.
  - When the lookup fails, the locator keeps the mesh frame, attach 0 (`10106744`).
  - Names are compared exactly (randy31 `RCATMesh_t::GetAttractor` / `GetBoneMatrix`, a case-sensitive
    strcmp). The port's two name tables match stock's `0x102c63f8` / `0x102c63a8` entry for entry.
  - The port (`VisualDynel.TryGetAttachMatrix`, 2026-09-22) follows these rules as written:
    - It finds a bone or attractor by its exact CAT name, and falls back to `Attractor01_head`.
    - 3000, 3001 and any other id fail on a character. The locator then keeps the mesh frame
      (`EffectLocator`), and a point getter fails.
    - Any non-zero template field 7 is used.
    - `CatMeshLoader` now keeps every CAT attractor, including ones like Attractor30_beam whose name
      maps to no `AttractorPlace`, so name lookups can find them.
    - Spell1's attach test uses the same resolver (stock has only one).
  - What the port did before (all invented, none of it stock):
    - 3000 was the right-hand attractor, which put 43607 on the hand instead of between the feet.
    - Bones were matched by loose tokens ("hip", "body", "chest" ...), attractors by substring and by
      the number in their name (Attractor07_special became the hip).
    - A missing bone fell back to the hip attractor, a missing attractor to the hip and then the head,
      and unknown ids to the hip.
    - Template attach ids other than bones, attractors and 3000 were ignored.
  - Weapon muzzle effects (2000-2009 on a WeaponItem) aren't played yet; they'll need `1009c337`.
    3001's static-mesh attribute matrix (`VisualMesh_t::GetAttrMatrix`) isn't ported: no effect host in
    the port is a static mesh.
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
| `GfxVisualSpiral` | Spiral | A 12-segment ribbon round one turn (§5.27): ctor `10021eb4`, draw `10021924`. White vertices, faded at both ends of the kept range; the texture wraps along it. |
| `GfxVisualSprite2Type0` | Sparks, Fire, Smoke, Nano0/1 | Sprite pool (§5.22, `Sprite2Type0Visual`): ctor `1002646b`, `InitSpriteDefault 10025844`, `NewSprite 100261de` (every value) / `10025be0` (the defaults plus position, velocity and wind), `ProcessSprites 100267c1`, quad `10025391`. Gravity is +0x22c, 0 from the ctor. 0x78-byte sprite; an upright camera-facing quad of width × height. Blend from the third ctor argument: SRCALPHA/ONE when true, SRCALPHA/INVSRCALPHA when false. The atlas row is `cell / rows`, not `cell / cols`. |
| `GfxVisualSprite2Type2` | Spell1 window 2 | Clears its list after drawing (`10027df8`), so it is rebuilt every frame. |
| `GfxVisualPlasma` | Plasma | ctor `1001b8f7`, render `1001bbf2`. A 75-segment camera-facing strip. |
| `GfxVisualCord4` | Cord, Tracer4, Tracer5 | A link ribbon: render `1000f072`, geometry `1000f040`, side vector `1000ee68`. See `Cord4Strip`. |
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
- Bodies driven only by age (Flare, Tracer1, Plasma) just run every frame with the real age. Electra
  looked age-driven (its spawn count is owed by age) but caps spawns per call, so it's replayed too (§3.7).

### 3.7 Timing model by control (to revisit)
Stock calls every control's Process once per engine frame (`_EffectHandler_t::RunFunction`
`100d2423`), with the real frame delta and no time gate. So in the original client, effects with per-call
maths looked different at different frame rates.

**Our client unifies this (your rule, 2026-09-22): an effect must look the same at any frame rate.** The
maths is stock's; the clock is ours. The only exception left is MParticle (group C), kept by your call.

**The rule for choosing a model** (apply it to every new port):
- **Age-driven body** (every value is a function of age or dt, nothing counted per call): run it every
  Unity frame (group A). That gives stock's exact value at every frame, at any frame rate. Don't put
  these on the 30 Hz replay with `Blend`:
  - `Blend` only interpolates position and size, so turns, colours and frames would still step.
  - The straight-line interpolation cuts corners on anything moving in a circle.
  - Suns sunType 1 was moved off the replay for exactly this reason.
- **Per-call body** (spawn budgets, springs, per-call steps, anything that would run faster at a higher
  frame rate): replay it at 30 Hz (group B). If its sprites move, draw them between the last two steps
  with `Blend`.
- **Neither fits** (a stock formula that is unstable at the replay rate): sub-step it and say so (group C).
- Known exception: Stars starType 2 is age-driven but still on the replay (item 3 below).

The port uses four models:

**A. Every Unity frame, with the real dt and age.** This is stock at whatever frame rate the port
runs.
- FlareType0, Tracer1, Tracer3, Tracer4, Tracer5, Plasma, Spell1
- Nano0 (its spawn count is owed by age; the sprites move by dt)
- Shield, Deformer
- Sparks, Fire, Smoke, Spiral
- BParticle, TParticle, GroundGrid, EffectMesh, VolGrid (curves of age; the spin grows by dt)
- Sprite (its cycle is a function of age)
- SkyFlash (a function of age; the place is re-read in a following phase)
- Stars starTypes 7/8 (`StarsRing`, computed from age at draw time)
- Suns sunTypes 0 and 1, since 2026-09-22 (type 1 was on the 30 Hz replay and spun in 18° steps)

**B. Replayed at a fixed 30 Hz** (`EffectFrameRate.StockProcessHz`, `TakeFixedSteps`, at most 8 steps
a frame). For bodies with per-call spawn budgets, springs or step counts.
- Stars starTypes 2, 3, 4, 5, 6, 9, 10, 11, 13, 15, 16-20 and 22. They're drawn between the last two steps with
  `StarsCase3.Blend`, which interpolates position and size only.
- Suns sunType 4. It isn't interpolated, but its sparks don't move.
- Electra, since 2026-09-22: its spawn cap (≤ 2 a call) made the shell fill faster at high fps. The
  sparks are put back on the locator every frame (`ElectraSim.Place`), so the shell doesn't lag a moving
  host.
- BParticle2, since 2026-09-22: the respawn cap (field 15 a call), the bounce and the drag are per call.
  Particles are drawn between their last two steps (position, size, angle); one that was dead on the
  previous step is drawn where it is.
- Trail2: one sample a call, so the trail's length in time is N calls. The samples are taken at 30 Hz; the
  origin (the live end) is set every frame and the thrust drift runs every frame with its dt (§5.35).
- Cord, since 2026-09-22: stock's step is the frame delta clamped to [0.01, 0.025], so links drifted
  1.44× fast at 144 fps and 0.75× at 30. Each step is now one 1/30 s call (clamped step 0.025). The links
  aren't interpolated; they drift a few millimetres a step.
- Checked live at 30 fps and at ~150 fps: Electra 43452 (24 / 28 / 30 sparks at 0.5 / 1 / 2 s) and Cord
  20071 (13 / 15 links, same spread) are identical; BParticle2 in 71016 matches in count (20 / 60 / ~220)
  and differs only in its random layout.
- VulcanRocks, from its port (2026-09-22): a resting rock adds one settle a call and the control ends at
  field 20 settles, and a rock sinking between calls bounces again at low frame rates. Rocks are drawn
  between their last two steps (position, and the angle while the spin axis is unchanged).
- The wind (`EffectWind`, at most 4 steps a frame).
- BuffPlaceHolder's children: `ChildDt` = one stock frame per 0.45 rad step. This is a deliberate
  deviation, kept by your decision (§9).

**C. Sub-stepped at ≤ 1/60 s.** MParticle (`GfxControlMParticle.MaxStep`). Stock's bounce is
`100·b·dt`, which is unstable at 30 fps. This was reported as a decision when it was ported (§5.21).
It still depends on fps above 60 (each frame is then one step). Left as is by your call (2026-09-22).

**D. Per-call constants scaled by dt.** The unverified Stars starTypes that still run the earlier
stand-in (`FrameSteps`, `TakeBudget`).

To revisit:
1. **Measure the stock client's frame rate** (`/framerate`). Group B assumes stock ran at 30 fps; if it
   ran faster, those effects spawn and move faster in stock than here.
2. ~~Group A controls with per-call caps.~~ Done 2026-09-22: Electra, BParticle2 and Cord moved to
   group B.
3. **Stars starType 2 depends only on age** but sits in group B, which breaks the rule above. Positions
   are interpolated, but size, colour and frame change 30 times a second. Move it to group A, as Suns
   sunType 1 was, when that's convenient.
4. **Group B's drawn state updates at 30 Hz** apart from Stars' interpolated position and size.
   Colours and frames step visibly at high fps.
5. **MParticle's 1/60 sub-step** is a stability choice, not stock. It keeps its bounces close to what
   stock produces at 60 fps, but above 60 fps it still changes with the frame rate. The unified fix is
   fixed 1/60 s steps with the remainder carried; held back by your call.
6. **BuffPlaceHolder's child delta** (§9) makes the trail length frame-rate dependent in stock; the port
   fixes it at the 30 fps look.
7. **Controls not in these lists are older stand-ins** (Flare1, Nano1, Delay);
   their timing hasn't been checked.

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
| 43878 Lifegiving Elixir | 46262 Spell1 | 17100 Stars #16 (sparks along the whole line, 1 s) | hit 43426 Stars #22 (sparks pulled onto the limbs, 5 s + drain) | — |
| 75347 Wooden Skin | 46236 Spell1 | 17200 Meta: 3× Stars #17 helix strands, 1 s | hit 43029 Stars #8 | 1060 BPHFSM: every ~5 s two BuffPlaceHolders swing a Flare spark with a Cord trail round the spine for 1.5 s |
| 25988 Enhanced Senses | 46146 Spell1 | 17300 Stars #18 (as 26355) | hit 43447 (not checked) | 1020 BPHFSM (white): every ~5 s BuffPlaceHolder 20071 swings Flare 6203 with Cord 20072's trail round the head, 1.5 s (20076 has rate −4π and never steps) |
| 269470 Fists of the Winter Flame | 46145 Spell1 | 17200 (Stars #17, as 75347) | hit 43029 Stars #8 | 72381 Sequencer (loops, 0x400) → Meta 72390 for 30 s → Stars #4 72391 / 72392 on the left / right hand (blue flames) |
| 26355 Augment Agility | 46107 | 17300 Stars #18: a gold spark cloud riding the head of the hit line, ~90 alive | hit 43029 Stars #8 | 1050 |
| 45889 Smiting Missile Mk II | 46123 | 45708 Tracer5: a white streak (s_bullet) flying hand → target | hit 2710 Sparks: 16 red sprites burst, fall and fade in 0.25–0.5 s | — |
| 269708 Blessed by the Ancients | 14400 | 14400 | 14400 | 14400 Suns #0: a bright layered glow 2 m over the pelvis, each layer spinning its own way, swelling and fading over 10 s |
| 28597 Burning Bones | 46256 | 45686 Stars #20: a white fire puff (~120 full-size sparks, 64-frame burn) flying hand → target in 1 s, gone at once | hit 2710 Sparks | — |
| 28609 Freezing Surge | 46142 | 2662 Tracer3 carrying Nano0 2685: a pale blue stream of 2 m puffs laid along the line from the hand, reaching the target in 0.2 s and gone with it | hit 43725: ripple Shield 43722 and Stars #15 | — |
| 28604 Electrifying Containment | 46136 | 17800 | hit 43723 Stars #15: 70 big blue sparks thrown off the target's skin in 0.2 s, flying ~6 m out and shrinking, gone at 1.1 s | — |
| 29645 Immolation Shield | 46102 | 17000 | hit 43029 Stars #8 | 43618 BuffPlaceHolder → Meta 47001: Stars #15 43048 (orange sparks drifting ~0.4 m off the skin, ~110 alive) and Shield 43054 |
| 125772 Stunned by Brawl | 49999 | 49999 | hit 43461 | 43307 Suns #1: a ring of 18 soft coloured lights circling the head, opening and fading over the buff |
| 25994 Hostile Hatchling | 46133 | 17300 Stars #18 | hit 43010 Spiral: a white smoke double helix winding up the target to 2.1 m in 2 s, spinning, then unwinding from the base (4 s) | — |
| 83943 Claw Eyes | 46133 | 17300 Stars #18 | 49999 = nothing | 43657 Smoke: still, black 2 m puffs half a metre in front of the target's face (§5.26) |
| 210484 Sanctifier (and 18 other Sanctifier / Reaper nanos) | 46142 | 45551 | hit 43187 | 43607 Sprite: an orange glow (0x7ffc8900) between the feet, about 1 m across, breathing ±0.125 m over 6.7 s (§5.33) |
| 201723 Spawn Entrance Nano | 49999 = nothing | 49999 | 49999 | 80006 Sprite: a red dot at the head, shrinking from 0.5 m to nothing and fading in each 0.5 s, forever (§5.33) |
| 269534 Blessing of the Ancient Form | 72362 VolGrid on the hand | 72362 | hit 72362 (and buff 413 = 72362): a green light pillar, 3.6 m square, shooting up to 80 m in 0.3 s and sinking to 20 m as it fades over 3 s (§5.32) | 72362 |
| 28636 Slime Cascade | 46123 | 17600 Plasma | hit 43070 Stars #9: green slime flashes 2 m over the pelvis, falling and shrinking, 8 s | — |
| 204594 Weeping Flesh | 49999 = nothing | 49999 | hit 43045 Stars #13: a green-to-purple streamer writhing round the neck, growing and shrinking over 10 s | 49999 |
| 152418 Imprisoned | 49999 = nothing | 45679 | hit 43008 Stars #5: a pale mist cage rising from the feet past the head, 4 s then a 1.3 s drain | — |
| 157988 Fiery Breath | 46256 | 45712 | hit Meta 47400: Stars #7 45001, 43713-43715 and VulcanRocks 45060: tiny rocks thrown ~16 m up from the target's feet, bouncing up to 4 times and coming to rest ~20 m round; gone at ~6.9 s (§5.31) | — |
| 152838 Magnified Psychic Hammer | 46167 | 45705 | hit 43103 ShockWave: two red ground rings 0.5 s apart, each spreading to 5 m and fading over 1.5 s, and two 40 m beams flashing up with each (§5.30) | — |
| 246033 Fountain of Life | 19400 | 45662 | hit 43220 Stars #2: a ball of 128 many-coloured sparkles round the pelvis that spreads to 1.3 m, shrinks and fades to grey-white in 1.5 s | 1000 |
| 100250 Berserk Rage | 46123 | 17300 Stars #18 (as 26355) | — | 2200 Fire: 16 flame sprites a second off the feet, streaming out behind the character (§5.25) |
| 223386 Composite Nano Expertise | 46209 Spell1 | 45613 Stars #19 | hit 47175 Meta: Stars #10 sparkle shell + #11 and #6 rising swirls (violet, ~6 s + drain) | 1010 BPHFSM: the 25988 halo in yellow |
| 290473 Yalmaha Clio (and the Phasefront vehicle nanos) | — | — | — | 72308 Meta: Trail2 72301 / 72302, blue crossed ribbons on attractors 2006 / 2007, drifting along their z; plus Toggles 72412 / 72416, whose exhaust starts when the vehicle drives forward (§5.36). Made directly on the humanoid caster, which lacks both attractors, so both sit on the head (§5.35, §9) |
| 168665 Gift of Life | 49999 = nothing | 49999 | hit 43108 SkyFlash: six nested 60 m columns of light standing on the target's feet, bright white at the top, fading out over 4.9 s (§5.34) | — |
| 56213 Greater Hold Victim | 46136 Spell1 | 17800 Meta: Suns #4 17000 (sparks on the line) + Plasma 17600, 1 s | hit 43608 Shield (cyan scrolling shell over the body, 3 s) | 43613 Sequencer → Shield 43608 for 60 s, looping until the buff ends |

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
| 0xbbc | `_GfxControlSequencer_t` | `GfxControlSequencer` | Verified |
| 0xbc5 | `_GfxControlDelay_t` | `GfxControlDelay` | Unverified |
| 0xbd5 | `GfxControlScatter_t` | `GfxControlScatter` + `ScatterSim` | Verified (§5.39); position modes above 1 and the matrix/dynel locator bases have no record |
| 0x3ed | `_GfxControlFlare_t` | `GfxControlFlareType0` + `FlareType0Sim` | Verified |
| 0x3ee | `_GfxControlFlare1_t` | `GfxControlFlare` | Unverified |
| 0x3f2 | `_GfxControlSpell1_t` | `GfxControlSpell1` + `Spell1Trail` | Verified when field 33 ≠ 0 |
| 0x3eb | `_GfxControlCord_t` | `GfxControlCord` | Verified (§5.14) |
| 0x3ec | `_GfxControlFire_t` | `GfxControlFire` + `FireSim` + `Sprite2Type0Visual` | Verified (§5.25) |
| 0x3f1 | `_GfxControlSmoke_t` (the port once called it Nano2) | `GfxControlSmoke` + `SmokeSim` + `Sprite2Type0Visual` | Verified (§5.26), wind mode 7 included (80011) |
| 0x3fa | `_GfxControlSparks_t` | `GfxControlSparks` + `SparksSim` | Verified (§5.22), wind modes included (§5.24) |
| 0x3ef | `_GfxControlNano0_t` | `GfxControlNano0` + `Nano0Sim` | Verified (§5.29) |
| 0x3f0 | `_GfxControlNano1_t` | `GfxControlNano` | Approx |
| 0x3fe | `_GfxControlTracer3_t` | `GfxControlTracer3` + `Tracer3Sim` | Verified (§5.28) |
| 0x3fb | `_GfxControlTracer1_t` | `GfxControlTracer1` + `Tracer1Sim` | Verified |
| 0x400 | `_GfxControlTracer4_t` | `GfxControlTracer4` + `Tracer4Sim` + `Cord4Strip` | Verified |
| 0x401 | `_GfxControlTracer5_t` | `GfxControlTracer5` + `Tracer5Sim` + `Cord4Strip` | Verified (§5.23) |
| 0x7d2 | `_GfxControlPlasma_t` | `GfxControlPlasma` + `PlasmaSim` | Verified |
| 0x7d4 | `_GfxControlStars_t` | `GfxControlStars` + `StarsCase2` / `StarsCase3` / `StarsCase4` / `StarsSwirl` / `StarsCase9` / `StarsCase10` / `StarsCase13` / `StarsRing` / `StarsBodySparks` / `StarsLineSparks` / `StarsLimbSparks` | Verified for starTypes 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 16, 17, 18, 19, 20, 22; others Unverified |
| 0xbb9 | `_GfxControlDeformer_t` | `GfxControlDeformer` + `DeformerSim` + `CatMeshDeformHost` | Verified mode 1; modes 0/4 Missing |
| 0x7d6 | `_GfxControlElectra_t` | `GfxControlElectra` + `ElectraSim` | Verified mode 1; modes 0/2 Missing |
| 0x7db | `_GfxControlHighlight_t` | `GfxControlHighlight` + `HighlightSim` | Verified for modes 0-2 (§5.40); mode 3 Approx (no nano) |
| 0x7d5 | `_GfxControlSuns_t` | `GfxControlSuns` + `SunsSim` | Verified sunTypes 0, 1 and 4; others Missing |
| 0x7d1 | `_GfxControlSpiral_t` | `GfxControlSpiral` + `SpiralSim` / `SpiralRibbon` | Verified (§5.27) |
| 0x3f4 | `_GfxControlSprite_t` | `GfxControlSprite` + `SpriteSim` | Verified (§5.33); Approx for the unblended (0x1000) and Sprite3 multiply draws (only 7100) |
| 0xbde | `GfxControlVolGrid_t` | `GfxControlVolGrid` + `VolGridSim` | Verified (§5.32); the spin's order against a tilted locator unchecked (no record spins) |
| 0x405 | `_GfxControlVulcanRocks_t` | `GfxControlVulcanRocks` + `VulcanRocksSim` | Verified (§5.31), with model slot 4 taken as loaded (§9) |
| 0xbdc | `GfxControlToggle_t` | `GfxControlToggle` + `ToggleSim` | Verified (§5.36); Approx with 0x1000 (no nano's record); the moving start/stop not seen live (GfxTest has no mover) |
| 0xbdf | `GfxControlTrail2_t` | `GfxControlTrail2` + `Trail2Sim` | Verified (§5.35); the dynel ctor's rendering switch isn't ported |
| 0xbbe | `_GfxControlSkyFlash_t` | `GfxControlSkyFlash` + `SkyFlashSim` | Verified (§5.34); Approx for a negative field 18 (12510, the torso sphere, §9) |
| 0xbb8 | `_GfxControlShockWave_t` | `GfxControlShockWave` + `ShockWaveSim` | Verified (§5.30); two stock paths no record takes aren't ported (§9) |
| 0xbbb | `_GfxControlShield_t` | `GfxControlShield` + `ShieldSim` | Verified (wave, ripple and host material included) |
| 0xbda | `GfxControlShield2_t` | `GfxControlShield2` + `Shield2Sim` + `StockColorCurve` + `StockFloatCurve` | Verified (§5.38); Approx on a field 20 mech record; displacement mode 1, UV modes 2-5 and the mech .abiff have no nano |
| 0xbd4 | `GfxControlBParticle2_t` | `GfxControlBParticle2` + `BParticle2Sim` + `StockColorCurve` | Verified (§5.16); flags 0x400000 / 0x1000000 not modelled |
| 0xbcc | `GfxControlTParticle_t` | `GfxControlTParticle` + `TParticleSim` | Verified (§5.17) |
| 0xbd7 | `GfxControlTParticle2_t` | `GfxControlTParticle2` + `TParticle2Sim` + `StockColorCurve` | Verified (§5.37); flags 0x100, 0x8000, 0x10000, 0x40000, 0x80000 and frame modes 1-3 have no record |
| 0xbd0 | `GfxControlBParticle_t` | `GfxControlBParticle` + `BParticleSim` | Verified particle modes 8 and 1 (20 of 33 records, §5.18); other modes Missing |
| 0xbd6 | `GfxControlGroundGrid_t` | `GfxControlGroundGrid` + `GroundGridSim` | Verified visual mode 0 (§5.19); modes 1/2 Missing |
| 0xbd1 | `GfxControlEffectMesh_t` | `GfxControlEffectMesh` + `EffectMeshSim` + `StockUvTrack` | Verified (§5.20), body scale, Atrox variants and animated UVs included; Approx with 0x2000/0x4000, effects 1/2/7, or effect 4 on a model that isn't emissive white |
| 0xbd3 | `GfxControlMParticle_t` | `GfxControlMParticle` + `MParticleSim` | Verified (§5.21) |
| others | see `EffectTypeCatalog` | — | Missing / Approx |
| 0x3e9 | `_GfxControlBPHFSM_t` | `GfxControlBuffFsm` | Verified |
| 0x3ea | `_GfxControlBuffPlaceHolder_t` | `GfxControlBuffPlaceHolder` | Verified |
| 0xfa0, 0 | audio, none | — | stock draws nothing |

### 5.2 Meta (0x7d7)
Up to 10 child ids in fields 0-9, all spawned at construction. Field 9 = −1 means no child and an
infinite parent. Slots 11, 12 and 13 (`100e5e4a` / `100e5e92` / `100e5eda`) forward the colour to every
child, as Delay's (`100d8961` / `100d899a` / `100d89d3`) do to its one child.

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

**Case 2** (`100f8864`, `StarsCase2`): a ball of 128 sparkles round the locator that spreads, shrinks
and fades over 1.5 s, each in one of eight hues (hit 43220 of nano 246033).
- Fields: 18-25 ramp, 28 size. Field 26 is loaded, but the lazy init (`100f80ba`) sets the duration to
  **1.5 s** on the first call; a later SetDuration still lands.
- Lazy init: each slot keeps a random point in the unit ball (`100d316a`, +0x48), sits at the locator
  with size 1, and is visible for good.
- Per call, `p = age/dur`:
  - `k = √p·0.7 + 0.6`, and slot i sits at `locator + dir[i]·k` (it follows the locator)
  - `n = _ftol((1−p)·15)` clamped 1..15; size `f28·(1−p)·20/span[n]` (ints at `0x102c5fa8`: 10, 6, 7, 7,
    9, 11, 16, 19, 22…29); slot i shows frame `n − (i & 1)`
  - colour j is the ramp at p with its start R, G, B replaced by palette j (floats at `0x102c5ee8`: pink,
    yellow, magenta, cyan, blue, green, red, pale green); slot i takes colour `i & 7`
- Expiry readies it at once. Terminating readies it after its next call (the tail at `100f8622`).
- 18 records (13000, 25003, 43216–43231), all world mode, material 33, size 2.1; they differ only in the
  end colour and attach (1000, or 3001 for 25003).

**Case 3** (`100f89dd`, `StarsCase3`): the orbiting swarm.
- Fields: 18-25 ramp, 28 size curve, 29 X/Z radius, 30 life ms.
- Per call:
  - spawn ≤ 2
  - spring `v += (o−p)·0.1; p += v·0.1`
  - size `(t+0.2)·c·(1−t²)`
  - frame `15 − _ftol((timer−age)·16/life)`, clamped
- The spawn call leaves the sprite record as it was.
- Replayed at 30 Hz and interpolated.

**Case 4** (`100f8c39`, `StarsCase4`): rising sparks, e.g. flames on the fists (nano 269470's buff).
- Fields: 18-25 ramp, 28 size, 29 launch speed, 30 life ms. Timers start at -100 (lazy init shared
  with case 3).
- Per call, per slot:
  - alive (`age < timer`): `v = v·0.95 − (0, −0.1, 0)`, then `p += v·0.1`
  - frame `_ftol((timer−age)·64/life)`, clamped 0..63
  - size `(2 − t)·f28`, colour the ramp at t
- Spawn: ≤ 2 per call, not while terminating. `p` = locator (`1010640a`),
  `v = f29·((0, 3, 0) + ball)`. The spawn call leaves the sprite record as it was.
- On expiry it drains like case 3 (terminating + 5 s).
- 38 records, 32 in world mode. At 30 Hz and 285 ms life that's ~18 sparks per locator, rising ~0.35 m.

**Case 5** (`100f8e71`, `StarsSwirl`): sparks thrown out sideways 1.2 m below the locator and sprung back
in, a rising cage of mist round the target (43000-43014 and 13300, e.g. hit 43008 of 152418 Imprisoned).
- Fields: 18-25 ramp, 26 duration, 28 size curve, 29 launch speed, 30 life ms, 31 spring in hundredths
  (int). Lazy init `100f81e0` (timers -100), and it drains on expiry (its `0x100fc374` entry is 0).
- Alive (age < timer), with k = f31/100:
  - `v += k·(locator − p)` in all three axes, then `p += v·0.2` (`1015f45c`)
  - frame `15 − _ftol((timer − age)·16/life)` clamped 0..15
  - t = (life + age − timer)/life; size `(t + 0.2)·f28·(1 − t²)`; colour the ramp at t
- Spawn (`100f9018`): ≤ 2 per call, not while terminating. r = XZ unit vector (`100d3215`),
  `p = locator + (0, −1.2, 0)`, `v = (r.x·f29, r.y, r.z·f29)`, timer = life + age. The spawn call
  leaves the sprite record as it was.
- The alive maths is case 11's with a 0.2 step, so the port runs it in `StarsSwirl`.
- 23 records, all world mode on attach 1000 (the pelvis), life 1.3 s, size 3.5. Speed 0.3 with spring 1
  (11), 0.2 with spring 4 (11), 0.4 with spring 5 (13300).
- Seen live on 152418 (43008, 4 s): sparks start at the feet, swing out ~0.46 m, and rise through the
  pelvis to ~2.15 m (the undamped spring carries them 1.2 m past it). About 80 are alive at once.
  Spawning stops at 4 s and the last spark is gone at 5.3 s.

**Case 9** (`100f946f`, `StarsCase9`): sparks that flash big above a random point near the locator and
fall (43070-43074, e.g. hit 43070 of 28636 Slime Cascade; 13700).
- Fields: 11 base size (+0x167c), 18-25 ramp, 26 duration, 28 fall speed (+0x16d4), 29 spread radius
  (+0x16d8), 30 life ms, 31 flash in hundredths (int, +0x16e0). Lazy init `100f81e0` (timers −100).
- Only every other sprite record is used (stride 0x40): 64 sparks, each with a spawn point at
  +0xc48 + 24j and a timer at +0x1448 + 8j. The odd records are never written, so they stay hidden.
- Alive (age < timer), with x = age + life − timer (seconds):
  - t = x/life, g = 0.5 − x·f28
  - size f11, plus g·f31/100 while g > 0
  - position spawn + (0, 1.5 + g, 0); colour the ramp at t; frame 0
- Spawn: ≤ 2 per call, not while terminating. spawn = locator + (x, 0, z)·f29, where (x, z) is a point
  in the unit disc (`100d32c2`: each (rand() & 0x7fff)/16384 − 1, redrawn until inside and off the
  centre, not normalised). Then timer = life + age; the record is left as it was.
- Drains on expiry (its `0x100fc374` entry is 0). Terminating with nothing alive is ready (`100f884e`).
- 6 records, all world mode on attach 1000, material 8: life 0.5 s, fall 4, spread 0.7, flash 400
  (43070-43074); 13700 is slower and wider.
- Seen live on 28636 (43070, 8 s): green flashes ~2.8 m up (pelvis + 2 m, up to 1.8 m across) shrink to
  0.35 m in 0.13 s and fall to pelvis height by 0.5 s. About 32 are alive at once. Spawning stops at 8 s
  and the effect is gone at 8.5 s.

**Case 13** (`100f9c3b`, `StarsCase13`): a streamer of 128 sparks strung along a wandering heading,
growing out from the locator and back over the duration (43040-43046, e.g. hit 43045 of 204594 Weeping
Flesh; 43734-43737; 14200).
- Fields: 11 spark size (+0x167c), 18-25 ramp, 26 duration, 28 reach (+0x16d4), 29 turn per step in
  radians (+0x16d8).
- The heading step (`100f7ec9`):
  - The axis A (starts (0, 0, 1), `100f73cf`) takes 0.15 of a random unit vector (`100d3005`) and is set
    back to length 1.
  - The heading D (starts (0, 1, 0)) is turned about A by field 29 and set back to length 1.
  - Stock's quaternion product (`1007c5e5`, this·arg) is the Hamilton product arg·this, and the inverse
    (`100daa20`) is conj/|q|². So stock's Q·D·Q⁻¹ is Hamilton's Q⁻¹·D·Q, a turn of −field 29 about A.
- The lazy init (`100f8316`) fills a ring of 128 headings with 128 steps; the head starts at 128.
- Per call:
  - p = age/duration, env = 1 − (2p − 1)².
  - Seven steps, each writing ring[head = (head + 1) % 128].
  - Slot i has f = ((head − i + 128) % 128)/128, the age of its heading. It's shown while f < env, at
    `locator + ring[i]·f28·f`, size f11, the ramp at f. The frame is never written, so it stays 0
    (the ctor zeroes the records).
- On expiry it's just ready (its `0x100fc374` entry is 1). Terminating readies it after its next call.
- 17 records, all world mode, material 8. Attach 1005 (neck), 1000 or 1015. Turn 0.06 or 0.1; reach
  0.7-2.4.
- Port: replayed at 30 Hz like the other cases. A ring entry that gets rewritten bumps its serial, so
  `Blend` doesn't smear the jump.
- Seen live on 204594 (43045, 10 s): a green-to-purple streamer writhing round the neck. It grows to all
  128 sparks at 5 s, shrinks back, and is gone at 10 s.

**Cases 6 and 11** (`100f90d7` / `100f9800`, `StarsSwirl`): sparks launched round a ring a metre below
the locator that circle and climb (hit 47175 of nano 223386).
- Fields: 18-25 ramp, 28 size curve, 29 ring radius, 30 life ms, 31 spring in hundredths (int).
- Spawn: ≤ 2 per call. `r` = XZ unit vector (`100d3215`)·f29, `p = locator + r + (0, −1, 0)`,
  `v = (r.z, 0.5, −r.x)`. The spawn call leaves the record as it was.
- Alive, with `k = f31/100`:
  - `v += k·(locator − p)`; case 6 zeroes the pull's y, so nothing slows the climb
  - `p += v·step`, step 0.15 (case 6) or 0.1 (case 11)
- Size `(t+0.2)·f28·(1−t²)`, frame `15 − _ftol(16t)` clamped 0..15, colour the ramp at t.
- Case 6 keeps slot i alive `(i & 7)/9.5` s past its timer, with `t = (life + age − timer)/(life + that)`.
- Both drain on expiry. All 204 type-6 and 80 type-11 records are world mode.
- At 30 Hz case 6 climbs 2.25 m/s: 43372's column tops out ~3.5 m above the pelvis.

**Case 28** (`100fc0d7`, `StarsCase28`): a point that travels the hit-location line over the duration,
shedding sprites as it goes. Its only record is 12600, the **tracer** (stat 419) of 259902 Throw
Snowball and its two v2 kin — a snowball.
- Fields: 18-25 ramp, 26 duration, 28 the size a sprite swells to (+0x16d4), 30 life ms.
- The two ends are read every call (`101054fe` start, `10105534` end) and mixed by p = age/duration.
  **Without a hit location nothing spawns** (`100fc28b`), as with the line types.
- Up to **ten** sprites start per call (the literal 10 at `100fc0df`). Alive, with
  `t = (age + life - death)/life`: size on both axes is `f28 * (1 - (1-2t)^2)` — swelling from nothing
  to full at half life and back — colour the ramp at t, frame `15 - ftol(t*15.99)` counting 15 down
  to 0.
- **Every live sprite sits on the travelling point**, not where it was born: stock writes the slot's
  stored position into the sprite (`100fc1cf`) and then overwrites it with the current point
  (`100fc26a`), so the stored one never reaches the screen. It draws as one bright head, not a trail.
  The per-slot arrays at +0xc48 and +0x648 are written on spawn and never read; not modelled.
- Stock also computes `1 - (i % 17)/34` per slot (`100fc18a`), which is **always 1** because the
  remainder cannot reach 34. It is the life multiplier at spawn, so it has no effect. Kept as stock.
- starType 28 is past the end of the expiry table (1 to 25), so it takes the default `100fc366` and
  simply **ends at its duration** — no 5 s drain.
- **It must count as a stock tracer** (`GfxControlStars.IsHitLocationTracer`, which is why that
  property tests case 28 as well as the line types). It crosses by `age / duration`, so if
  `EffectHandler.StartTracer` overrides the duration with `travel + 0.5` the ball crosses at the wrong
  speed, and `CompletePendingTrace` then cuts it off when the hit lands — user-reported: the snowball
  vanished short of the target instead of arriving. Measured at travel 2.5 s: the head was 0.57 m short
  of a 3.2 m gap when the hit landed. Stock's launch (`FUN_1004f989`) sets no duration, so it always
  crosses in its record's 1 s whatever the distance.
- Ten a call at 30 Hz lasting 0.3 s means 90 of the 128 slots. Verified live on 12600 over a
  caster-to-target line: the head runs -1.60 to 1.49 in a second, 90 alive, size peaks at 0.198 for a
  field 28 of 0.2, frames count down, ends at 1.01.

**Case 12** (`100f9a7e`, `StarsCase12`): short streaks of four sparks that fly out from the locator
together and shrink as they go, each streak one flat colour. Its only record is 14100 (257089 Alien
Cocoon and its kin).
- Fields: 11 the size a spark **ends** on (+0x167c), 26 duration, **28 the life in SECONDS**, 29 reach
  (+0x16d8), 31 how much bigger it starts, in hundredths (+0x16e0). There is no ramp: the colour is
  flat per streak.
- **The life is field 28, not field 30.** Every call copies +0x16d4 over +0x16cc (`100f9a8a`), so this
  type overrides the shared 'life in milliseconds' the lazy init put there.
- Lazy init is the shared `100f81e0` -> `100f80a6`: all 128 death times filled with -100.
- Per call **two** streaks may start (the literal 2 at `100f9ac2`, not a field), and only on a slot
  whose index is a multiple of four (`100f9b9d`):
  - Spawn: one random unit vector (`100d3005`) goes to all four slots of the group, and slot i + k gets
    death `age + life - k*0.1*life`, so the four trail each other by a tenth of a life along one line.
    The slot that triggered the spawn was already hidden by `100f9ba1`, so it sits the call out while
    its three followers light up at once.
  - Alive: `u = (death - age)/life` falls 1 to 0, `t = 1 - u`. Position `locator + dir*f29*t`, size
    `f11 + u*f31/100` (so it starts big and shrinks), colour `palette[(i >> 2) & 7]` from `0x102c5f48`
    (the same eight as case 2: pink, yellow, magenta, cyan, blue, green, red, pale green), frame 0.
- Expiry drains (its `0x100fc374` entry is 0); the shared tail `100f884e` readies it when nothing is left.
- At 30 Hz that is 60 streaks a second each lasting 0.5 s, so **it runs its pool nearly full**: ~106 of
  128 slots are up. Verified live on 14100: saturates at 106-108, spread settles at the 0.8 reach, size
  spans 0.30 to 1.23, all 8 colours present, terminates at 5.0 and drains in exactly one life (5.55).

**Case 14** (`100f9da3`, `StarsCase14`): a flat ring of sparks that fly straight out from the locator
while the ring as a whole rides up through it — 95796 Captivated Gaze's hit 43118 and its kin.
- Fields: 11 spark size (+0x167c), 18-25 ramp, 26 duration, 28 reach (+0x16d4), 29 the turn between one
  spawn and the next in radians (+0x16d8), 30 the spark's life in ms (+0x16dc, `/1000` into +0x16cc by
  the shared lazy init `100f7fe7`), 31 spawns allowed per call (+0x16e0).
- Lazy init (`100f81e0`): loads **-100** and jumps to the shared fill (`100f80a6`), which
  `rep stosd`s it over all 128 death times at +0x1448, so every slot starts expired and the first calls
  spend their whole budget spawning.
- Per call, `p = age/dur` (or **1** once terminating, `100f9dbf`), budget = field 31:
  - **Alive** (age below the slot's death time): `t = (age + life − death)/life` runs 0 → 1, and the
    spark sits at `locator + (dir.x·f28·t, dir.y + 2p − 1, dir.z·f28·t)`. Size f11 on both axes, colour
    the ramp at t, frame 0.
  - **Spawn** (budget left, not terminating): the running angle +0x1650 gains field 29 and the direction
    becomes `(sin θ, 0, cos θ)`; the death time becomes `age + life`.
  - Otherwise hidden.
- Because the direction's y is always 0, **every spark shares the ring's height**: this is a circle of
  outward streaks, not a sphere, and `2p − 1` carries it from one below the locator to one above over
  the duration.
- The spawn branch jumps to the loop tail **without writing the sprite record**, so a slot that dies and
  respawns in the same call draws one more frame where it died. Reproduced, not smoothed over.
- Expiry takes the drain path (its `0x100fc374` entry is 0): +5 s and spawning stops, then the tail
  readies it once nothing is alive.
- 4 records (14300, 43118-43120), all size 0.4, duration 5, reach 0.8-0.9, turn 3.3 rad, life 600 ms,
  2 spawns a call, material 8. At 30 Hz that is 60 sparks a second living 0.6 s, so ~38 of the 128 slots
  are up at once. Verified live on 43118: radius settles at the reach, the ring climbs 0.01 → 1.90 over
  its 5 s, and it drains in one spark-life (ends at 5.64).

**Case 10** (`100f9615`, `StarsCase10`): still sparkles on a sphere shell that swell and shrink, the
whole cloud fading in and out over the duration.
- Fields:
  - 11 base size, 18-25 ramp, 26 duration
  - **28 spark life in seconds** (copied over +0x16cc every call), 29 shell radius
  - 30 non-zero → the 8-colour palette at `0x102c5f48`, 31 size swing in hundredths
- Spawn: ≤ 5 per call. `p = locator + unit(100d3005)·f29`, still.
- Per call, `p' = age/dur` and `e = 1 − (2p'−1)²`. Colour is the ramp at p' with alpha `_ftol(e·255)`,
  or the palette entry for `(slot & 7)` under the same alpha.
- Alive: `u = 2(timer−age)/life − 1`, size `(1−u²)·f31/100 + f11`, frame 0.
- Ready at once on expiry (no drain).

**Cases 7/8** (`100f9371`, `StarsRing`): 128 fixed directions.
- Type 7 is a flat ring `(sin a, 0, cos a)`. Type 8 spirals over a sphere `(sin 30t·sin t, cos t, cos 30t·sin t)`.
- `u = f31·age/dur`, `frac = u − floor u`, `s = √frac`.
- Size `f28·s + f29`. Position `locator + dir·f30·0.01·s`. Colour is the ramp at frac.

**Cases 16, 18, 19 and 20** (`100fa20f` / `100fa781` / `100fa9e4` / `100fac3e`, `StarsLineSparks`): tracer spark lines. They're built
from a hit location (ctor `100f7d4a` stores the hitloc id at +0x34 and a locator at the world origin).
- Per call:
  - 15 spawns
  - each spark is **static** at `end·q + start·(1−q) + ball·f29`
  - size `f28·(1 − (1−2t)²)` (can be slightly negative early; drawn at |size|)
  - frame `15 − _ftol(t·15.99)`
  - colour is the ramp at t
- Case 19 (the trail's head): `q = age/dur`, life `f30/1000·((rand()&0x7ff)·1e-4 + 0.9)`.
- Case 16 (the whole line at once): `q = (rand()&0x7fff)/32768`, life exactly `f30/1000`.
- Case 18 (`100fa781`, 17300, the tracer of ~750 nanos): case 19 with a life of exactly `f30/1000` and
  **10** spawns per call. It writes the life as `f30/1000·(1 − (i%17)/34)` for slot i, but the division
  is an integer one, so the factor is always 1. At 30 Hz and 300 ms that's ~90 sparks at the head.
- Case 20 (`100fac3e`, 45686, the tracer of 28597 Burning Bones): case 19's spawn with a life of exactly
  `f30/1000` and 15 spawns per call. While alive the size is **f28 throughout** and the frame counts
  **up**, `_ftol(t·63.99)` (64 cells, no clamp). Colour is the ramp at t.
- No hit location → no spawns. On expiry all four are just ready, with no drain.
- Every record of these types has field 0 = 5 (world mode): 173 of type 19, 1 of type 16 (17100, the
  tracer of ~1k nanos), 1 of type 18 (17300), 19 of type 20 (17900-17911, 45680-45688, 45710-45717;
  life 250 or 450 ms, material 9 or 33/39).
- Handled as stock tracers (no SetDuration).

**Case 17** (`100fa44b`, in `StarsLineSparks`): a helix of sparks round the hit-location line.
- Spawn:
  - 15 per call; `q = rand()/32768`
  - `D = end − start` (x nudged by 0.001 under 1e-5); `a = FindPerpendicular(D)`, `b = unit(a × D)`
  - `θ = √|D|·2q + 4·age/dur + f29`
  - spark at `start + D·q + (b cos θ + a sin θ)·2q(1−q)`
- Life `f30/1000`. Colour is the ramp at **age/dur**. Size and frame as case 19.
- Three records with f29 = 0 / 2 / 4.1 make the three strands of nano 75347's tracer.

**Case 15** (`100f9f5f`, `StarsBodySparks`): sparks thrown off the host's skin along its normals (43723,
the hit of 28604 Electrifying Containment; 43618, the damage-shield buffs).
- Fields: 18-25 ramp, 26 duration, 28 size (+0x16d4), 29 speed (+0x16d8), 30 life ms. Field 31 is loaded
  but unused.
- Lazy init (`100f811f`):
  - The locator's dynel is cast to `n3VisualDynel_t` and asked for `GetCatMesh()`. With no CATRender
    (+0x90), +0x16f0 is cleared and Process returns before the age moves; it tries again next call.
  - Otherwise it keeps the render's +0x78 as a scale (+0x16ec) and registers the vertex callback
    `100f740b` on the render (`CATRender_t::RegisterVertexProcessCallback`, the same hook the Deformer uses).
  - Every timer is set to −100. The 30-entry tables are filled with position (0, 0, 0) (+0x48) and normal
    (0, 1, 0) (+0x1b0), and the next entry (+0x16e8) is set to 0.
- Vertex callback `100f740b`:
  - randy31 (`100546a9`) calls it once per CAT group as the mesh is drawn, with the group's vertex count
    and its base index in the mesh.
  - Stride n = `GetTotalVertexCount() / 31`, at least 1. It walks the group from vertex `rand() % 30` in
    steps of n. Each vertex is copied (skinned position and normal) into entry `(base + index) / n`
    while that is below 30.
- Per call:
  - P = `Vehicle_t::GetGlobalPos` less the locator's local-mode position (`10106306`; zero in world
    mode, which every record uses). Q = `n3Dynel_t::GetGlobalRot`.
  - Alive (`age < timer`): `v *= 0.96`, `p += v·0.1`. The record takes p, and with
    t = (life + age − timer)/life, size `(1.1 − t)·f28`, colour the ramp at t, frame 0.
  - Due: up to **10** per call, none while terminating. From entry k = +0x16e8:
    `p = P + Q(pos[k]·scale)`, `v = Q(normal[k]·f29)`, then k = (k + 1) mod 30. The record is left as it
    was.
  - The tables are only as fresh as the last draw, so the first call spawns from the defaults: 10 sparks
    leave P (the dynel's feet) straight up.
- Expiry drains like case 3 (terminating + 5 s). Terminating with nothing alive readies it (`100f90c1`).
- Port:
  - `CatMeshDeformHost` also takes readers (`ICatVertexReader`). In LateUpdate it bakes each CAT
    SkinnedMeshRenderer and hands it over as one group, in renderer order.
  - `GfxControlStars` keeps each sample in the dynel's frame (attach 0, without the template), so the next
    call places it with the frame it has then, as stock does. Scale is 1: the baked vertices already
    include the renderer's scale.
  - Replayed at 30 Hz (§3.7 group B).
- 14 records, all world mode, attach 2000, material 8:
  - 43047-43052, 43627, 43628, 14800, 97023: 10-20 s, size 0.2-0.5, speed 0.5, life 350 ms. These are the
    damage-shield buffs (29645 Immolation Shield, 29781 Spike Armor, …), under a Shield.
  - 43723, 43726, 43759, 43766: 0.2-0.54 s, size 0.7-2.9, speed 4.2-8.2, life 850-1150 ms. These are
    hits (28604, 28609 Freezing Surge, 45224 Lesser Coronet of Frost, …).
- Seen live:
  - 43723 on the target: 70 sparks in 0.2 s, flying out to ~6 m as they shrink, gone at 1.09 s.
  - 43048 on the caster: ~110 orange sparks drifting about 0.4 m off the skin.

**Case 22** (`100fb299`, `StarsLimbSparks`): sparks drawn onto the host's limbs (e.g. 43426, a heal hit).
- Each call reads 12 attach points on the locator's dynel (`10106078`, ids at `0x102c5fe8`:
  1013/1011, 1014/1012, 1000/1006, 1009/1007, 1010/1008, 1007/1008) as 6 segments (base P, vector D,
  length L): calf→thigh ×2, pelvis→head, forearm→upper arm ×2, shoulder→shoulder.
- Spawn:
  - up to field 31 per call
  - segment `k = rand() % 6`, `q = (rand()&0x7fff)/32768`
  - direction r = a unit vector on the XZ circle (`100d3215`), mapped onto
    `a = FindPerpendicular(D)` and `b = unit(a × D)`
  - start at rest at `P + D·q + r'·f28`
- Each call after that:
  - `s = clamp(dot(D, p−P)/L, 0, 1)`. This divides by L, not L², so on limbs shorter than 1 m the
    sparks slide toward the base.
  - `c = P + D·s`, then `v += (c−p)·0.1`, `p += v·0.1`
  - a spark within 0.04 of c is due again
- Life `f30/1000`, size `(t·0.7 + 0.3)·f29`, frame `_ftol(t·15.99)`, colour is the ramp at t.
- The lazy init sets the timers to 0. On expiry it drains like case 3.
- All 37 records are world mode, attach 1000.

Other starTypes are still the previous developer's approximations. By nanos they hold back (2026-09-22):
5 (26), 13 (15), 9 (12), 14 and 12 (6 each).

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
    Unity skins on the GPU. Stars case 15 reads the same callback (§5.8).
  - So `CatMeshDeformHost` (`[DefaultExecutionOrder(20300)]`) BakeMeshes each SkinnedMeshRenderer,
    pushes the vertices, draws with `Graphics.DrawMesh` and sets `forceRenderingOff` on the original.
    BakeMesh works on the non-readable CAT meshes.
  - Bind positions come from `CatMeshSourceVertices`, added by `CatMeshLoader`.

### 5.10 Suns (0x7d5) sunType 4: `SunsSim`
vftable `1016de8c`, loader around `100fd16a`, Process `100fc425` (type switch on field 10; type 4 at
`100fc48b`), visual **GfxVisualSol** (32 sprites of 0x28 bytes, additive).
- Fields: 0 flags, 9 material, 10 sunType, 18-25 ramp, 26 duration, 28 size, 29 frame scale, 30 life ms.
  sunTypes above 4 get duration 0.
- Type 4, per call (so it's replayed at 30 Hz):
  - up to 3 spawns, each still at `start·(1−q) + end·q` (`q = rand()/32768`, from the hit location)
  - life `f30/1000`
  - size `f28`
  - frame `15 − _ftol(f29·t)`
  - colour is the ramp at t
  - screen angle `(slot % 7)·0.3`
- On expiry it's just ready (no drain). Slot 6 stops the spawns; a call with nothing alive then readies it.
- GfxVisualSol (`10020831`) draws a camera-facing quad per sprite: `A = (sin a·w/2, cos a·h/2)` and
  `B = (−cos a·h/2, sin a·w/2)` in screen axes, corners `P ± A ± B`, texture u along A and v up along B.
  The port uses `Quad.UseAxes`.

**Suns sunType 0** (`100fcea7`, `SunsSim.StepHalo`): 8 sprites stacked 2 above the locator, e.g. the glow over
a blessed character.
- Per call, q = age/dur and envelope e = 1 − (2q − 1)⁴ (0 at both ends, 1 in the middle).
- Sprite i (0..7) sits at `locator + (0, 2, 0)`:
  - size `(f28 + i·f29)·e`, width and height alike
  - turned by `(i − 4)·age/2`, so the layers spin at different rates and in both directions
  - colour: its entry of the D3DCOLORs at `0x102c60a0` (the same 8 as Stars case 10's palette),
    with alpha cut to 0xc0
  - frame 0
- Terminating readies it after the call. It depends on age alone, so it runs every frame (§3.7).
- 2 records:
  - 43768: pelvis attach, 180 s, sizes 0.6 + 0.25i. It's the buff of 166 nanos: the Blessed by the
    Firefly / Wolf / Spirits / Phoenix / Frog / Bear / Eagle lines, and Saved from False Redemption's hit.
  - 14400: 10 s, sizes 1 + 0.25i. All four slots of 269708 Blessed by the Ancients, and NCU Overload's hit.
- Seen live on 269708: a bright layered glow 2 m above the pelvis (y 2.95), growing to 1–2.75 m.

**Suns sunType 1** (`100fcbeb`, `SunsSim.StepRing`): a ring of 18 sprites round the locator, e.g. the
lights circling a stunned head.
- Dynel ctor `100fd851`. The locator takes fields 1-7 through the shared `100d2f58` (see §9).
- Loader offsets: 26 duration, 28 size (+0x62c), 29 spin (+0x630), and fields 30/31 (+0x634 / +0x638)
  read as flags.
- Per call, q = age/dur:
  - x = q, or 1 − q with field 30 set. In that case, below 0.2 it's eased to x = 1 − (1 − x)·(5x)²⁷.
  - w = 1 − x before the easing.
  - Alpha `_ftol((1 − x²)·255)`, radius `s = 1 − w²`.
  - Sizes 0.9 and 1.1 × f28 × s. With field 31, both also gain f28·(1 − x)⁸, and the radius stays ≥ 0.3.
  - Pair k = 0..8: θ = 2k·6.28/18 + age·f29, at `locator + s·(sin θ, 0.1·sin(4·age + 3θ), cos θ)`.
    - The first sprite is turned by 2·age.
    - The second is turned by −1.5·age and shifted 0.05·s along the screen's x.
    - Each takes palette colour 2k or 2k + 1 (8 D3DCOLORs at `0x102c60c0`) under that alpha, frame 0.
- It follows the locator's world-mode position every call. A lost locator, or slot 6, terminates it,
  and the tail (`100fcfae`) readies it after that call. Expiry readies it at once.
- It depends on the age alone, so the port runs it every frame, including the arming call at age 0
  (§3.7), rather than on type 4's 30 Hz replay.
- Slot 13 (`100fd09c`) sends the packed colour to both slots 11 and 12, unlike Fire's fade to alpha 0.
- 2 records, both used as buffs or hits:
  - 43307: head attach 1006, 6 s, size 1.8, spin 9.25, field 31. It's the buff of 17 nanos
    (125772 Stunned by Brawl, Power Bolt, Massive Detonation, …).
  - 43068: pelvis attach, offset x −0.6, 4 s, size 1.3, fields 30 and 31. All four slots of
    276012 Butterfly Kick.
- Seen live on 276012 Butterfly Kick (43068): the ring closes from 1.0 to 0.3 m, and alpha rises then
  drops to 0 in the last fifth. It sits 0.6 m above the pelvis, because the −0.6 x offset runs along
  the pelvis bone's x axis, which points down.
- Seen live on 125772 (3 s buff):
  - The ring sits on the head at 1.5 m and opens from 0.3 to 0.98 m while fading from alpha 248 to 63.
  - The colours are soft, and it's gone when the buff ends.

### 5.11 Shield (0xbbb): `ShieldSim`, `GfxControlShield`
vftable `1016d9a4`, loader `100edeed`, visual build `100edf78`, Process `100edcf4`, slot 6 `100edc9c`.
DisplaySystem **GfxVisualShield**: ctor `1001ce93`, vertex callback `1001cd09`, vertex build `1001c94f`,
draw `1001c8a5`.
- A shell over the host's own skinned CAT mesh. The visual registers a CATRender vertex callback, copies
  the skinned vertices and indices, and draws them as a triangle list with the field 9 material.
- Fields:
  - 0 flags, 8 duration, 9 material
  - 10 colour, 11 normal offset, 12-14 / 15-17 wave point / direction
  - 18 UV mode, 19/20 UV scale, 21 fade-in rate, 22 wave speed, 23 fade-in limit, 24/25 UV scroll
  - 26 pulse mode, 27/28 pulse range, 29 fade mode, 30 fade time, 31
- Flags: 0x400 blend (passed to the base visual at +0x190; read as additive), 0x800 / 0x1000 wave,
  0x2000 ripple, 0x8000 render priority, 0x10000 the host mesh's own material. Nothing in the Shield reads
  0x4000 (set on 12350).
- Per vertex:
  - position `p + n·f11`
  - UV mode 0 `((u + f24·T)·f19, (v + f25·T)·f20)`; 1 cylindrical `atan2(x, z)`, where v also scrolls by f24; 2 planar
  - alpha `A·fade·sin²(clamp(f21·T, 0, f23))`
- T is the age, bent by the pulse modes. The control terminates itself at `duration − f30`. Fade mode 1
  scales alpha by `1 − d/f30`; modes 2 and 3 run T backwards; it's ready at `d ≥ f30`. It's hidden
  while the host is below 0.95 opacity (not ported).
- Port: the control bakes each SkinnedMeshRenderer every frame, offsets it, sets the UVs and draws it
  through `EffectBillboardBatch.MeshDraw` in one colour.
  - The shield texture is set to Repeat.
- **Per-vertex alpha** (vertex build `1001cab2`..`1001cd00`), ported 2026-09-22:
  - Wave, 0x800 (visual +0x1ac; the Gamecode build `100ee0e3` sets it with the point, fields 12-14, at
    +0x1b0): the fade-in's sine takes `x = f21·T − dist·f22` instead of `f21·T`, still clamped to
    [0, f23]. dist is measured from the pushed-out position: `|p − point|`, or with 0x1000 (+0x1ad) the
    offset along fields 15-17 set to length 1 (+0x1bc, `100439aa`), `d.y·dir.y + dir.x·d.x + d.z·dir.z`.
  - Ripple, 0x2000 (+0x1ec, the ctor's 5th argument): after the fade-in, each vertex's alpha byte is
    scaled by `sin²(10x + 10y + 10z + 3T)` over its source position (the CATTriVertex_t, stride 0x44).
    The port uses the bind positions (`CatMeshSourceVertices`), the same assumption as the Deformer.
  - The shell's renderers sit in the dynel's own frame (checked live), so baked positions are stock's
    model space.
  - Drawn with the vertex-colour effect shader (`Hidden/LostEden/EffectVertexColor`, §6): field 10's RGB
    and the per-vertex alpha on each vertex.
- **Host material**, 0x10000 (`100edfd6`): the shell takes `CATRender +0x1d8[0]` instead of field 9's
  material. randy31 keeps that list by the CAT file's material index (`10057a30`, sized from the mesh's
  material count), so it's material 0. The loader now records each submesh's `MaterialId` on
  `CatMeshSourceVertices`; the shell takes the texture of the submesh using material 0 (on the GfxTest
  body, texture 8850). A body where no submesh uses entry 0 takes its lowest entry.
- 0x8000 only calls `RVisual_t::SetRenderPriority(3)` and sets +0x1a4 = −1 (else 1). Not ported; it
  changes draw order only.
- The records that use them: 43722 (0x2400, ripple, dark blue, 0.7 s: the hit shield of 60 nano uses,
  e.g. 28609 Freezing Surge), 43761 (the same, 3), 12351 (0x1a400, host material + ripple + 0x8000, 18)
  and 12350 (0x16400, 7). The wave records (32201-32204, 0xc00) are used by no nano.
- Seen live:
  - 43722 on 28609's target: a pale icy shell ~0.3 m out, fading in over 0.7 s, patchy where the
    ripple bands cross it.
  - 12351 on the caster: a ghost outline in the host's own texture. Its 615 vertices carry alpha from 0 up
    to the uniform value, averaging about half.

### 5.12 Sequencer (0xbbc)
Process `100ecb95`, slot 6 `100ecb55`.
- Entries are `{id, start, end}` from field 2. It runs off `t = age − cycle start`.
- An entry spawns once `t ≥ start` and (`t < end` or `end ≤ start`), using the Sequencer's own locator
  kind. Then, if `end > 0`, **`SetDuration(end − start)`** on the child.
- A child that's ready is deleted.
- Process **clears the Sequencer's own ready flag every call**, so a duration never ends it. It ends when
  every entry is done; with flag 0x400 it restarts instead.
- Slot 6 clears 0x400 and terminates the children gracefully.
- The port's `GfxControlSequencer` follows this. It ignores SetDuration and passes `IgnoreWatchdog` on to
  its children.

### 5.13 Buff FSM (0x3e9) and BuffPlaceHolder (0x3ea)
The catalog used to call these "not rendered". They draw nothing themselves, but they spawn effects that do.
- **BPHFSM** (`GfxControlBuffFsm`; vftable `1016c444`, loader `100d3f12`, Process `100d4066`)
  - Fields: 10/11 effects A/B, 12-15 / 16-19 start/stop colour, 20 interval, 21 count.
  - A countdown runs out every f20 seconds. Then, if 5 s of game time have passed since any BPHFSM
    fired on this host (a shared per-dynel map, `0x102ea67c`), it creates A and B on the host with the
    colours pushed in. Otherwise it retries in `rand()/16384` s.
  - The count counts down and it's ready at 0.
  - Slot 6 readies it. Slot 8 alternates between setting the duration and the interval.
- **BuffPlaceHolder** (`GfxControlBuffPlaceHolder`; vftable `1016c4fc`, loader `100d54ca`, dynel ctor
  `100d5d20`, init `100d5834`, Process `100d52c3`, colour push `100d55d9`)
  - Fields: 1-7 locator, 10 point child, 11 dynel child, 12-19 colours, 21 radius, 22 start phase,
    23 rate, 26 per-body table.
  - Per-body table (`EffectBodyTable`): on a character host, the (offset z, radius) pair at
    `(Breed−1)·12 + (Sex==3 ? 6 : 0) + Fatness·2` replaces field 3 and field 21. The ctor also takes
    Scale (stat 360) / 100. Radii run 0.09 (female heads) to 0.13 (Nanomage, Atrox).
  - Both children belong to the BuffPlaceHolder (`CreateGfxControl`, not the handler).
  - Child A (a point effect) starts at `pos + Z·sin·R + X·cos·R`. Child B is created with `(id, dynel, 0)`.
  - Process: `acc += rate·dt`, and the phase follows in 0.45 rad steps. Each step:
    - child B gets `UpdatePosition((cos, sin, 0)·R·scale)` and runs its Process
    - then child A gets `pos + Y·sin·R + X·cos·R` and runs its Process
    - So the children advance once per step, each by a whole frame's delta. The port uses one stock
      frame (`StockProcessHz`), by decision; see §9 "BuffPlaceHolder child delta".
    - A negative rate never steps (`_ftol` gives a negative count).
  - Slots 11/12 store the colour and push it into both children. A BPHFSM's colours therefore replace
    the record's: nano 25988's buff is white, not the record's violet.
  - Slot 6 readies it. Release deletes both children.
- Coverage walks fields 10/11 of both.

### 5.14 Cord (0x3eb): `GfxControlCord`
vftable `1016c564`, loader `100d60d5`, dynel ctor `100d67b1`, visual build `100d627a`, Process `100d5f5a`,
slot 4 `100d69d8`, link append `100d6323`. DisplaySystem **GfxVisualCord4** `(material f9, null,
additive, no life-v)`, with a 32-link `Cord4CircularLinkList` (`GetNew 1000e9b7`, `GetFirst`/`GetNext`
newest→oldest, purge `1000e9f9` after the render).
- Fields: 0 flags, 8 duration, 9 material, 12 link width, 16-19 start colour (A,R,G,B), 20-23 stop colour
  (stored, never read), 35 link life, 36 per-body table (offset z only). Rate, cone and magnitudes go
  unread.
- **Links come only from slot 4, in local mode (field 0 bit 1).** While `duration < 0` or
  `age < duration − life`, `UpdatePosition(p)` appends a link with:
  - point = velocity = p (the same pointer is pushed twice)
  - width f12, the start colour packed as `fistp(c·255 − 0.49999)`, life f35
  - Without bit 1, slot 4 is the locator's `101064f7`, which does nothing on an attach, so the Cord
    never draws. RunFunction never calls slot 4, so a Cord draws only when its owner feeds it; all 21
    local Cords sit under BuffPlaceHolders, except one under Nano3 9010.
- **Process** (the arming call too):
  - The visual takes the locator's position and turn, so link points are in the locator's frame and
    ride along with the head.
  - Each link, newest first:
    - `p += v·clamp(dt, 0.01, 0.025)`, so the trail spreads outward
    - `life −= dt`
    - alpha byte = `_ftol(life / f35 · 254)`
- Slot 6 sets duration = age + life. If the locator is lost, it does the same.
- Drawing is `Cord4Strip` with v = 0.25; vertices carry each link's ARGB. The port draws the whole
  ribbon as one strip with those vertex colours (vertices 2k and 2k + 1 are link k's).
- `_GfxLocator_t` notes (`10106be2`, dynel kind 4):
  - `+0` = field 0 bit 0 (update each frame); `+8` = bit 1 (local mode)
  - Local-mode accessors (`10106306` pos, `10106346`/`377`/`3a8` axes, `101062d5` matrix) return the
    locator's own values, or zero/identity without bit 1.
  - World-mode accessors (`1010640a`, `10106439`/`46a`/`49b`, `101063d9`) are the reverse.
  - The template rotation (fields 4-6) and offset (fields 1-3) apply on every update (`10106193`).
    The port applies them (`EffectLocator.WithTemplate`) to every ported type whose dynel ctor passes them
    (`EffectHandler.TakesLocatorTemplate`, §9).

### 5.15 Electra (0x7d6) mode 1: `ElectraSim`
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

### 5.16 BParticle2 (0xbd4): `BParticle2Sim`, `GfxControlBParticle2`
vftable `1016effc`, loader `1010b196`, init `1010bf63`, Process `1010b499`, slot 6 `1010af68`. Each particle
owns a DisplaySystem **GfxVisualBParticle2** (ctor `1000b432`, geometry `1000b4fc`). The base is the
"B" particle family: `+0x8c` locator, `+0x94` emitter, `+0x20` visible, `+0x28` terminate.
- Fields (read in order from 9):
  - 0 flags, 8 duration, 9 mode (0 burst / 1 emitter), 10 material, 11 count
  - 12 emit interval, 13 spawn cube half-size, 14 drag, 15 spawns per interval, 16 gravity y
  - 17/18, 19/20, 21/22 velocity ranges; 23/24 spin range in degrees (stored as radians)
  - 25/26 life range, 27/28 size range, 29 bounce, 30 growth, 31 frame speed, 32 frame mode (1 loop,
    2 hold, 3 ping-pong, sharing one speed across particles), 33 aspect, 34 → visual +0x1a4
  - 35.. a keyed colour curve (`StockColorCurve`: count, then (time, D3DCOLOR) pairs; `1011678b` blends
    with randy31 `Color_t::Interpolate`, w = `(int)(f·256)`, bytes `(a·(256−w) + b·w) >> 8`; t before the
    first key or at/after the last gives the last key)
- Flags:
  - 0x100 swaps the quad's UVs, 0x200 additive, 0x400 emitter on the ground
  - 0x4000 no random start angle, 0x8000 bounce
  - 0x10000 respawn at ground + locator offset y, 0x20000 random start frame, 0x100000 stick to the emitter
  - 0x200000 live until terminated, 0x800000 fade 3-6 m above the ground
  - 0x400000 time-of-day fade and 0x1000000 host body scale are not modelled.
- Spawn draws, in order: size, life, frame, position (emitter ± cube·f13), velocity, then
  acceleration (0, f16, 0), angle (r·2π), spin. Velocity and acceleration go through the locator's turn
  (`1013bc38` inverse, times the scale, then `1010aef4` rows·v). Draws come from the client's shared R250
  (`1013dec9` on `0x102ead20`); the port uses any uniform.
- Per call: `life −= dt`; dead particles hide.
  - Otherwise:
    - bounce
    - `p += v·dt`, `v += a·dt`
    - `angle += spin·dt`
    - `size += (f30−1)·size·dt·100`, `v += v·(f14−1)·dt·100`
    - colour = curve(1 − life/total)
    - frame advance
  - Mode 0 is ready when nothing is alive. Mode 1 respawns up to f15 dead particles once f12 seconds have
    gathered, while `age < duration − f26`.
- Emitter and turn come from the local-mode accessors (field 0 bit 1): without it the world origin. Slot 6
  only raises the flag; without 0x200000 the next Process ends it. At the duration everything goes at once.
- Visual: a quad on the camera's right/up turned by the angle about the view axis, half-width size,
  half-height size/f33. u runs from the right edge to the left and v top to bottom (0x100 swaps them).
- Ground height (`100d33f3`, the playfield under a point) is a downward raycast in the port
  (`EffectGround`), NaN when there's nothing.
- The base control ctor `100d2a4b` defaults the duration to **60 s**. Its `+0x20` flag is visibility,
  starting 1; the base Process's 60 s counter only runs while a control is hidden. The port's blanket
  60 s watchdog is its own.

### 5.17 TParticle (0xbcc): `TParticleSim`, `GfxControlTParticle`
vftable `1016f9dc`, loader `10112ccd`, visual build `10112b4e`, Process `10113101`; DisplaySystem
**GfxVisualTParticle** (ctor `10029de9` with 21 arguments, ProcessParticles `10029c81`, geometry
`1002a350`). The particles live in the visual; the control only animates colours and widths.
- Fields:
  - 0 flags, 8 duration, 9 material, 10 mode, 11 count, 12 radius, 15 trail time, 16 gravity y
  - 17/18, 19/20, 21/22 velocity ranges, 32 spin range (degrees; kept, never drawn)
  - Colours: 23/25/27 tail at start / middle / end, 24/26/28 head.
  - 34 = 4 eases the blends as t⁸ (`1003dfc0` integer power).
  - Half-widths: 35/37/39 tail, 36/38/40 head.
- Flags: 0x100 swaps UVs, 0x200 additive, bit 1 places the visual on the locator (else the origin).
- Spawn by mode:
  - 0: `p = unit(cube draw)·r·radius`, v in the box, `a = (0, g, 0)`
  - 1: tails only at `(2r−1)·radius`
  - 2: angle a, `p = (sin a·R, r·600, cos a·R)`, `v = (sin(a+2)·vx max, vy max, cos(a+2)·vz max)`
  - 3: zero with `v.z = 1`
- ProcessParticles: modes 0/2 `p += v·dt; v += a·dt`, then `tail = p − v·trail`.
- The control: with `u = time/duration` (ready past 1), colours and half-widths blend start→middle over
  `2u` and middle→end over `2u − 1`.
- Drawing: two crossed quads per streak, on `side2 = |side1 × d|` then `side1 = |d × up|`, where
  `d = tail − head`. Head pair in the head colour and width, tail pair in the tail's.
- The port draws each streak as one quad with the head and tail colours on its vertices, all streaks
  in one draw.

### 5.18 BParticle (0xbd0) modes 8 and 1: `BParticleSim`, `GfxControlBParticle`
vftable `1016efa4`, loader `1010a644`, visual build `1010a412`, Process `1010a909`. The particles live in
DisplaySystem's **GfxVisualBParticle** (27-argument ctor `10009938`, ProcessParticles(dt, u) `10008bce`,
per-particle draw `1000a70f`).
- The control:
  - It keys colour (fields 25/26/27) and width/height (31-36) over `u = time/duration`: start→middle
    before field 40 (0.5 when negative), middle→end after field 41, stored as `1 − f41`.
  - With no duration u is −1 and it holds the middle key.
  - Field 13 (the motion) = 4 eases the sizes as t⁸.
  - Ready past u = 1.
- Visual fields: 12 spawn cube, 13 motion → +0x204, 14 quad shape → +0x1e8, 15 life policy → +0x1ec,
  16 rate → +0x1fc, 17 life rate → +0x200, 28-30 camera-distance fade, 38/39 frame speed/mode, 42/43.
  The first and last frame are **not** record fields: they come from the material's frame count
  (`1010a44a`), which is why they are constructor arguments. Fields 18-24 (a respawn acceleration and a
  velocity box) are byte-identical in all 33 records and are read only by mode 12's out-of-line respawn
  at `10009754`, so no other mode uses them.
- **The generic per-frame path** (`10008bce`, everything but modes 7/8/9/11/12) runs four stages, each
  its own pass over the array:
  1. `+0x24 += +0x28·dt`, wrapped down past 360 and up past 0.
  2. The frame machine on `+0x2c` when the frame mode isn't 0: −999 freezes it; past the last frame,
     mode 1 restarts, 2 sets −999, 3 reverses; before the first frame only mode 3 reverses.
  3. The motion: **0** integrates velocity then acceleration; **1** is an orbit of fixed amplitude
     1 / 0.4 / 0.7 about `+0x4c`, its angle at `+0x1fc` per second.
  4. The life policy on `+0x38`: **1** a random walk reseeded outside [0,1]; **2** a sawtooth over [0,2);
     **3** runs down and, below −1, restarts at 1 with a new UV offset and angle, in place; **4** and
     **5** are the same code twice — restart at `r·0.5+0.5` **and** move to a new cube point.
  The draw applies the policy's alpha rule (`1000a855`) before the mode's own (`1000a919`): policy 1
  caps the fade at the life, 3 multiplies it by the life, 4 multiplies fade **and** size by
  `sin(life·π)`; 1, 3 and 4 all hide a particle whose life has run out.
- **Mode 8** (the only one ported). All 16 records have motion 0, quad 0, policy 0, frame mode 1.
  - Spawned with life 999 (a sentinel) and delay `r·0.2` (particle 0: 0).
  - A particle still at 999 loses `f17·dt` off its life, so it leaves the sentinel on the first call.
    Otherwise the delay drops by `f16·dt`; below 0 the particle pops with life `r·0.5 + 0.5`, delay 999,
    angle `r·360` and a new cube point. From then the life runs down at `f17` per second.
  - Drawn while `0 < life ≠ 999`, size and alpha × `sin(life·π/2)`. The first call's `999 − f17·dt` gives a
    one-frame glitch in stock, which the port keeps.
  - It never ends by itself, only by the duration or TerminateGracefully (base slot 6: ready).
- **Mode 1** (4 records: 71061 and 71032 in the Orbital Strike trees, 72332/72333; 72333 differs only in
  its frame mode). Spawned at the emitter (`10009e46`): position 0, angle `r·360`, no spin, scale 1,
  life `r·2−1`, UV offset `r`. With motion 0 and no velocity they **never move** — 30 quads stacked on
  the emitter, each fading in and out on its own cycle and re-aimed every time policy 3 restarts it.
  - Quad type **2** (`1000aea0`) is a tapered plume, not the symmetric billboard: the far edge is
    `pos + up·h·2 ± right·w·2` at v = 1 and the base is `pos ± right·w` at v = 0, where right and up
    are the camera's turned by the particle's angle about the view axis (stock turns them for every
    quad type, so the plumes radiate).
  - Mode 1 then offsets every vertex `v += +0x40`, mirrors `u = 1 − (u + +0x3c)`, and **clears the alpha
    byte of the two far vertices** (`1000ac88`), so the plume fades out along its length.
  - The port draws it as a strip (`Quads = true`) because the batch's quad path can't carry per-vertex
    colour. A strip holds one texture, so a run ends when the material cell changes; every mode-1 record
    has one frame, so it is a single strip in practice.
- The other modes (0, 2-7, 9-13; ctor table `0x1000a6db`) are not ported. Mode 13 (72340) is past the
  table's `cmp eax, 0xc`, so stock gives it no spawn at all.

### 5.19 GroundGrid (0xbd6) mode 0: `GroundGridSim`, `GfxControlGroundGrid`
vftable `1016f5ac`, loader `1010ebbd`, init `1010e951`, Process `1010e704`; DisplaySystem
**GfxVisualGroundGrid** (Update `100164b4`, SetAlpha `1001692d` → D3D texture factor).
- An N×N grid (field 10) at spacing f18, laid over the ground under the emitter plus f19. With flag 0x800
  it is laid once, otherwise every call. Flag 0x20000 picks a random turn after the first lay, so a
  once-laid grid never uses it.
- Alpha: `age/f14` while fading in, `1 − (age − (dur − f15))/f15` while fading out, else 1.
- UV: `(j+c)/(N−1)·uScale + uOff` and `(i+c)/(N−1)·vScale + vOff`, with `c = −(N−1)/2` under flag
  0x8000. Scale and offset change at fields 21/22/25/26 per second.
- Visual mode 0 is a uniform colour (f16). Modes 1 (diamond fall-off) and 2 (per-vertex table), and the
  flag 0x2000/0x4000 waves, are not ported.
- 71370 (in 71016) is a 200 × 200 m `groundhit.png` scorch, 60 s.

### 5.20 EffectMesh (0xbd1): `EffectMeshSim`, `GfxControlEffectMesh`, `EffectModels`
vftable `1016f0d4`, loader `1010caf8`, init `1010d457`, Process `1010ce9c`. One ABIFF model drawn by a
DisplaySystem `VisualMesh_t` (imports `SetMesh`, `SetPosition`, `SetRotation`, `SetScale`,
`SetTransparency`, `SetRenderingEffect`).
- Fields: 0 flags, 8 duration, 9 motion, 10 model, 16-18 velocity, 19-21 acceleration, 22-24 spin axis
  (zero = up, then normalised), 25 angle, 26 spin, 27 spin acceleration (radians), 28 scale, 29 its rate,
  30 that rate's rate, 31 rendering effect, 32.. a float curve (`StockFloatCurve`: the colour curve's
  loader `101166f2` in mode 0, evaluated by `1011689d`, linear, out of range = last key). Fields 11-15 are
  loaded but unused: init zeroes the offset that 13-15 fill.
- Model: a 28-name switch (`0x1010d7f6`), loaded as RDB mesh type 1010001 by name
  (`InstanceManager_t::GetTypeInstance`). An index past 27, or a name the database doesn't have, is ready
  at once. 1 (`EP03_mech_heal_effect.abiff`) is not in the database, so 71123 draws nothing in stock
  either.
- Per call: `time += dt`; alpha = curve(time / duration) when that is ≥ 0, else 1 (a negative duration
  holds 1). Motion 1 bobs: `offset.y += sin(2π·age/duration)·v.y·dt`; otherwise `offset += v·dt`, then
  `v += a·dt`. Angle, spin, scale and its rate step the same way, value before rate.
- Placement: the locator's local-mode position (the ground under it with 0x100) plus the offset. The turn
  is the axis-angle quaternion times the locator's turn (`1007ca22`). Every record spins about up on a
  yaw-only host or not at all, so the order never shows. The port writes locator turn × spin.
  - 0x400 turns the model's up axis to the camera instead (shortest arc, `100ad4f8`).
  - 0x800 swaps the alpha for a camera-distance ramp: 0 under 5 m, (d−5)/10 to 10 m, the curve from 10 m.
- Rendering (`VisualMesh_t::SetRenderingEffect`, DisplaySystem `1006ce7d`, per node `1006c61d`):
  - 4/5/6 (TransparentDeltaState, Transparent2, Transparent4X) all blend SrcAlpha/One, with no Z-write,
    no fog, no culling, and texture × diffuse. Nodes get render priority 6.
  - 4 has D3D lighting on; 5 and 6 are unlit (their states are identical).
  - Other effects keep the model's own material. 1 is Holo, 2 Space, 7 ZBias, 3 nothing.
  - The port draws 4/5/6 as additive `MeshDraw`s of the model's diffuse texture. For 4 the colour is
    clamp(emissive + diffuse) (full light); 5/6 are white. Alpha is transparency (× opacity for 4).
  - Own-material models use `AbiffMaterialFactory`'s HDRP Lit material. Below full alpha they use a
    transparent clone with `_BaseColor` alpha.
- 71028 (in 71016, also 71113 and 97020): `EP03_blast_wave_effect.abiff`, a flat dome of radius 1.5
  (the submesh base rotation lays it flat), effect 4, scale 1 + 120 t − 60 t² (~62× at 1 s), alpha
  0 → 1 at 0.1 → 0 at 1 s. Its material is emissive (1,1,1), so full light is exact.
- The dynel ctor (`1010da22`, kind 2, the only ctor that keeps the dynel's identity at +0xd8). The loader
  (`1010cce0`..`1010cd65`) reads, from that dynel:
  - Its breed (stat 4, +0xcc). With 0x8000 an Atrox (breed 4) has field 28 × 1.45 (`1016f0c0`), once.
    With 0x10000 an Atrox takes models 20, 21 and 27's Atrox versions (`1010d46a`:
    `hoverbike_a_effect_circle_atrox.abiff`, `hoverbike_b_effect_circle_atrox.abiff`,
    `hoverbike_b_effect_circle_red_atrox.abiff`); any other model makes nothing.
  - With 0x200, its body scale (`n3VisualDynel_t::GetBodyScale`, +0xac = stat 360 / 100, set by
    `SetBodyScale` from several Gamecode paths), read again every call while the mesh is shown
    (`1010cf2d`). The locator's frame is divided by it before its turn is taken (`1010d1f5`), and the
    visual's scale is body scale × scale (`1010d40b`). The port's turn is taken from the frame's
    normalised axes, which is the same; its body scale is stat 360 / 100, or 1 when the stat is unset,
    as `VisualDynel.ApplyScale` draws the body.
  - The ctor's visibility switch (`1010c9c9`, like Trail2's `10115071`) isn't ported: §9.
- 0x1000, an animated model (`1010d265`): each call anim time (+0xbc) += dt; once it and the tree's total
  (`VisualMesh_t::GetAnimationTreeTotalTime` → randy31 `RRefFrame_t::GetAnimationTreeTotalTime`
  `10045728`, the longest animation in the tree) are above 0, a time past the total is fmod'ed, and it goes
  to `VisualMesh_t::SetAnimationTime` → `RRefFrame_t::SetAnimationTime` (`1004506b`).
  - Each node: t = fmod(t, its animation's total), a whole number of totals above 0 being the total; then
    the animation's evaluate (`FAFAnim_t`, vftable `1008a7cc`, slot 3 `10028fde`).
  - The evaluate: looping, a t past the total is fmod'ed; not looping, held at the total. It sets the
    rotation, translation, visibility and UV tracks, each from a cached key index (`StockUvTrack` for UV):
    step back while key[k].time > t, then forward while key[k + 1].time < t. The UV time is held at the
    last key. Off key k, f = (t − k.time) / (k+1.time − k.time); the tiling (+0x84) always lerps; the
    offset (+0x8c) lerps only when the key's flag (AODB's `UVKey.Unk2`) is set, else holds. With fewer
    than two UV keys nothing is set.
  - The port evaluates the UV track per part and draws it as the texture's ST: the bake stores v as −v,
    so D3D's (tu, tv, ou, ov) is ST (tu, tv, ou, −ov). The code that applies +0x84 / +0x8c to the texture
    wasn't found in randy31; the port takes D3D's usual texture transform, u·tiling + offset (§9).
  - Only the hoverbike circles (72345, 72346, 72511, all effect 6) set 0x1000. Their models (271013,
    272411 and the Atrox 272410, 272418) scroll the offset u 0 → 1 over 0.8 s, looping, with rotation and
    translation constant, so only the UV track moves. The port reads only UV tracks, and applies the ST on
    the textured draw (effects 4-6), not on own-material draws; no 0x1000 record uses those.
- Not modelled (the records are Approx): the vehicle-direction fade 0x2000/0x4000 (`Vehicle_t::GetDir`;
  no record sets it), and the model's own effect attractors (`n3GenericEffect_t`).
- The vehicle buffs' records: 72345 / 72346 / 72511 (flags 0x11203: body scale, animated, Atrox model;
  the hoverbike circles, effect 6) and 72450 / 72452 / 72510 (0x8203: body scale, Atrox size; the
  hoverboard fans, own material), all on attach 2006 (see §9, "Trail2 attractors", for why that isn't
  checked on a vehicle).
- Seen live: 72345 (made directly on the GfxTest caster, Solitus, stat 360 unset): the circle's model is
  `hoverbike_b_effect_circle.abiff`; body scale 1; the UV offset ran 0 → 1 over 0.8 s and wrapped; up
  close, the bright bands on the two arcs moved between frames 0.25 s apart. The model's own vertices sit
  about 3 m from its origin, so on the head attractor the circle lies flat about 3 m behind the head.

### 5.21 MParticle (0xbd3): `MParticleSim`, `GfxControlMParticle`
vftable `1016f674`, loader `1010f5ee`, init `10110229`, Process `1010f92b`. N debris models, each its
own `VisualMesh_t` with no rendering effect (the model's own material).
- Fields:

  | Field | Meaning |
  |---|---|
  | 0 | flags |
  | 8 | duration |
  | 9 | mode: 0 one burst, 1 emitter |
  | 10 | read and dropped |
  | 11 | count N |
  | 12 | emit interval |
  | 13 | spawn cube half-size |
  | 14 / 15 | fade-in / fade-out, fractions of life |
  | 16 | gravity (y) |
  | 17-22 | velocity x, y, z ranges |
  | 23-25 | spin axis |
  | 26 / 27 | spin range, degrees/s stored as radians |
  | 28 / 29 | life range |
  | 30 / 31 | scale range |
  | 32 | bounce b |
  | 33 | model count M |
  | 34.. | models: indices into the first ten EffectMesh names (`1010f4c0`) |

  A zero axis becomes three R250 draws at load, shared by every particle. Flags: 0x100 puts the emitter
  on the ground, 0x800 enables the bounce.
- Init: each particle takes model `rand() % M`, from msvcrt's rand. The first Process call spawns them
  all in draw order: scale, life (= total), position (emitter + (2r−1)·f13 per axis), velocity, angle
  (r·2π), spin. Acceleration is (0, f16, 0); there is no locator turn and alpha starts at 0. Mode 1 keeps
  only particle 0, at life = f29.
- Per call, for each particle: `life −= dt`; at ≤ 0 it's hidden. Then:
  - With 0x800 and ground ≥ y: `v.x −= b·v.x·dt·100`, `v.y = −b·v.y·dt·100`, `v.z −= b·v.z·dt·100`,
    and spin = r-spin·|0.1·v.y|·b.
  - Then `p += v·dt`, `v += a·dt`, angle += spin·dt.
  - Alpha with x = 1 − life/total: x/f14 below f14; 1 − (x − (1−f15))/f15 past 1 − f15; else 1.
  - Mode 0 is ready once nothing is alive.
  - Mode 1 respawns one dead particle per call, turned by the locator, once f12 seconds have gathered
    while age < duration − f29 (never with a negative duration).
- The bounce depends on the frame rate: restitution is 100·b·dt. See §9 "MParticle step".
- 71045 (in 71016): 80 × `EP03_RUBIKA_asteroidbig2.abiff` (140 m across) at scale 0.006-0.012
  (~1-1.7 m chunks). Velocity ±80 / 10-40 / ±80, gravity −50, life 3-4 s, fade over the last half.
  Its duration is 1 s, so the base ends it (and hides every chunk) at 1 s, before any of them fade.

### 5.22 Sparks (0x3fa): `SparksSim`, `GfxControlSparks`
vftable `1016db8c`, loader `100f1758`, init `100f1939`, spawn `100f1bbe`, Process `100f24c8`. It drives
DisplaySystem's **GfxVisualSprite2Type0**, not Flare's visual. The visual's pool and ProcessSprites are
`Sprite2Type0Visual`, shared with Fire (§5.25).
- Fields:
  - 0 flags: 0x200 readies once the pool is empty; 0x800 alpha-blends instead of additive
  - 8 duration, 9 material, 10 sprites per second
  - 12/13 width start/end, 14/15 height start/end
  - 16-19 / 20-23 start/end colour A,R,G,B
  - 24 `rand()` mask gating a spawn call
  - 25/26 angle a, 27/28 angle b, 29/30 speed
  - 31 burst, 32 speed scale
  - 34/35 life
  - 36/37 wind band bottom/top above the emitter, 38 wind mode, 39 wind scale
  - 40 gravity (y, m/s²)
  - 11 and 33 are loaded but unused.
- Init:
  - The pool holds `max(_ftol(f10·1.5·f35), f31)` sprites, and the spawn counter starts at −f31.
  - Blend comes from the ctor argument `!(flags & 0x800)`.
  - Field 40 goes to the visual (+0x22c).
- Per call:
  - If `(rand() & f24) == 0` and (duration < 0 or age < duration − f35), it spawns until the counter
    reaches `_ftol(f10·age)`. A rate-0 record is one burst of f31 on the first call.
  - Then ProcessSprites(dt, the handler's wind).
- Spawn draws from R250 in this order: a, b, speed s, life, wind scale `(0.9r + 0.1)·f39`.
  - `v = (X·cos a·cos b + Y·sin b + Z·sin a·cos b)·s·f32`, where X/Y/Z are the locator's world axes
    (`10106439` / `1010646a` / `1010649b`; unit axes in local mode).
  - The sprite starts at the locator's world position (zero in local mode).
  - Width, height and colour ramp to their end values over the life. The frame runs from the
    material's first cell to its last.
- ProcessSprites:
  - `life −= dt`; below 0 the sprite dies.
  - Wind by mode (field 38), with h = (y − bottom)/(top − bottom) clamped to 0..1 (the band is fixed at
    spawn from the emitter's height). The drift is `wind·f·windScale·dt`, where f is 1 (mode 1), h (2),
    h² (3), (h² + h)/2 (4), 1 with `y += 1.5·dt` (5), h with a random walk `x, y += (r − 0.5)·dt` (6),
    or 0 (8+).
  - Mode 7 (`1002682b` / `10026a03`) steers instead of drifting:
    - Before the move, v = dir·(band top), with the band top acting as a speed.
    - After gravity, dir = v at length 1.
    - The sprite keeps a constant speed along a path that bends.
    - dir (+0x18) is drawn by every NewSprite as (2r − 1, 0.89r + 0.1, 2r − 1) from the visual's own
      random source. A mode-7 NewSprite sets the visual's gravity to its band bottom.
    - 80011 (Smoke): band −8.8 / 10.5, so debris puffs fly at 10.5 m/s and arc back down.
  - Then `p += v·dt`, `v.y += g·dt`.
  - Colour, frame and size step, and the colour is FISTP-packed (`c·255` as a float, − 0.49999).
- Slots: 6 sets duration = age + f35, 8 sets duration, 11/12 set start/end colour for later spawns.
  Stock Sparks has no light (the old emitter port had one).
- The wind is the effect handler's GetWind (§5.24). 14 of the 42 records use mode 4: black (43668,
  43675–43680) and white (43689, 43696–43701) sprite plumes at 0.5–8 per second, e.g. the Omni-Tek
  Rank 1–7 Effect buffs (one step per rank).
- 2710 (nano 45889's hit): a burst of 16 red 0.3 m sprites at 1.5–3 m/s in every direction, falling at
  9.8 m/s², fading out in 0.25–0.5 s, alpha-blended.

### 5.23 Tracer5 (0x401): `Tracer5Sim`, `GfxControlTracer5`
vftable `1016e0a4`, loader `101003f9`, init `1010077d`, build `101004b4`, Process `10100aa3` → update
`1010065a`. It's built from a hit location (`10100b82`), with start and end read once.
- Fields: 0 flags, 1-7 locator, 8 duration, 9 material, 10 speed, 11 streak length, 12 width, 13-16 colour
  A,R,G,B (FISTP-packed). 17-19 are read but unused.
- Init:
  - A line shorter than 0.01 is ready at once.
  - **Speed is capped at 5 × the length**, so the flight lasts at least 0.2 s.
  - The locator matrix is the same as Tracer4's: rows cross(dir, p), dir, p, start.
- Build: one GfxVisualCord4 (additive, life-v) of three links.
  - All three have width f12 and the packed colour.
  - Newest first, their lives are 1, 0.001, 0.001, with life scale 1, so v runs from 0 at the head to
    ~1 at the tail.
- Update:
  - `tail = speed·age`, `head = f11 + tail`.
  - The head is capped at the length, and the tail is kept ≥ 0.
  - Once the tail reaches the length, it is set to the length and the control is ready.
  - The links sit at (0, head, 0), (0, tail, 0) and the world-mode position (zero in local mode), along
    local y (the flight direction). The first two make the drawn quad; the third only turns its side.
- Slot 6 readies it; slot 8 is a no-op.
- All 13 records are local mode (field 0 = 2).
- The old `GfxControlTracer` stand-in still draws 0x3f5. 0x402 is now ported (§5.43).
- 45708 (nano 45889's tracer): speed 50, streak 4.5 m, width 0.225, colour (1, 1, 0.8, 0.8), s_bullet.png.
  Over GfxTest's 2.6 m line the cap makes it 12.9 m/s.
### 5.24 Wind: `EffectWindSim`, `EffectWind`
The effect handler's wind (`_EffectHandler_t::ComputeWind` `100cdee7`, `GetWind` = +0x24,
`GetSmoothWind` = +0x30) copies the weather controller's wind object (global `0x102e447c`, +0xc; read
`100bfbf0`). Sparks reads the raw wind.
- The wind object (0x58 bytes, ctor `100bfece`, created at `100b9090`) holds:
  - W, the base wind (+0): starts (1, 0, 0)
  - A, the output (+0xc)
  - B and C, two gusts (+0x18, +0x24)
  - D (+0x30): zeroed and never set again
- Per frame, from the weather update (`100beacd`):
  - **Strength** (`100bff68`, which also drives the wind sound):
    - s = the weather state's wind speed (+0x1c), clamped to 0–33, and at least 0.001.
    - W keeps its direction at length s.
    - Gust weight g = 1 − (1 − s/33)³.
  - **Update** (`100bfc80`, p = 0.9 and k = 0.5 from the weather controller), with m = 0.5g + 0.5 and
    kicks of `(rand()%100 − 50)/450/0.016` (y: /800):
    - 9 frames in 10, C += (kx, 0, kz)·k·m.
    - Again 9 in 10, B += (kx, ky, kz)·k·m.
    - Then A = W + B, B /= 1.02, C /= 1.02, and W += C/1000 while C ≠ 0, so the base direction wanders.
  - The handler then copies A as the raw wind and blends smooth = 0.04·raw + 0.96·smooth.
- The whole update is skipped while a display preference (`EnvironmentPreferences_t` byte +3) is off;
  the port always runs it.
- With no weather wind (s = 0.001), only the gusts blow: each axis wanders with a spread of ~2.5–5 m/s
  and a correlation time of ~50 frames. `rand()%100 − 50` gives stock a small negative bias
  (~−0.5 m/s on y).
- Port:
  - `EffectWind` (a static, like stock's singleton handler) steps the sim once per stock frame at
    `StockProcessHz`, once per Unity frame. It's called from `EffectHandler.Tick`.
  - `EffectWind.WeatherSpeed` is the weather input. It stays 0 because the port has no weather yet;
    that's stock's own no-weather state.
- Not traced: the weather controller itself, i.e. weather presets and blending (`100bd930` from the
  playfield's +0xc8..+0xe4, blender `100bd3b3`), which is where the real speed comes from.
### 5.25 Fire (0x3ec): `FireSim`, `GfxControlFire`
vftable `1016ccdc`, factory case in `100d0656` (0xa4 bytes), ctor `100dcb72`, loader `100dc433`, init
`100dc59c`, Process `100dbed5`. It drives the same **GfxVisualSprite2Type0** as Sparks (§5.22), through
its defaults.
- Fields:
  - 0 flags: bits 0-2 are the locator's; 0x100 sends the sprites up the world y instead of down the
    locator's z
  - 1-6 locator offset and turn (the ctor passes them to `10106be2`, like Cord); 7 attach
  - 2 is also a lift added to each sprite's y
  - 8 duration, 9 material, 10 sprites per second
  - 11/12 disc radius min/max, 13 speed
  - 14/15 width start/end, 16/17 height start/end
  - 18-21 / 22-25 start/end colour A,R,G,B
  - 26 life
  - 27/28 wind band bottom/top above each sprite's spawn height, 29 wind mode, 30 wind scale
  - 31 `rand()` mask gating a spawn call
- Init:
  - GfxVisualSprite2Type0(material, null, **true**): always additive.
  - The pool holds `_ftol(f10·1.5·f26)` sprites.
  - `InitSpriteDefault` gives every sprite width f14 → f15, height f16 → f17 and colour f18-21 → f22-25
    over f26, and the material's frames first → last. There's no gravity.
- Per call:
  - If the locator is lost, duration = age + f26.
  - If `(rand() & f31) == 0` and (duration < 0 or age < duration − f26), it spawns until the counter
    reaches `_ftol(f10·age)`.
  - Then ProcessSprites(dt, the handler's wind).
- Spawn:
  - r from R250 in f11..f12, then a random unit d (`100d3005`).
  - The point (d.x·r, d.z·r, 0) goes through the locator's world-mode matrix (`101063d9`: identity in
    local mode), then y += f2.
  - The velocity is |d.y|·f13/f26 down the locator's world-mode z (`1010649b`), or up the world y with
    0x100.
  - Default `NewSprite` (`10025be0`) with wind mode f29, band f27 + y .. f28 + y, and scale f30.
- Slots:
  - 6 sets duration = age + f26, 8 sets duration.
  - 11/12 set the start/end colour and call `SetDefaultColor`, so they affect later spawns only.
  - 13 (`100dc2ce`): packed ARGB, start = the colour, end = the same colour at alpha 0.
- 17 records. Wind modes 4 (10), 1 (4), 0, 3, 5. No record uses 0x100 or mode 7. Six are local
  (flags 6/7). Attaches: 0, muzzle 3000 (2008, 2204, 2209) and 2020 (80003).
- 2200 (the buff of the Rage line: 100250 Berserk, 100251–100254, 275830, 304605; also every slot of
  303345 Shroud of Lost Souls):
  - Flags 5, offset z −0.25, attach 0.
  - 16 per second off a 0–0.5 m disc, at up to 4 m/s.
  - Sprites are 3 m square, narrowing to 1 m, white on material 9 (fire), and live 1 s.
  - Wind mode 4, with a band 0–4 m above the spawn.
- Attach 0 is the CAT mesh's root frame (`10106591`: `GetCatMesh()` +0x90 → +0x3c
  `RRefFrame_t::GetWorldMatrix`). `VisualCATMesh_t::RunFunction` (`10074bb4`) sets that frame from the
  dynel's position and quaternion only. The CAT bone data is y-up under an identity root in the port,
  so stock's root frame must be upright too: its z is the character's forward.
  - So 2200's disc stands upright at the feet, and the flames stream out behind the character,
    horizontally. The floor hides the half below it.
  - The world-mode sprites stay where they were emitted, so a running character leaves a fire trail.
  - This follows stock's code, but it hasn't been compared with the live client (§9).
### 5.26 Smoke (0x3f1): `SmokeSim`, `GfxControlSmoke`
vftable `1016db34`, factory case in `100d0656` (0xa4 bytes), ctor `100f12bf`, loader `100f0b6b`, init
`100f0cd4`, Process `100f04bd`. The same **GfxVisualSprite2Type0** as Sparks and Fire. The port used to
call this type "Nano2" and drew a stand-in billboard.
- Fields:
  - 0 flags: bits 0-2 are the locator's; 0x400 keeps it running when the locator is lost
  - 1-6 locator offset and turn (the ctor passes them to `10106be2`); 7 attach
  - 8 duration, 9 material, 10 sprites per second
  - 11/12 disc radius min/max, 13 speed
  - 14/15 width start/end, 16/17 height start/end
  - 18-21 / 22-25 start/end colour A,R,G,B
  - 26 life
  - 27/28 wind band bottom/top above each sprite's spawn height, 29 wind mode, 30 wind scale
  - 31/32 height offset min/max
- Init:
  - GfxVisualSprite2Type0(material, null, `id == 80005`): **alpha-blended** except effect 80005.
  - The pool holds `_ftol(f10·1.5·f26)` sprites.
  - Its `InitSpriteDefault` computes the height rate as `f17 − f17` (a stock slip, `D9 C0 DE E1` at
    `100f0ef2`). It doesn't matter: every spawn uses the full `NewSprite`.
- Per call:
  - If the locator is lost and 0x400 is clear, duration = age + f26.
  - If duration < 0 or age < duration − f26, it spawns until the counter reaches `_ftol(f10·age)`.
    There's no `rand()` gate.
  - Then ProcessSprites(dt, the handler's **smoothed** wind, GetSmoothWind +0x30).
- Spawn draws from R250 in this order:
  - life `f26·(1 + 0.3r)`
  - angle a ∈ [0, 2π), angle b ∈ [0, π/2)
  - radius in f11..f12
  - size scale `s = 1 + 0.3r`
  - height in f31..f32
  - wind scale `(0.9r + 0.1)·f30`
- Local point `(cos a·cos b·r, height, sin a·cos b·r)`, through the locator's world-mode matrix
  (`101063d9`; identity in local mode).
- Velocity `(0.3x, 0.3z, −1)·f13/life`, through the same rotation (`100dcd23`).
- Width `f14·s` → by `(f15 − f14)·s` over the life; height `f16·s` → by `(f17 − f16)·s`. The colour ramps
  f18-21 → f22-25 and the frame runs first → last.
- Slots: 6 sets duration = age + f26, 8 sets duration, 11/12 set the start/end colour for later spawns,
  13 (`100f0a06`) is the packed ARGB as start and the same at alpha 0 as end.
- 17 records: flags 5 (10), 0x1001 (4), 0x1403 (3). Wind modes 4 (9), 0 (3), 5 (3), 6, 7 (80011). Five
  turn the locator 90° about x, which makes "down z" point up. 94 nano uses.
- 43657 (the buff of 83943 Claw Eyes, 83950–83952, …):
  - Attach 2002 (head attractor), offset 0.5 m along the turned y, i.e. in front of the face.
  - 2.6 per second, speed 0.
  - Black at alpha 1, 1.7–2 m wide and 1.5–2 m tall, life 2–2.6 s, material 31, 30 s.
  - A still, opaque black cloud over the eyes: 5–6 of the 7 slots alive.
### 5.27 Spiral (0x7d1): `SpiralSim`, `SpiralRibbon`, `GfxControlSpiral`
vftable `1016dca4`, factory case in `100d0656` (0x98 bytes), ctor `100f53a8`, loader `100f4ef1`, init
`100f4f45`, Process `100f4cb5`. DisplaySystem **GfxVisualSpiral**: ctor `10021eb4`, draw `10021924`.
- Fields: 0 flags, 1-6 locator offset and turn (the ctor passes them to `10106be2`), 7 attach,
  8 duration, 9 material, 10-17 a colour ramp.
  - The ramp is set by slots 11/12, and slot 13 sets both of its ends to one colour. Nothing reads it.
- Init: two GfxVisualSpiral(material, i·6.28/2, 6.28, 12), so two ribbons half a turn apart.
- Per call (p = 2·age/dur):
  - Both visuals sit at the locator's world-mode position (`1010640a`), turned about the world y by
    age·3.7 rad (`100520d9`: (axis·sin(half), cos(half)), the angle wrapped into [0, 2π)).
  - Each keeps a range: [0, p] while p < 1, [p − 1, 1] while p < 2, and the last range after.
  - It also takes the age (+0x1c8), which scrolls the texture.
  - A lost locator sets duration = age; so does slot 6.
- GfxVisualSpiral:
  - Ribbon: 12 segments round a turn of radius 0.6, climbing 0.3 per radian (1.884 over the turn),
    0.3 tall.
  - Vertices come in pairs, top then bottom: (cos a·0.6, 0.3h, sin a·0.6) and 0.3 lower, with
    a = offset + h. The cos/sin table is built in the ctor.
  - u = −age + i·(6.28·0.6/1.5/12) along the ribbon; v is 1 at the top, 0 at the bottom.
  - Trimming:
    - A start > 0 moves pair ⌊12·start⌋ part way (by the fraction) to the next pair.
    - An end < 1 moves pair ⌈12·end⌉ back towards the previous one, which becomes the last pair.
    - Stock resets a start above 1 to 0.
  - The first and last kept pairs get colour 0, so both ends fade. Every other vertex is the visual's
    +0x18c, white.
  - Draw: FVF 0x142 triangle strip, SrcAlpha/One, no lighting, no Z-write, no culling, texture ×
    diffuse. The texture wraps (D3D default).
- Port:
  - Each ribbon is one strip with stock's vertex colours: 0 on the end pairs, white elsewhere.
  - The material's texture is set to Repeat, as for Shield.
- 3 records, all flags 5:
  - 43010: offset y 0.25, attach 0, 4 s, material 32 `thin_smoke.png`. It's the hit of 282 nanos,
    e.g. 25994 Hostile Hatchling.
  - 43011: the same, offset x 0.5.
  - 11200: 32 Fire-style fields under this type (like 2200's, with material 32 and a y offset); Spiral
    reads only 0-17.
- Seen live: a white smoke double helix winding up the target from 0.25 to 2.1 m in 2 s, spinning,
  then unwinding from its base, gone at 4 s.
### 5.28 Tracer3 (0x3fe): `Tracer3Sim`, `GfxControlTracer3`
vftable `1016dfec`; built from a hit location (`CreateGfxControl(id, hitLoc)` case `100d1b07`, 0x7c bytes,
ctor `100ff996`) or from two points (`100ff933`). Loader `100ff657`, init `100ff7f3`, child build
`100ff6e2`, Process `100ff91b` (step `100ff754`).
- Fields: 8 duration (the base's), 10 speed (+0x38), 11-14 colour A,R,G,B (+0x3c..+0x48, packed into
  +0x50 with `fistp(c·255 − 0.49999)`), 15 the child effect (+0x4c). Field 9 is read and unused.
- Init:
  - Start and end come from the hit location once (+0x54 / +0x60).
  - L = |end − start| (+0x78). Under 0.01 the tracer is ready at once.
  - Otherwise the direction (+0x6c) is set to length 1 and the speed becomes min(f10, 5L), so no flight
    takes less than 0.2 s.
  - The child is created by position at the start (`100cea4b`). It gets SetStartColor(f11..f14) and
    SetStopColor(0, f12, f13, f14), then one Process.
- Process: the base timer, then d = speed · age. At d ≥ L it's capped at L and the tracer is ready. The
  child is moved to start + dir · d (slot 4) and processed.
- The child is owned (+0x2c): deleting the tracer deletes it, so the trail goes the moment it arrives.
- Slots: 6 ready at once; 8 (SetDuration) is the empty `10079931`; 13 stores the colour and passes it to
  the child, then processes the child again (the port only passes it on); 15 sets the speed.
- 10 records (2640, 2650-2652, 2660-2662, 2670-2672), each carrying a Nano0: 27 nanos' tracers, e.g. 28609
  Freezing Surge (2662 → 2685), 45931 Condensed Halon Jet and 45936 Chilling Stream (2660).
- Port: `EffectHandler.CreateTracer3` reads the endpoints and makes the child with `CreateOwnedControl`.
- Seen live on 28609: a 2.58 m line flown at 12.88 m/s in 0.2 s, 23 blue puffs along it, gone on
  arrival.

### 5.29 Nano0 (0x3ef): `Nano0Sim`, `GfxControlNano0`
vftable `1016d47c`; by position `100e73ea` (0xcc bytes, from `100cea4b`). Loader `100e67f8`, init
`100e7146`, spawns `100e69ca` (locator) / `100e6ca4` (a point), Process `100e6f76`. It draws with
GfxVisualSprite2Type0 (§5.22), as Sparks does.
- Fields as Sparks (§5.22) up to 34, then:
  - The loader reads field 35 twice: into the life (+0xa0) and into the wind band bottom (+0xa4).
  - Field 36 is the band top, 37 the wind mode and 38 the wind scale. There's no gravity field.
- Flags: 0x200 ready once the pool is empty, 0x800 alpha blend; bits 0-2 are the locator's.
- Init:
  - Pool max(_ftol(f10 · 1.5 · f35), f31).
  - Burst f31 sprites at once from the locator along its axes (Sparks' spawn). The Process counter
    starts at 0.
- Process:
  - Spawns go while (rand() & f24) == 0 and (duration < 0 or age < duration − f35).
  - n = _ftol(f10 · age) less those already spawned, spread along the emitter's move since the last call:
    sprite k starts at prev + unit(d) · k|d|/n. Its direction is Sparks' (a, b, speed from fields 25-30),
    turned by the locator's world matrix (`100dcd23`).
  - Then ProcessSprites(dt, wind), and prev = the locator's position.
  - A lost locator sets duration = age + f35 on every call.
- Slots: 6 duration = age + f35; 8 duration; 11/12 colours; 13 start = colour, stop = colour at alpha 0.
- 14 records. They reach nanos only as Tracer3's children. 8010 is also field 31 of all 180 Spell1
  records, but only 9000 and 9002 have field 33 = 0 (so spawn it), and no nano uses those.
- Port: `GfxControlNano0` replaces the previous developer's `GfxControlNano` for 0x3ef (Nano1, 0x3f0,
  keeps it). Seen live as 28609's trail (§5.28).

### 5.30 ShockWave (0xbb8): `ShockWaveSim`, `GfxControlShockWave`
vftable `1016da14`; dynel ctor `100eeec0`, loader `100ee3cc`, init `100eeacb`, Process `100ee525`.
Visuals from DisplaySystem: `GfxVisualGroundRing` (ctor `100172bd`, Update `10016e84`, draw `10017223`)
per ring and `GfxVisualCone` (ctor `1000c196`, build `1000bd0a`, draw `1000c0ec`) per cone.
- Fields:
  - 0 flags, 8 ring life (the base duration), 9 ring material, 10 segments N, 11 ring count.
  - 12/16 inner radius start/end, 13/17 outer radius start/end.
  - 14/18 inner colour start/end, 15/19 outer colour start/end (D3DCOLORs).
  - 20/21 the ring's U/V scale, 22 height above the ground, 23 period between rings.
  - 24 cone count, 25 cone material, 26 cone segments, 27/28 cone U/V scale, 29 cone height.
  - 30/31 cone radius bottom/top, 32 per-cone step, 33 per-cone taper, 34/35 cone colour bottom/top.
- Flags:
  - bit 0: the centre follows the dynel (attach 0, `Vehicle_t::GetGlobalPos`).
  - 0x800: the rings blend SrcAlpha/One. Without it stock blends Zero/SrcColor, a darkening multiply.
    Not ported: every record sets 0x800 (§9).
  - 0x1000: the ring's u runs round it (f20 / N a step) and v is 0 inside, f21 outside. Without it the
    UVs are planar: (x + C.x)·f20, (z + C.z)·f21.
  - 0x2000: radii clamp at 0. 0x4000: the cone's u and v swap. 0x8000 is read but not traced (possibly
    draw order). Not ported: no record sets it (§9).
- Rings. Ring i runs over t = (age − period·i) / life and is deleted past t = 1.
  - Radii go start → end. Colours go through randy31's byte-wise `Color_t`: each channel is
    `_ftol(c·s + 0.5)`, clamped, then added.
  - The N + 1 inner/outer pairs sit on the circle, at the ground's height (`100d33f3`) + f22.
  - The centre is taken the first time the ring runs, or, with cones, during the first 0.3 of its period.
  - A ring updated in a call clears the ready flag, so the control lives as long as its rings, whatever
    the base expiry says. Slot 6 only raises the flag; slot 8 sets the rings' life.
- Cones. Cone j has bottom radius f30 + j·f32, top f31·f33^j + j·f32, and height f29. With p = age / period
  and frac = p mod 1:

  | frac | bottom alpha × | top alpha × |
  |---|---|---|
  | under 0.1 | 10·frac | 10·frac |
  | 0.1 to 0.2 | 1 | 2 − 10·frac |
  | from 0.2 | 1 − (frac − 0.2) / 0.8 | 0 |

  - The alpha is `(int)(a·e)`, so 0x80 at e = 0.5 is 63 (float rounding), in stock too.
  - The cones move to the centre while frac < 0.1 and go once p reaches the ring count.
- Both visuals draw a strip of 2N + 2 vertices: texture × vertex colour, no Z write, no culling, the
  texture wrapping.
- 11 records: 9 with flags 0x3801, 2 with 0x2801 (planar UVs). Ring counts 1-9; cones 0-9. All set 0x800;
  none sets 0x4000 or 0x8000. They reach 43 nanos, the only gap for 37 of them, e.g. 152838 Magnified Psychic Hammer (43103).
- Port:
  - When there's no ground under a ring (`EffectGround.HeightAt` is NaN), the ring sits at the centre's
    height + f22. GfxTest has no floor collider, so it shows that case.
  - Rings and cones go to `EffectBillboardBatch` as vertex-colour strips (inner/outer, bottom/top).
- Seen live on 152838 (43103, flags 0x3801, 2 rings, 2 cones):
  - Ring 0 spreads to 4.99 m and ring 1 starts at 0.5 s. Both are gone at 2.0 s, and the control with them.
  - The cones flash to alpha 0x80 and fade twice, then go at 1.0 s.
  - The inner radius stays 0 for the first third (0x2000), so each ring starts as a disc.

### 5.31 VulcanRocks (0x405): `VulcanRocksSim`, `GfxControlVulcanRocks`
vftable `1016e274`; loader `10103448`, init `10103595`, Process `10103bc1`. The dynel ctor (`10103a18`,
from `100d090c`) builds its locator with fields 1-6 and attach field 7 (`10106be2`) itself. The visual is
DisplaySystem's `GfxVisualRockList` (ctor `1001c5d0`, GetNew `1001c4f9`, ProcessRocks `1001c435`).
- Fields:
  - 0 flags (0x400: keep going when the locator is lost), 8 duration, 10 speed, 11/12 elevation
    low/high (radians).
  - 13-16 colour A,R,G,B, packed like Tracer3's and set by slot 13, but never drawn.
  - 20 the list's size (at most 128), 21 throws per second, 22 model count n, 23.. the n model slots.
  - The field after the models: above 0, settled rocks don't count and the list deletes its rocks.
  - Fields 9 and 17-19 are loaded and unused.
- Throws. While the throw counter ≤ field 21 · age, the counter goes up by 1 and a slot is picked:
  `_ftol(r·(n − 0.0001))`. The list gives a rock only if it has room and the slot holds a mesh. The
  global `GfxVisualRockHandler` also caps rocks at 256 in all. A failed throw makes no further draws.
- A new rock:
  - It starts at the locator. Its speed is field 10, along X·cos t·cos p + Y·sin p + Z·sin t·cos p of
    the locator's axes, with t = r·2π and p between fields 11 and 12.
  - It spins about a random unit axis (y when all three draws are 0), from angle r·2π − π, at
    (r·2π − π)·8 rad/s.
- Each call, per rock:
  - v.y −= 9.8·dt, then p += v·dt.
  - Under the ground (`100ada15`, height and normal), it's put back on the ground, v is mirrored in the
    normal and halved.
  - If it has bounced 3 times or fewer and its speed is still ≥ 0.1, it takes a new random spin and
    counts the bounce. Otherwise it stops: v = 0, spin 0, and the settle counter goes up (without the
    last field).
  - A stopped rock sinks under gravity again on the next call, so it adds one settle for every call it
    rests.
  - Then angle += spin·dt.
- Ready once settles reach `_ftol(field 20)`. It is also ready once 50 newer VulcanRocks exist (a global
  instance counter), or when the locator is lost without 0x400. Slot 6 makes it ready at once; slot 8 is
  empty.
- Drawing: each rock is a `VisualTinyRock_t`, the first mesh of its slot in `VisualEnvFX_t`'s model table.
  It sits at the rock's position, turned by (axis, angle), in the model's own material, at render
  priority 3. The rocks go when the control does.
- The model table (the loader is `100616e4`, reached only through `VisualEnvFX_t::ActivateFX`):
  - Slot 4 is rock01.abiff, loaded once any environment effect with FXID 4 (`e_GenericMeshObject` in the
    twk FXS files) has run.
  - Slots 5-10 and 42-43 are textures, so they never give a rock.
  - Slots 40, 41 and 46 are gib06_slime, gib07_slime and shell_casing. No call site found and no FXS id activates them.
  - Slots 44 and 45 are empty.
  - Slots 11-17 are rock01-rock07; Gamecode starts one of them at random (`100b00d7`).
- 11 records; 7 nanos reach them:
  - 45058-45060 (slots 4-10) are used by 33 nanos, e.g. 157988 Fiery Breath, 158716 Annihilating
    Breath, 226116 Seismic Smash, 275380 Magma Burn.
  - 61031/61033/61034 (slots 40-46) are used by the 4 Destruction nanos, which get no rock in stock.
- Port:
  - Slot 4 is taken as loaded, by decision (user, 2026-09-22; §9). So about one throw in 7 gives a rock
    from the 45058-45060 records.
  - The body replays at 30 Hz (§3.7 group B).
  - The ground is `EffectGround.TryGround` (a raycast, with the hit normal). With nothing under a rock,
    it falls on without bouncing.
  - Losing the locator ends it even with 0x400 (the base `GfxControl.Process`). Only the Destruction
    records set 0x400, and they draw nothing.
- Seen live on 157988 (45060, with a temporary floor collider):
  - One throw in about 6 gave a rock (11 of 69 by 0.28 s); the list was full (23) at 0.6 s.
  - The rocks rose ~16 m, landed at ~3.6 s and bounced up to 4 times, coming to rest up to ~20 m out.
  - The control ended at ~6.9 s. rock01 is mesh 36067: a 0.2 m brown rock, material gib1.

### 5.32 VolGrid (0xbde): `VolGridSim`, `GfxControlVolGrid`
vftable `1016fbac`; loader `10115f8c`, init `10115eb5`, Process `10116292`. The dynel ctor (`10116562`)
builds its locator through `100d2f58`, so it takes fields 1-7 as its template. The visual is DisplaySystem's
`GfxVisualVolGrid` (ctor `1002f56d`, render states `1002f410`, build `1002f86d`, edge fade `1002f69e`,
draw `100302ad`).
- Fields:
  - 0 flags, 8 duration, 9 material, 13 spin (rad/s about y), 14/15/16 the slice counts across x, y, z.
  - From field 17, five keyed curves over t (`101166f2`; the port's `StockFloatCurve` / `StockColorCurve`):
    height, bottom size, top size, bottom colour, top colour.
  - Fields 10-12 are loaded and unused.
- Flags:
  - 2: the locator's local frame. 0x800: the position follows the locator every call (else it's taken
    once, at init). 0x2000: on the ground.
  - 0x1000: the spin angle starts at r·360, used as radians. 0x400: each vertical slice maps the whole
    texture (else its v is the slice's place, one texture row a slice).
  - 0x4000: each slice's alpha × |n·d|. d is the unit camera-to-grid direction in the grid's frame. n is
    the cross of the slice's two unit edges and isn't normalised, so a tall thin slice never gets back
    to full alpha.
- Process: ready once the duration ≤ age. Otherwise, at t = fmod(age, duration) / duration, the curves
  set the visual and mark it dirty. The turn is the locator's local-mode turn every call, orthonormalised
  (`10116182`), after the spin about y (`100520d9`). The spin angle += field 13·dt. Slot 6 is an empty
  `ret`, so it runs to its end; slot 8 sets the duration.
- The visual: one fan of 5 vertices per slice (centre first, indices 0 1 2 3 4 1), rebuilt when dirty.
  W is the bottom size, D the top size, H the height; x slices, then z, then y:
  - x slice i of a, at x = (i/a)·W − W/2. The centre (x, H/2, 0) is in the half-way colour. (x, 0, ±W/2)
    take the bottom colour, (x, H, ∓D/2) the top colour. So a slice is a trapezoid and the last one
    stops short of +W/2.
  - z slice i of c: the same about z, at z = (i/c)·W − W/2.
  - y slice j of b, at y = (j/b)·H: the square ±W/2, all in the colour j/b of the way up (randy31
    `Color_t::Interpolate`), always the whole texture.
  - Drawing: unlit, texture × vertex colour, SrcAlpha/One, no Z write, no culling, no fog, render
    priority 6. The texture scale and offset keep their defaults (1, 1, 0, 0).
- 13 records, reached by 32 nanos. 29 of them use 72360-72363, the light pillar of the Blessing, Path to
  Elevation and similar nanos (cast, tracer, hit and buff all the same record). Those have flags 0x203,
  20 × 1 × 20 slices and life 3 s. 72233 (0x4003, edge fade) is on 278905 Add Spawn and 302810 Shattered
  Mirror of the Xan; 72230 (0x5003, edge fade and a random start angle) on 287985 Standing in the Kyr'Ozch
  Gene Pool. The height
  runs 0 → 80 at t = 0.1 → 20; sizes 3.6. The bottom colour runs up to full alpha at t = 0.1 and out by
  t = 1; the top colour is always alpha 0.
- Port:
  - The body runs every frame (§3.7 group A). The grid is one dynamic mesh on the vertex-colour shader.
  - The spin is applied about world y before the locator's turn. Stock's quaternion order isn't checked;
    no record spins (field 13 is 0 everywhere); 72230's random start angle is a turn about y.
- Seen live on 269534 (72362):
  - A green pillar on the target: 80 m tall at 0.3 s, down to 21 m and faded out at 3 s.
  - The cast pillar stood on the caster's hand (1.36 m up), as its locator gives. It and one hit pillar
    ran about 2 s, the length SetDuration gave them (slot 8).

### 5.33 Sprite (0x3f4): `SpriteSim`, `GfxControlSprite`
vftable `1016dd54`; loader `100f6369`, init `100f6483` / `100f64a7`, Process `100f6d2b`. The dynel ctor
builds its locator with fields 1-6 and attach field 7 (`10106be2`, at `100f6c93`). The visual is
DisplaySystem's `GfxVisualSprite2` (ctor `10023c6a`, draw `10023ea4`) or `GfxVisualSprite3` (ctor
`100283ec`, draw `10028606`).
- Fields: 0 the locator's flags, 8 duration, 9 material, 10 sprite flags, 11/12 width from/to, 13/14
  height from/to, 15-18 start colour A,R,G,B, 19-22 stop colour, 23 period (0 → 1), 24 repeats (negative:
  forever), 25/26 pulse amplitude and rate (Hz). The previous port read the material from field 0; it's
  field 9 (field 0 goes to +0x38).
- Sprite flags:
  - Bits 0-1 pick the visual: 0 Sprite2, 3 Sprite3. Anything else makes nothing and the control is ready
    at once.
  - 4: alpha blend instead of additive. 0x1000: no blending.
  - 8: Sprite3 takes the locator's turn. 0x2000: Sprite3 turns about y to face the camera (`100f6e98`):
    x = normalise(−dz, 0, dx), d the camera-to-sprite direction ((1, 0, 0) when dx² + dz² ≤ 0.001).
  - 0x10: follow the locator every call; a lost locator ends it.
  - 0x20: the frame runs first → last over each period. 0x40: repeat field 24 times.
  - 0x80: the colour goes start → stop. 0x100: the size goes from → to. 0x800: a pulse is added to both
    sizes.
- Init:
  - The visual starts at width f11 and height f13, the start colour, and the material's first frame
    (`100cdfa1`; `100cdfb7` is the last).
  - Its place is the locator's local-mode position; a Sprite3 with flag 8 also takes its turn.
- Process, with p = age / period, n = `_ftol(p)` and f = p − n:
  - Ready once n > field 24 (with 0x40 and a non-negative field 24), or n > 0 without 0x40.
  - Otherwise: frame `_ftol((last − first)·f + first)`; colour per channel
    fistp(((stop − start)·f + start)·255 − 0.49999); size (to − from)·f + from, plus
    sin(field 26·age·2π)·field 25 with 0x800.
  - With 0x10 the locator's +0x108 switches rendering on and off each call. The port doesn't read it and
    always draws.
- Slots: 6 is ready at once; 8 sets the duration; 11/12 set the start/stop colour (11 shows it at once);
  13 sets start = the colour and stop = the colour at alpha 0.
- The visuals:
  - Both draw one quad of width × height, 4 vertices as a strip, texture × the vertex colour (+0x18c).
  - The atlas cell is column frame % columns, row frame / rows (stock's quirk, as in §3.4).
  - Sprite2 faces the camera (the view's inverse rotation on ±w/2, ±h/2). It blends SrcAlpha/One, or
    SrcAlpha/InvSrcAlpha with flag 4, or One/Zero with 0x1000. Render priority 6 (3 when unblended).
  - Sprite3 lies in its own frame's xy plane. Its alpha is the texture's only (ALPHAOP SELECTARG1
    TEXTURE). It blends SrcAlpha/One. With flag 4 it's Zero/SrcColor, and with 0x1000
    DestColor/SrcColor; neither multiply is ported, and only record 7100 (no nano) uses one.
- Records and nanos:
  - 16 records; 27 nanos reach them, all through the buff slot:
    - 43607 (Sprite2, attach 3000): 19 Sanctifier/Reaper nanos. On a character 3000 fails, so the sprite
      sits on the mesh frame, between the feet (§3.2).
    - 80006-80009 (Sprite2, head attractors): the Spawn Entrance and Halo nanos.
    - 61085-61087 (Sprite3, attach 2023 Attractor30_beam, 1.5 × 70 m, offset 29.7 m up): the Clan,
      Omni and Neutral Beam nanos.
  - Stock looks an attractor up by name and falls back to `Attractor01_head` when the model lacks it
    (`10105f93`, `1010601b`). The port does the same.
  - The Solitus female has no Attractor30_beam, so the beam hangs off the head attractor. That frame is
    tilted about 16° in the idle pose, so the 29.7 m offset lands ~8 m to the side. Stock does the same
    on that model.
- Port:
  - The body runs every frame (§3.7 group A). Quads go through `EffectBillboardBatch` with explicit axes.
  - A Sprite3 draws with vertex alpha 1, so the texture alone sets its alpha.
- Seen live:
  - 43607 (on 210484): an orange sprite between the target's feet, breathing between 0.88 and 1.13 m.
    It first showed on the right hand, until attach 3000 was fixed (§3.2).
  - 80006 (made directly, since GfxTest skips nanos with only a buff): a red dot at the head that shrinks
    and fades each 0.5 s.
  - 61085 on a world point: a pink vertical beam, 70 m tall, turning to face the camera.

### 5.34 SkyFlash (0xbbe): `SkyFlashSim`, `GfxControlSkyFlash`
vftable `1016da74`; loader `100eefd2`, init `100ef58b`, Process `100ef141`. The dynel ctor (`100ef80f`)
builds its locator through `100d2f58`, so it takes fields 1-7 as its template; the point ctors (`100ef6eb`,
`100ef77d`, `100ef8ef`) don't. The visual is DisplaySystem's `GfxVisualCone`, as ShockWave's cones (§5.30).
- Fields: 0 flags, 9 material, 10 cycles (unsigned), 11 / 12 the lengths of a cycle's two phases,
  13 cone count, 14 segments, 15 / 16 u / v scale, 17 height, 18 / 19 bottom / top radius, 20 / 21 what
  each further cone adds to them, 22 / 23 their growth over a cycle, 24-29 colours c0-c5. Field 8 (the
  duration) is loaded and never read.
- Flags: bits 0-2 the locator's; 0x100 the cone's uv swap; 0x200 additive (every record sets it);
  0x400 the column comes down from the top in phase 1; 0x800 / 0x1000 follow the locator in phase 1 / 2;
  0x2000 turn cone i about y by i · 6.28 / count, once; 0x4000 the base on the ground; 0x8000 the height is
  the locator's height over the ground.
- Init: the cones, each turned with 0x2000; the place from the locator (on the ground with 0x4000);
  one Process.
- Process:
  - Ready once cycles · (f11 + f12) ≤ age. Otherwise c = fmod(age, f11 + f12) and u = c / (f11 + f12):
    bottom radius f22·u + f18, top radius f23·u + f19, height f17.
  - Phase 1 (c < f11), k = c / f11: bottom colour c0 → c2, top c1 → c3 (randy31's byte-wise `Color_t`
    `*` and `+`). With 0x400 the bottom radius is k·bottom + (1 − k)·top, the per-cone step likewise, and
    the height k·f17.
  - Phase 2, k = (c − f11) / f12: bottom c2 → c4, top c3 → c5.
  - In a following phase: a lost locator ends it; the place is the locator's. With 0x4000 the base goes
    to the ground g, and with 0x8000 as well the height is the locator's y − g while that is in 0..f17,
    else 0.
  - Cone i stands at the place + (0, f17 − height, 0) (no lift with 0x8000), its radii the phase's plus
    i steps, rebuilt every call.
  - It ends by clearing the ready flag, so a terminate and the base duration are both ignored.
- Dynel ctor: a negative field 18 is turned positive after the init and, when the dynel is an
  `n3VisualDynel_t` with a CAT mesh, multiplied by `VisualCATMesh_t::GetTorsoSphereRadi` (+0xbc, 1 when
  the mesh has no torso sphere). Only 12510 (field 18 = −2, nano 256069 Xenosquad) has one (§9).
- The visual: a strip of 2N + 2 vertices, bottom ring in the bottom colour and top ring in the top
  colour, texture × vertex colour, SrcAlpha/One, no Z write, no culling, the texture wrapping.
- Records: 43108 (flags 0x3a03; the hit of 168665 / 168666 Gift of Life, 204764, 206241, 305515, and
  every slot of 269729 and 269991), 43107 (0x7e03: grows down, on the ground; 285258, 285299, 291099 and
  every slot of 305432 Purification by Fire) and 12510. 12 nanos in all.
- Port:
  - The body runs every frame (§3.7 group A). `ShockWaveSim.BuildCone` builds the cones.
  - The turn's sign isn't checked. The cones are round, so it only moves where each one's texture starts.
- Seen live:
  - 43108 (on 168665): six columns 60 m tall and 0.45 m round on the target's feet, white fading to clear
    over 4.9 s, gone at 4.9 s.
  - 43107 (made directly): the column comes down from 60 m to the ground in 0.4 s, then its bottom
    widens from 0.4 to 2.1 m as it fades, gone at 5.9 s.

### 5.35 Trail2 (0xbdf): `Trail2Sim`, `GfxControlTrail2`
vftable `1016faf4`; loader `1011518a`, init `10115260`, Process `10115605`; ctors `101153a5`, `10115417`,
`1011548d` (the dynel's, through `100d2f58`, so fields 1-7 are its template) and `1011550e`. The visual is
DisplaySystem's `GfxVisualTrail2` (ctor `1002d040`, render states `1002c63e`, Reset `1002c8c8`, SetOrigin
`1002c8a4`, Sample `1002c7bd`, GetCurrentSample `1002c86e`, build `1002c918`, frame update `1002d169`).
- Fields:
  - 0 flags, 8 duration (the base timer's; every record has -1), 10 material, 11 the ring's size N,
    12 sample interval (0: one a call; every record), 13 the curves' rate, 14 thrust, 15 fluctuation.
  - From 16, three keyed curves (`101166f2`): colour, x size, y size.
- Flags: bits 0-2 the locator's; 0x400 the texture runs along v (else along u); 0x800 a sample taken while
  the dynel moves backwards is black; 0x1000 the samples drift (below). Every record is 0x1c03.
- Init: the visual (material field 10, the flags, N) at the world origin; Reset to the locator's frame
  (the identity if it can't be resolved), colour 0.
- Process:
  - It ends when there's no visual, the locator can't be resolved, or the locator has no dynel
    (`10106184`) or the dynel is gone (+0x80). So it only runs on a dynel.
  - The dynel ctor (kind 2) switches the visual's rendering on and off each call by `10115071`: on while
    the dynel's `VisualMesh_t` (+0xc, +0x94) or CAT mesh data (+0x90, +0xd0) flag is set. Not ported;
    the port always draws.
  - t = fmod(field 13 · age, 1). The locator's frame (`101062d5`) has its x axis × the x curve at t and
    its y axis × the y curve.
  - The colour is the colour curve at t. With 0x800: d = the frame's place − the place of the sample at
    the write index (`GetCurrentSample`, the oldest once the ring is full). Unless d is exactly
    (0, 0, 0), it is set to length 1, and d · (the dynel's `GetGlobalRot` × (0, 0, 1)) < −0.75 makes
    the colour 0.
  - Then `101150ff`: SetOrigin(frame, colour), and one Sample; with field 12 above 0, dt is carried
    and one Sample is taken per field 12 seconds.
  - Slot 6 is the base (ready at once); slot 8 is the empty `10079931`.
- The ring: N samples of a matrix and a colour, a write index and a count.
  - Sample writes at the index, moves it on (wrapping at N) and counts up to N.
  - While the ring isn't full it also rewrites samples 0 .. N − count − 1 with the new sample. Those are
    not the unused slots, so the first full ring holds 9 8 7 6 5 6 7 8 9 10 (N = 10), and the trail
    folds back on itself until N more samples pass. A trail started on a standing dynel doesn't show it.
- The build: count + 1 points. Point i < N − 1 is sample (index + i) % N, the rest the origin, so a
  full ring draws the oldest N − 1 samples and then the origin twice (the newest sample isn't drawn).
  - Each point gives a vertex pair on four strips about its place P: (P + Y, P − Y), (P − X, P + X),
    (P − X + Y, P + X − Y) and (P + X + Y, P − X − Y).
  - u = i / count (v with 0x400); the pair's other coordinate is 0 and 1. The colour is the point's.
  - Drawing: unlit, texture × vertex colour, SrcAlpha/One, no Z write, no culling, the texture clamped
    (D3DTSS_ADDRESS 3).
- The frame update (`1002d169`), with 0x1000: samples 0 .. count − 1 (by slot, not ring order) move by
  the origin's z axis × (field 14 + field 15 · r) · dt. r is DisplaySystem's random in [0, 1)
  (`100844df`). dt is the frame time `VisualEnvFX_t::FrameProcess` stores (`+0x7e8`, +4), which Gamecode
  passes it each frame (`100b0b73`). It is skipped while the visual's +0x19c is set (not traced).
- Records: 72301 / 72302 (attach 2006 / 2007, Attractor07 / 08_special; N 10, material 45),
  72420 / 72422 (N 8, material 46), 72454 (N 5, offset x 2.5). All reach nanos through the buff slot:
  Metas 72303-72310, 52 Phasefront / Yalmaha vehicle nanos.
- Port:
  - Samples at 30 Hz (§3.7 group B); the origin and the drift every frame.
  - The dynel's forward is its transform's rotation × (0, 0, 1).
  - Material textures 45 / 46 are set to clamp; the only other users are two Sprites that draw 0..1 uvs.
- Seen live (Meta 72308 made directly on the GfxTest caster, which has neither attractor 2006 nor 2007, so both
  trails sit on the head attractor, §3.2):
  - 10 samples, sizes cycling 0.20 ↔ 0.22 every 2 s, colours from the curve, the trail following the
    caster as it moved: blue crossed ribbons.
  - The head attractor's z points forward, so the thrust pushes the samples ahead of the caster and the
    0x800 test blacks them while it stands.
  - Not checked with the attractors the records ask for (§9, "Trail2 attractors").

### 5.36 Toggle (0xbdc): `ToggleSim`, `GfxControlToggle`
vftable `1016f984`; loader `1011265a`, Process `10112952`, terminate (slot 6) `101124c3`; ctors set the
locator mode (+0x30): 0 a point (`1011279a`), 1 a matrix, 2 the dynel's (`101128c7`: its identity and
attach), 3, and 4 (no locator; 0x1000 forces it).
- Fields: 0 flags, 1 duration, 2 the child's id, 3 a mask on the playfield resource (+0x50), 4 a count,
  5.. that many playfield ids.
- Flags: bits 0-2 the locator's; 0x400 stay alive to run the child again after it ends (a terminate clears
  it); 0x800 the ids are playfields it may not run in (else the only ones it may); 0x1000 no locator
  (`100ce912`); 0x2000 on a dynel, start only as the dynel starts moving forward; 0x8000 end the child
  gracefully as it stops.
- The child is made through the handler's factories (`100cea4b`, `100cf4fe`, `100d0656`, `100cfdc6`,
  `100ce912`) without registering it (CreateEffect2 is `100d0656` + `100ce4e5`), so the Toggle alone
  processes it. The port uses `CreateOwnedControl` and forwards the child's draws.
- Process:
  - With a child that isn't terminating (slot 7, +0x28), or while the Toggle is: with 0x8000, a dynel that
    stops terminates the child; the child is processed; when it is ready it is deleted, and the Toggle
    stays with 0x400 (not terminating), else is ready. With no child there, it is ready.
  - Otherwise: the playfield test (`101125ed`: a mask set but not met fails; without 0x800 the playfield
    must be listed, and an empty list fails; with 0x800 it must not be). A failure makes it ready. In
    mode 2 the child is made only on the rising edge with 0x2000, else at once; no dynel ends it.
  - So a child ended by 0x8000 isn't processed again, and the next start replaces it (stock never deletes
    it). Every record's child is a Meta, whose terminate hands its registered children a graceful end, so
    they drain in the handler as usual; only the Meta object stays. Nothing shows.
- The moving flag (+0x90, 0 at first; `10112580`), with 0x2000: the dynel's mover, `n3Dynel_t` +0x50
  (Vehicle.dll `Vehicle_t`, every moving dynel's): direction +0x90 (+1 at first, set to ±1 by
  `SetDirection` from Gamecode's movement code, `1006df54` and others), speed +0xcc. With a direction above 0
  the flag is speed > 0; below 0 it holds.
- Records: 72412 (0x2c03, child Meta 72411: BParticle2 72409 / 72410) and 72416 (0xac03, child 72415:
  72413 / 72414) in the vehicle buff Metas 72303 and 72308, 19 nanos. 72106 (playfields 385 and 19 only),
  3421 and 73100 (0x1803: not in 153 or 4106, no locator) reach no nano.
- Port: the mover is the character's `CharacterMotor`: speed its current speed; direction −1 while the
  Backward flag is set, +1 once Forward is (§9). A visual-only dynel (GfxTest's) stands. The playfield id is
  `GfxControlToggle.PlayfieldId`, which nothing sets yet; the resource flags aren't read (no record sets
  field 3). With 0x1000 the child is made on the Toggle's own locator (Approx; no nano).
- Seen live:
  - Meta 72308 on the GfxTest caster: both Toggles stay with no child, as they should on a standing
    vehicle.
  - 72106 with the playfield set to 19: its child BParticle2 71119 was made at once, run by the Toggle
    (age advancing) and drawn through it.
  - The start and stop on a moving dynel weren't seen live: GfxTest's dynels have no mover. The unit tests
    cover the edges.


### 5.37 TParticle2 (0xbd7): `TParticle2Sim`, `GfxControlTParticle2`
vftable `1016fa34`; loader `10113750`, init `10114310` (called from the constructor, `1011484b`),
Process `10113b55`, cleanup `1011364c`, start / stop `101136bc` / `101136fb`; slot 6 is the base
`100a76f0` (a terminate is ready at once) and slot 8 the plain `1010340b`.

It is **BParticle2's streak twin**: the same spawn, life, bounce and frame machinery (§5.16), drawn as
TParticle's ribbon (§5.17) instead of a camera-facing quad. N particles, each its own DisplaySystem
`GfxVisualTParticle2` (ctor `1002a9e7`, geometry `1002ac11`), and each visual keeps an identity
transform, so the particle's **world** position goes straight into its four vertices.
- Fields, read in order from 9 by the loader: 0 flags, 8 duration, 9 mode (0 one burst, 1 emitter),
  10 material, 11 count N, 12 emit interval, 13 spawn cube half-size, 14 trail, 15 spawns per interval,
  16 gravity (y), 17/18, 19/20, 21/22 velocity x, y, z ranges, 23/24 spin range (degrees/s, stored as
  radians by `10113865`), 25/26 life range, 27/28 size range, 29 bounce, 30 growth **per call**,
  31 frame speed, 32 frame mode, 33 the far half-width's divisor, then two `StockColorCurve`s (`+0xc8`
  the near end's, `+0xd0` the far end's).
- Flags: bits 0-2 the locator's; 0x100 transposes the ribbon's u and v; 0x200 additive; 0x400 the
  emitter sits on the ground (`10113bb2`); 0x4000 no random start roll; 0x8000 bounce off the ground;
  0x10000 the emitter's spawn drops to the ground (`1011423c`, and only the emitter's — the init has
  no such step); 0x20000 random start frame; 0x40000 fade the ribbon edge-on; 0x80000 flip its v.
- The element is 0x4c bytes: +0 position, +0xc velocity, +0x18 acceleration, +0x24 roll, +0x28 spin,
  +0x2c spin accel, +0x30 size, +0x3c life, +0x40 total life, +0x44 frame, +0x48 the visual.
- Spawn, the same order of draws in the init (`101144b2`) and the emitter (`10114025`): size, life
  (= total), frame, position (emitter + (2r - 1) * field 13 per axis), velocity, acceleration
  (0, field 16, 0), roll (r * 2pi, or 0 with 0x4000), spin; then velocity and acceleration go through
  the locator's turn (`1010aef4`), whose rows `10113a45` has set to length 1. Without 0x20000 the frame
  is a plain 0 and takes **no draw**.
- Per call: life -= dt (life &lt;= 0 hides the visual, and the next call reads that as dead —
  `10113c1f` tests `GfxVisual +0x178`, which `DisableRendering(1)` sets and `EnableRendering` clears);
  0x8000 with ground &gt;= y scales v by field 29, negates v.y and redraws the spin; p += v dt then
  v += a dt; roll += spin dt; spin += spin accel dt; **size *= field 30** — a bare multiply with no dt,
  so the growth ran with the frame rate (§3.7); both curves at `1 - life / total`; then the frame.
- The roll is advanced and never reaches the visual, exactly as in TParticle (§5.17).
- Mode 0 is ready once nothing is alive (`10113f64`). Mode 1 refills dead slots, at most field 15 per
  call, once field 12 has gathered, and only while `age < duration - field 26` — so it stops a full
  life before the end and the base timer's duration takes it. Mode 1's init spawns everyone and then
  zeroes every life (`10114776`); mode 0 leaves them alive.
- Frame (`10113e64`, only when field 32 is non-zero, and every shipped record is 0): frame +=
  field 31 * dt, then past the material's last frame mode 1 loops to the first, mode 2 parks it at the
  sentinel **-999** (`1016fa88`) which also stops it advancing, and mode 3 turns the rate around — on
  the control, so for every particle — at both ends. The material gives the cell grid and the frame
  range (`100cdf73` / `100cdf8a` / `100cdfa1` / `100cdfb7`).
- Geometry (`1002ac11`), per particle, the same frame as TParticle: the streak runs from the particle
  to `position + velocity * field 14`. d = the velocity set to length 1 — **a still particle is
  skipped**, the test is on the raw velocity; side1 = |d x (0, 1, 0)| (else (1, 0, 0)), side2 =
  |side1 x d| (else (0, 1, 0)). Two quads, on side2 then side1: the near pair at half-width = size in
  curve A's colour, the far pair at size / field 33 in curve B's. With field 33 below 1 the far end is
  the **wider** one — 73001's 0.5 doubles it.
- UVs come from the material's cell: u from its column, v from its row, running from the far pair to
  the near. 0x100 transposes them, then 0x80000 has the near and far pairs trade their v.
- Flag 0x40000 (`1002aaa3`) scales every corner's alpha by |quad normal . view|.
- Records: 15, all mode 1, frame mode 0, and all on 1x1 materials (45, 74, 91), so no shipped record
  animates its cell. 73001 / 71558 / 72365 / 97120-97132 / 97202 / 97204 / 71058 / 71600 / 97310;
  11 nanos, e.g. 284265 Harvested Mind, 288049 Warped Mind AoE, 301734 Ravaged Mind. The union of
  every record's flags is 0x20603, so 0x100, 0x4000, 0x8000, 0x10000, 0x40000 and 0x80000 are ported
  from the disassembly but never exercised.
- Port: the calls are replayed on the fixed clock (§3.7) because the growth and the respawn cap are
  per call, and each particle is drawn between its last two steps. All the particles share one strip,
  drawn with the atlas and the cell's UVs, which is what stock does.
- Seen live: 73001 on the GfxTest caster. 80 particles with 3 spawned per call and lives of 0.15-0.4 s
  hold ~25 alive at a time; the streaks radiate from the emitter, widen at the far end, take the
  curves' colours and recycle; the control runs its 5 s duration and then goes.


### 5.38 Shield2 (0xbda): `Shield2Sim`, `GfxControlShield2`
vftable `1016f72c`; loader `1011148b`, init `101116a2`, Process `10111110`, slot 6 `10111071`, slot 8
the plain `1010340b`. The visual is DisplaySystem `GfxVisualShield2` (ctor `1001e35d`, vftable
`1008afb4`, draw slot 13 `1001d3ee`, vertex transform `1001d569`, CAT callback `1001dc74`,
`Stop` `1001dffe`).

It is **Shield's layered cousin** (§5.15): the host's own skinned mesh pushed out along its normals and
drawn N times, each copy a little larger.
- Fields, in loader order: 0 flags, 8 duration, 9 the displacement mode, 10 the UV mode, 11 the colour
  mode, 12 material, 13/14 the u/v scale, 15/16 the u/v scroll per unit of t, 17 (loaded, handed to the
  visual, never read), 18 the flicker rate, 19 the layer count, 20 an EP03 mech model, then a
  `StockColorCurve`, a `StockFloatCurve`, and a trailing effect id. `+0x58` is a hard **2.0**, not a
  field.
- Flags: 0x400 additive (the visual's base ctor), 0x800 the global flicker, 0x1000 drop each layer,
  0x2000 (the visual's +0x1cc, unread), 0x4000 / 0x8000 the cull mode, 0x10000 the host's own material,
  0x20000 skip the draw once stopped, 0x40000 skip a mesh over 1000 vertices.
- The control resolves its dynel from the handle at +0x80 (`n3Dynel_t::GetDynel`), casts it to
  `n3VisualDynel_t` and follows **the CAT mesh's node** (+0x90): position `100d46f2`, rotation
  `100d4721`, scale `100edc21`. It hides the shield while that node's +0xc4 is under 0.95, and hides it
  entirely unless the host has a mesh (`HasMesh`/`GetMesh` +0xc +0x94) or a CAT mesh
  (`HasCatMesh`/`GetCatMesh` +0x90 +0xd0). Shield2 takes **no locator template** (§9).
- Per call: t = age / duration into the visual's +0x200, the colour curve at t through vfunc 0x54, the
  float curve at t into +0x1c8 (the alpha the geometry uses), the stop ramp into +0x20c and the age into
  +0x210. **+0x20c and +0x210 are never read by the visual**, so slot 6 is a 2 s delay, not a fade (§9).
  The control calls slot 6 on itself once `age > duration - 2`.
- Geometry (`1001d569`), per source vertex, with a = the float curve's value:
  - mode 0: `p' = (p + n * a) * bodyScale`.
  - mode 1: `p' = (p + n * sin(2x + 3y + z + 30t)^2 * a) * bodyScale`. No record uses it.
  - The UV: mode 0 is the source vertex's own, `(uv + speed * t) * scale`; mode 1 is cylindrical,
    `u = atan2(x, z) * field13 / 2pi + field15 * t`, `v = y * field14 + field16 * t`. Modes 2-5 read the
    normal (2 and 3 through the view matrix) and no record uses them.
  - The colour scales the curve colour's alpha: mode 0 one flicker for the whole mesh, written as
    D3DRS_TEXTUREFACTOR; modes 1 and 2 per vertex from three sines over the source position and
    `T = t * field18`, mode 2 also times `t * 1.5`. Above 2 the alpha is left alone.
  - The shield vertex is 36 bytes (position, normal, colour, uv) and the source 32; **the normal is never
    written**, so the shell is unlit.
- The draw (`1001d3ee`) repeats the whole mesh for i in 0..N-1 at scale `1 + (i / N) * a`, dropped by
  `(i / N) * a` with 0x1000. The render states (`1001d17b`) turn z-write off and pick the cull mode from
  the flags: 0x8000 cull back faces (and render priority 3), 0x4000 cull front faces, else double-sided.
- Stock streams the host's skinned vertices in through the CAT mesh's vertex callback, registered in
  slot 8. Unity skins on the GPU, so the port bakes each skinned submesh every frame exactly as
  `GfxControlShield` does and adds one draw per layer.
- Field 20 in 0..9 puts the shell on an EP03 mech `.abiff` (`10111888`: preorder / scout / heavy mech,
  anti-personnel and anti-vehicle guns, and their upgraded forms) instead of the host's body — records
  71320-71339. That path is not ported and reaches no nano (§9).
- Records: 47. The nanos reach 72274-76 through Sequencer 73007 -> Scatter 72278 -> Sequencer 72277;
  25 nanos, e.g. 216976-216979 Blood of the First..Fourth Brood.
- Seen live: 73007 on the GfxTest caster. The Scatter brings up three Shield2 shells at a time, each one
  layer, double-sided, additive; the colour curve ramps from clear to `ffff3030` exactly on its keys
  (`CCCC2626` at t = 0.32, which is 0.8 of the way to the 0.4 key), and each shell draws 5 submeshes of
  138 vertices. With field 13's 0.01 the shell hugs the skin, and colour mode 2's three-sine alpha peaks
  near 100/255, so it reads as a faint red shimmer over the hips and legs rather than a bright shell.


### 5.39 Scatter (0xbd5): `ScatterSim`, `GfxControlScatter`
vftable `1016f6cc`; loader `10110cc2`, slot build `10110db5`, Process `101108a8`, slot 6 `100d2b34`
(the base flag only). Rebuilt 2026-09-22 from a stand-in that only modelled the timing.

N timed copies of one child, each placed by its own scatter offset.
- Fields: 0 flags, 1/2/3 an offset added to every copy, 4 the **position** mode, 5 the **time** mode,
  6 the slot count, 7 the duration (the base timer's +0x10), 8 the scatter distance, 9 the second
  scatter scalar, 10 the child's id. Every record has exactly 11 fields; the line cursor (+0xa8) and the
  cycle base (+0x44) are zeroed by the constructor, not loaded.
- Flags: 0x400 run the whole schedule again when it empties, 0x1000 drop each copy to the ground,
  0x2000 make the copy on the Scatter's own locator instead of the point it computed, 0x4000 end at once
  when terminated.
- The slots are stamped once (<c>10110e03</c>), 12 bytes each (time, a fired byte, the child): time
  mode 0 gives each `r * duration`, mode 1 gives it `duration * i / count`, any other mode leaves 0.
- A slot fires when its time passes `age - cycleBase`. Its offset (`101109d2`):
  - mode 0: `(2r - 1, 2r - 1, 2r - 1) * field8` — a point in a cube.
  - mode 1: `(0, 0, cursor) + (2r - 1, 2r - 1, 2r - 1) * field9`, then `cursor += field8 / count` — a
    line of copies, each jittered.
  - anything else: no offset. The draws happen before the 0x2000 test, so the cursor walks even when the
    offset is thrown away.
- Without 0x2000 the copy is made at a **world point** (`100cea4b`): the locator's base, plus that
  offset, dropped to the ground with 0x1000, plus fields 1-3 — and it does not follow the locator
  afterwards. With 0x2000 it is made on the locator (`100cf4fe` / `100cfdc6`) and the offset is unused.
  18 of the 27 records take the world-point path.
- Stock picks the base by locator mode: a stored point, a matrix it snapshotted once from the host's
  mesh frame (`10110963`), the dynel's position, or the locator object's matrix. The port takes the
  locator's resolved position for all four (§9).
- The children are made **without being registered**, so the Scatter processes and draws them itself,
  exactly as Toggle does (§5.36). The port uses `CreateOwnedControl` and forwards their draws.
- When every slot has fired and every child has gone: 0x400 clears the fired bytes and sets the cycle
  base to the age (`10110c96`), so it runs for ever; otherwise the control is ready.
- Field 7 is both the spread and the base timer's duration, so a slot whose random time lands near the
  top of the range can be cut off by the watchdog before it ever fires. That is stock.
- Records: 27. Position scatter is really used by 71305 / 71306 / 71310 (a 50-unit line, jitter 6 and 8)
  and 72343 / 72344 (a 3-unit line); 72370 scatters 60 copies over 50 units. 72278 (one copy on the
  locator, repeating) carries the Brood nanos.
- Seen live: 73007 on the GfxTest caster gives Scatter 72278 -> Sequencer 72277 -> three Shield2 shells,
  15 mesh draws in all; 73006 owns a Shield2 directly and forwards its 5 draws; 71229 fires its 30
  ground-snapped world points at exactly 3 a second over 10 s. 71229's child is GroundGrid mode 2, which
  is not ported, so nothing is drawn at those points yet.


### 5.40 Highlight (0x7db): `HighlightSim`, `GfxControlHighlight`
vftable `1016d164` (plus two more for its second base); loader `100e2dc0`, start `100e309b`,
Process `100e2efc`, slot 6 `100e2d91`, slot 8 `100e2da5`. Checked against stock 2026-09-22; the port's
field map and curves were already right, and the terminate was not.

It spawns no visual — it tints the target's existing mesh through randy31:
`RRefFrame_t::SetTransparency` takes the first ramp value, `SetEmissive` the other three, and mode 3
also calls `SetSpecular`.
- Fields: 0 flags (+0x84), 1 the mode (+0x3c), 2 the duration (+0x10), 3-6 the start block and 7-10 the
  end block — eight consecutive floats read by the ramp at +0x50 (`101085de`), each block
  (transparency, r, g, b) — and 11 an int that raises +0x39. Every record leaves field 11 at 0.
- Mode 2's loader (`100e2e1e`) moves the duration into the pulse length (+0x4c) and forces the duration
  to **-1**, so it holds until something ends it.
- The phase (`100e2f38`): mode 0 is `age / duration`, **unclamped**; modes 1 and 3 are
  `1 - (2 * age / duration - 1)^2`, an arc up and back down; mode 2 rises as `min(age / pulse, 1)` and,
  once terminated, falls as `(duration - age) / pulse`; any other mode stays at 0.
- Slot 6 is **not** an ending: for mode 2 it sets `duration = pulse + age` and then every mode just
  raises +0x38. The control still runs until its duration expires. The port used to go ready at once
  for modes 0, 1 and 3 — fixed.
- Records: 11. 11500/11501/11503/11504 and 61110-61112 are mode 0, 11502 mode 1, 11506 mode 2,
  11507/11508 mode 3. The nanos reach mode 2 (11506: 269512 Dimensional Shift, 275524 Monster Cloak
  Nano, 293946 Umbral Fastness) and mode 0 (through 61060-61063 and 61090, e.g. 202509 Destruction 1).
  **No nano reaches mode 3.**
- Seen live: 11506 on the GfxTest caster. The phase rises 0 to 1 over exactly its 2 s pulse
  (0.255 / 0.504 / 0.759 / 1.000 at half-second steps), holds at 1, and a terminate at age 4.99 sets the
  duration to 6.99 — `pulse + age` to the digit — after which it fades 0.961, 0.710, 0.456, 0.203 and
  the control goes. Every nano-reachable record (11506, 61110, 61111, 61112) is **transparency-only**
  with a black or near-black emissive, so the fade is the whole effect; `EffectMeshTint` swaps in
  transparent material clones to render it (§9). 11506 reads back as alpha 1.00 -> 0.20 over its pulse
  and back to 1.00 after the terminate, with the original materials restored.


### 5.41 GroundShake (0xbd8): `GroundShakeSim`, `GfxControlGroundShake`
vftable `1016f604`; loader `1010f015`, Process `1010f12a`, slot 6 the base `100a76f0`. It draws nothing
at all — it only shakes the camera, reached through `n3EngineClient_t::GetInstance()` +0x7c, by writing
an offset into `n3Camera_t` +0x1d4 and calling `n3Camera_t::UpdateTargetEye`.
- Fields: 0 flags, 8 duration, **9/10/11 are scratch** — the loader reads them but Process overwrites
  +0x38..+0x40 with the locator's position every call — 12 the range, 13.. a `StockFloatCurve` over the
  life. Every record leaves 9, 10 and 11 at 0.
- Per call: ready once `duration <= age`; otherwise `t = age / duration`, `d` is the distance from the
  locator to the camera clamped to the range, and the amount is `curve(t) * (range - d) / range` — full
  at the epicentre, nothing at the range. The offset is three fresh draws, `(2r - 1) * amount` per axis.
- It takes the locator template (`1010f2ab`), so it is in `TakesLocatorTemplate`.
- Records: 5 — 71046 (8 s, range 200), 71216 (2 s, 50), 71309 (4 s, 50), 71344 (2 s, 20), 72380
  (2.3 s, 30). 7 nanos.
- Port: the offset is posted to `EffectCameraShake` and consumed inside `EffectHandler.Tick`, which is
  where stock applies it (Process writes and calls `UpdateTargetEye` in the same breath). Putting it in
  the tick rather than in `EffectRuntimeHost` matters: GfxTest drives the handler directly and would
  otherwise never shake, and the accumulator would grow unbounded.
- Seen live: 71046 on the GfxTest caster at 4.9 m. The amount follows its curve — 0.09, 0.19, 0.28,
  then 0.60 at the step the curve takes at t = 0.6, then decaying — the camera's frame-to-frame jump
  tracks it (0.13, 0.27, 0.38, 1.34, 0.80), the pending offset reads zero every frame so nothing
  accumulates, and the control goes at its 8 s duration.

### 5.42 Spiral2 (0xbd9): `Spiral2Sim`, `Spiral2Ribbon`, `GfxControlSpiral2`
vftable `1016f924`; loader `10111ebd`, Process `10111c40`, slot 6 `10111d75`, ctors `101121e9` (shared)
and `10112242` (dynel). Field 10 helical ribbons stand on a common axis at the locator, evenly spaced
round it, all sharing one turn about the world y that accelerates as the effect runs. Its dynel ctor
goes through `InitDynelTemplate` (`1011227e`), so it takes fields 1-6 as its offset and turn.

Fields: 0 flags, 8 duration, 9 material, 10 the ribbon count, 11 segments, 12 the wobble rate, 13 the
ribbon's half width, 14 its height, 15 the start angle, 16 the angle it sweeps, 17 the u scale, 18 the
angular velocity, 19 the angular acceleration, 20 the fade-out width, 21 the fade-in width; then the
colour curve and the radius curve.

- **Field 15 == 999 exactly** (`10112022`) means a random start angle — `random() * 6.28` — so each
  cast winds from somewhere else. Any other value is taken as written. Ribbon *i* then starts at
  `i * 6.28 / count` past it (`101120a9`).
- **Both curves are loaded in int mode** (`push 1` at `10111fb9` and `10111fc8`) yet Process reads the
  second with the *float* evaluator (`1011689d`). That is harmless, not a bug: the two template readers
  (`10106dc8` int, `10106de9` float) return the same dword and the loader writes it to the same 4-byte
  slot either way, so the record's raw bits survive and each evaluator sees what its author wrote.
- `+0x80`, which scales the segment count and the height and sweep handed to each ribbon, is set to 1
  by the shared ctor (`10112220`) and never written again. It is a constant in the port.
- Per call (`10111ca2`): each ribbon is placed at the locator's world-mode position (`1010640a`), then
  `angle += spin · dt` and only afterwards `spin += acceleration · dt`; the turn is a quaternion about
  (0,1,0) (`100520d9`). Every ribbon then takes the phase `age / duration`, that turn, the colour curve
  at the phase (vfunc 0x54) and the radius curve at it.

**Geometry** — DisplaySystem `GfxVisualSpiral2` (ctor `1002270b`, vftable `1008b2dc`, draw slot 13
`10022687`, builder `10022407`), one triangle strip of `2 · segments` vertices through randy31
`render_t::RenderTriangleStrip`, FVF 0x142, stride 0x18. With `f` running `0, 1/N, … (N-1)/N` and
`theta` from the ribbon's start angle stepping by `sweep / segments`: `x = sin(theta) · radius`,
`z = cos(theta) · radius`, `y = height · f ± halfWidth`, `u = f · uScale`, and `v` 0 on the upper
vertex, 1 on the lower. Alpha is the colour's own, scaled by the fade in (`f < fadeIn → f / fadeIn`),
the fade out (`f > 1 - fadeOut → 1 - (f - (1 - fadeOut)) / fadeOut`) and, with **flag 0x400**, a wobble
`(sin(rate · t + theta · 4) + 1) / 2`. Both fades write the same slot and the fade-out is written
second, so where the windows meet the fade-out wins. **Flag 0x200** makes the ribbons additive.

`+0x1e4`, which the draw gates on, is left at 0 by the ctor (`100227a3` stores the `fldz` that has sat
on the x87 stack since `1002272e`) and nothing ever writes it, so the gate — "draw while it is not
negative" — always passes. Not modelled.

**Slot 6 is not an ending.** While more than a second is left it sets `duration = age + 1` and rewrites
both curves to three keys — hold, then go — so the ribbons keep their look until the interruption and
then take exactly one second to leave: the colour to nothing, the radius one unit wider. Past that
point (`age > duration - 1`) it does nothing and lets the timer run out. Both replacement values are
each curve's value at **t = 0**, not at the current phase (`10111da2` / `10111e21` both push zero), so
a record whose colour curve *fades in* from transparent — 71108 starts at `0x004070c0` — snaps out the
instant it is interrupted instead of fading. That is stock.

Timing: the turn is the only per-call integration, and its Euler error scales with the frame time, so
it runs on the fixed stock clock (§3.7 group B) with the drawn angle interpolated between the last two
steps. The phase, colour and radius are read from the age every frame.

8 records: 71066 / 71070 / 71071 (nanos 278873 Orbital Strike Resolve Mission, 278875 Orbital Strike,
via tree 71015 — 71066 is an 800 m column, the strike itself), 71108 (265805 Capturing Control Point,
284263 Harvested Mind, 291322 Ravaged Sanity, 302676 Flesh of the Voidling, via 71002), 72625 (273220
Strength Of The Ancients, and 281089 Summon Towers via 72626); 71125, 97000 and 97001 are reached by no
nano. Verified live on 71108 (a woven lattice round the caster) and 71125, including the slot 6 fade.

### 5.43 Tracer6 (0x402): `Tracer6Sim`, `GfxControlTracer6`
vftable `1016e104` (object 0xd0); loader `10100c93`, init `101014ff`, build `10100dab`, Process
`10101872` → update `1010107d`, slot 6 `10100c8e`, slot 13 `101014c8`. This was the port's last tracer
stand-in. It is reached only through `CreateGfxControlTracer(id, from, to)`, whose dispatch
(`100d18d9`) reads `sub ecx, 0x401; je Tracer5; dec; je Tracer6; dec; je Tracer7; sub ecx, 0x7cf`, so
**0x401, 0x402, 0x403 and 0xbd2 are Tracer5, 6, 7 and 8**.

Fields are Tracer5's shape plus 17-26: 0 flags, 1-7 locator, 8 duration, 9 material, 10 speed, 11 the
spacing between sprites, 12 the sprite's base size, 13-16 colour A, 17 how much a sprite widens with
distance along the line, 18 how fast it widens with time, 19 the link count, 20 the ribbon's speed,
21 its length, 22 its width, 23-26 colour B. Both colours are FISTP-packed once, so 0.25 becomes 63.

The init matches Tracer5's call for call — direction and length, a segment under 0.01 readies the
control, the speed capped at 5 × the length, and the same locator frame — and adds one cap of its own:
**if field 11 × 4 is past the length, field 11 becomes 0.24 × the length**, so a short flight still
gets about four sprites.

Two visuals, both of classes the port already had:
- **three `GfxVisualCord4` ribbons** (`10100de0`) whose material is the **literal 15**, not field 9.
  All three are given the same link positions every call (`101012d4` walks them with one body), so they
  sit on top of each other and, being additive, simply come out three times as bright.
- **one `GfxVisualSprite3Type0`** (`10100f6c`) on field 9's material, its atlas forced to **8 × 8**
  (`10100fae`) whatever the material says. Its quads sit in the visual's own frame rather than facing
  the camera: position ± width/2 along the frame's x and ± height/2 along its z (`10028cd0`), which puts
  them **across** the line, since the locator's y is the direction of flight.

The update runs in two phases, and the record's duration of **-1** is what picks between them — nothing
expires the control until it sets its own duration:
- **The flight, while age > duration.** The trail runs from tail = speed · age (kept at 0 or more) to
  head = tail + field 11 (capped at the length), and a sprite is spawned every field 11 from the cursor
  up to the head, at size `cursor × field 17 + field 12`, life 1, frame 32. The ribbons run from
  `a = field 20 · max(0, age − (length / speed − length / field 20))` to `a + field 21` — tuned so `a`
  reaches the length exactly when the trail's tail does. When the tail lands, **duration = age + 1**.
- **The last second, while age < duration.** Both ribbon ends collapse to 0 and their links are given
  colour 0 (`10101463`), leaving only the sprites to fade out.

Every sprite, each call: width and height grow by field 18 · dt, the position moves by its velocity
(always zero — stock spawns it at rest and never writes one), life falls by dt and is kept at 0 or more,
the alpha becomes `ftol(life · 255)` and the frame `ftol((1 − life) · 30 + 33)`, so it walks cells 33 to
63 as it dies.

**The base timer has to run before the body.** Stock's `100d2a86` sets `+0x14` and `10101883` then skips
the body, so once the duration the control gave itself has passed it never runs again. Ported without
that, the update keeps meeting "age > duration" and buys itself another second forever — caught in the
live probe, where the duration climbed 1.20, 2.21, 3.21, … and the tracer never died.

One record, 70000: speed 30 (capped to 12.9 over GfxTest's 2.58 m), spacing 0.5, colour A blue
(0xff3f3fff) for the sprites and colour B red (0xffff3f3f) for the ribbons. Its only nano is **294977
Michizure's Decuple** (stat 419). Verified live: the red ribbons run caster to target with the sprite
discs across the line, the trail lands at 0.20 s, the ribbons go at once and the control ends at 1.21 s.

### 5.44 Beam (0xbce): `BeamSim`, `BeamBlades`, `GfxControlBeam`

Stock `GfxControlBeam_t` (vftable `1016ef3c`, Process `10109c0c`, slot 6 `101096c2`, build + field
loader `101096ee` / `10109817`) with DisplaySystem's `GfxVisualBeam` (ctor `10007ef4`, render
`100087c1`, geometry `10008056`). **36 records**, reached by 6 nanos through trees 71002 and 71015.

A **star of flat blades** standing on the locator. The angle starts at 45 degrees and steps by
`180 / field 14` while it stays under 225, so the blades share half a turn between them; each is one
four-vertex quad through the axis, running from the foot radius at `y = 0` in colour 1 to the tip
radius at `y = field 17` in colour 2, with u across it and v along it. Field 39, when it is not
negative, adds a flat square of half-size *foot* lying at `field 40 x length`, all four corners in
colour 1, drawn in a second pass with its own material.

**The three-stop ramps.** Fields 18-23 are three (foot, tip) radius pairs and 24-29 six packed ARGB
colours as two ramps, and both are read the same way: the middle stop is the steady value, field 11
is how long it takes to fade in from the first, and field 12 how long it takes to fade out to the
third. Two things to know about them:

- **A field 8 of zero or less is not "forever"** (`101099fa`): the build reads the duration as
  `f11 + f12`, so such a record lasts exactly as long as its two fades.
- **Most records have no steady part at all.** 71064, 71106, 71107, 71500 and 71862 all have
  `f11 + f12 == duration`, so they rise and then immediately fall. Only 71109, 96006 and the weapon
  family hold anything.
- Stock tests the fade-in **first**, which matters after a graceful stop: slot 6 pulls the duration in
  to `age + 3` and makes field 12 three seconds, but leaves field 11 alone, so a record with a long
  fade-in keeps *rising* until it ends and then snaps onto the fade-out ramp part way down. That jump
  is stock's; `BeamSimTests.TerminatingGivesItThreeSecondsOfFadeOut` locks it.

**Repeats.** Field 13 is a repeat count and field 41 the gap between takes. On reaching the duration
stock blanks both colours and holds for the gap, then spends one repeat, resets *its own age* to zero
and starts over; when they run out it ends. The count is decremented in place, so a record's repeats
are consumed rather than remembered. The weapon family (72031-72083) runs up to 24 takes of 0.02 s
with 0.04 s gaps.

**Rate.** That repeat machine, the per-call wobble jitter and the falling anchor all step per call and
all show, so the body is replayed at 30 Hz (group B) and the drawn state is interpolated between the
last two steps. Left uncapped, a 0.02 s take would be one frame in two at 30 Hz and six in eighteen at
300 FPS.

**Flags.** 0x200 additive, 0x400 wobble (a travelling sine `sin(2*pi*f35*t + f37) * f33` plus a fresh
random `* f31` on each radius, fields 37/38 being **degrees**), 0x800 take the anchor from the locator
every call, 0x2000 snap it to the ground under it (only 71064; the port takes a `GroundHeight` hook and
leaves the anchor alone without one), 0x20000 an extra quarter turn about x. In the visual: 0x100 swap
u and v, 0x1000 flip v along the beam, 0x8000 start at a random v, **0x10000 fade a blade out as it
turns edge-on** (`|n . view|` scaling its vertices' alpha).

**Field 30** is the spin about y in radians per second, or exactly **999** for a random angle re-picked
for every take. Two runtime fields are worth naming: `+0xb4` is a fall of **-800** units/s/s that only
bites when field 10 is zero *and* nothing refills the anchor (just 71862), and `+0xd4` is a scale the
sub-ctor sets to 1 that nothing in the vftable or the four ctors ever writes again.

Verified live on 71109 (10 s, 8 blades, length 4, the blue `0xa02050a0` ramp): the alpha climbs to
0x9E by 2.97 s, holds at 0xA0, is back to 0x07 at 9.87 s and the control ends at **10.04 s**; the
blades sit at y 0 to 4 with the second material's square at 0.80, and the turn reads 0.2 x the age
throughout.

### 5.45 CrazyCone (0xbc1): `CrazyConeSim`, `ConeFrustum`, `GfxControlCrazyCone`

Stock `_GfxControlCrazyCone_t` (RTTI `.?AV_GfxControlCrazyCone_t@@`, vftable `1016c5c4`, Process
`100d6dcc`, field loader `100d6a6f`, build `100d73b2`) with DisplaySystem's `GfxVisualCone` (ctor
`1000c196`, render `1000c0ec`, geometry `1000bd0a`). 11 records, reached by 7 nanos.

**The record is not a flat field list.** Fields 0-20 are a header and then there are **field 20 stages
of 29 fields each**, the first at field 21 — which is why the shipped records have 50, 79, 108 or 137
fields (21 + 29n). The loader proves it: its index starts at 21, the array is `stageCount * 0x74`
bytes, and 0x74 is 29 floats. **Field 10 is the number of cones and field 20 the number of stages** —
two different counts that are easy to confuse (60001 has 4 cones and 2 stages).

**A cone is a frustum.** Each is one triangle strip of `2 * field 11 + 2` vertices: a ring of the
bottom radius at y = 0 in colour B, a ring of the top radius at the height in colour A, with i running
to the segment count *inclusive* so the last pair closes onto the first. Either radius may be zero,
which is what makes a real cone. Stock's tables put the **cosine on x and the sine on z**, the
opposite way round to Beam and Spiral2.

The cones are all on one anchor, cone i turned about the world y by `i * 6.28 / count` plus the
stage's spin, and each one a step wider than the last (field 13 on the bottom radius, field 12 on the
top). Flag 0x400 also fans them at build time, but Process overwrites that rotation every call, so
the bit only sets the pose before the first Process.

**The stages are what animates it.** Slot 0 is a stage's duration; slots 3-9 are values and 10-16
their matching rates, Euler-integrated by dt. Those values then integrate into the **header's own
fields**, so the radii, the height and the texture spans carry on from where the last stage left them
rather than resetting:

| stage slot | drives |
|---|---|
| 3, 4 | the u and v texture scroll |
| 5, 6 | field 18 (the top height) and field 19 (the lift) |
| 7, 8 | field 16 (the top radius) and field 17 (the bottom radius) |
| 9 | the spin, read as `stage elapsed x slot 9` |
| 17-20 | two colour ramps: A is Interpolate(17, 19, t), B is Interpolate(18, 20, t) |
| 21-28 | a sub-cycle with its own timer and repeat count |

Slots 1 and 2 the loader never writes — they are the stage's own accumulators. Slots 17-21 and 27-28
are read as ints, everything else as floats.

Process steps the stage machine **inside one call** if dt spans more than one stage, carrying the
leftover, and ends the control on the call *after* the last stage is spent. That, the seven
integrators and the texture scroll are all per-call, so the body is replayed at 30 Hz (group B).

An invalid locator ends most controls; **flag 0x2000 lets a CrazyCone outlive its locator**. Flag
0x800 snaps the anchor to the ground under it, and field 19 lifts the whole nest.

Verified live on two records. 61021 (1 cone, 32 segments, one 4 s stage) is an expanding red ring:
the radii accelerate to 30.5 and 39.7, the alpha runs 0xE1 down to 0x0F and the spin reads
`elapsed x 0.05` throughout. 61081 (4 stages of 0.1 + 0.5 + 5 + 0.6 = 6.2 s) is a 162-unit column: its
bottom radius grows through the first two stages and then holds at exactly 3.33 as the rates say, and
the control ends at 6.29 s.

**When reading colours out of a record, take the raw bits.** Several of these are NaN patterns —
61021's stage colours are 0xFFFF0000, which a float-formatted dump prints as a bare "nan", the same as
0xFFFFFFFF.

#### Delay (0xbc5), checked at the same time
Delay was Ported but had never been checked, and after CrazyCone landed it was the sole gap on two
nanos. Its Process (`100d8863`) is a **countdown**, not a clock: `+0x34` starts at the delay, loses dt
every call, and the child is spawned on the call that takes it below zero — that path never ends the
control, so a record with no child (12251) dies on the call after rather than the same one.

The delay itself (`100d8a3b`) is `min + (rand() & 0x7fff) * (1/16384) * (max - min)`. The divisor is
**16384, not 32768**, so the weight runs 0 to just under 2 and a delay can reach nearly
`min + 2 * (max - min)`. Record 62010 is min 0, max 10 and the live run fired at **10.94 s** — above
its own max, which is only possible with that quirk, and a neat confirmation of it.

The port had added a `max < min` swap that stock does not do; no shipped record needs it, and it has
been removed. Both shipped children are `_GfxControlAudio_t`, which neither stock nor the port draws.

### 5.46 GroundRing (0xbc4): `GroundRingSim`, `GfxControlGroundRing`

Stock `_GfxControlGroundRing_t` (vftable `1016d094`, Process `100e1c7e`, field loader `100e20f6`,
build `100e1f21`) with DisplaySystem's `GfxVisualGroundRing` (ctor `100172bd`, Update `10016e84`).
9 records, all a flat 20 fields, reached by 5 nanos.

A band laid on the ground: `2 * field 10 + 2` points, an (inner, outer) pair per step with i running
to the segment count *inclusive* so the ring closes. Field 13 is the inner radius and 14 the outer,
and **each point's height is the terrain height under it plus field 19**, stored relative to the ring's
centre because the visual sits there. x comes from the cosine and z from the sine, as with the cone.

**Flag 0x800 decides whether it moves.** With the bit, the build bakes the ring to the ground once and
Process never touches the points again (`100e1dee` jumps straight to the visual update) — it stays
where it was spawned. Without it, Process re-reads the locator and re-samples the ground under every
point every call. Eight of the nine records are baked; only 60005 follows. Flag 0x1000 lets it
outlive a dead locator.

**The colours do not cross-fade.** Each of field 17 and 18 keeps its own rgb and only its *alpha* is
ramped — up from nothing over field 15, held exactly as written through the middle, and back down over
field 16. The middle stretch skips the interpolation entirely (`100e1d50`). Note 60005's field 18 is
`0x000000ff`: the outer rim's alpha is already zero, so the band fades away at its outer edge.

**The uv walks the ring, and getting this backwards is easy.** `10016ea0` is
`cmp byte [+0x1b4], 0` followed by `je`, so **the jump is the zero case** — the world-projected branch
at `10016f73` (which multiplies the point's world x and z by fields 11 and 12) is only taken when
flags bit 8 is **clear**, and no shipped record clears it. Every record therefore takes `10016eac`:
u runs 0 to field 11 around the circumference in steps of `field 11 / segments`, and v goes 0 on the
inner rim to field 12 on the outer. The texture tiles **along** the band. Reading that branch the
wrong way round projects the texture flat over the world and gives the ring obvious stripes, which
stock does not have.

**Ground height.** Stock asks n3Playfield (`100d33f3`) per point. The port raycasts straight down,
which is the engine's equivalent; with nothing to hit it falls back to the ring's own height, which is
correct on flat ground. Sloped ground needs terrain colliders, which the project does not have yet.

Verified live on 60005: 34 points for 16 segments, outer radius 8, height field 19 above the centre,
alpha 0 to full over the 0.5 s fade-in, held through the middle, back down over the last second, and
the control ends at its 10 s duration. The render is a smooth radial glow, white at the centre out to
transparent violet at the rim.

### 5.47 Mesh (0xbc2): `MeshSim`, `GfxControlMesh`

Stock `_GfxControlMesh_t` (vftable `1016d31c`, Process `100e52f3`, field loader `100e5502`, build
`100e53eb`). 5 records, all a flat 14 fields, reached by 4 nanos — Destruction 1 to 4.

The simplest control in this family: it stands **one model** on the locator, turns it about the world
y and fades it in and out. Its visual is not a GfxVisual at all but a plain `VisualMesh_t`, driven by
three calls a frame — `SetPosition`, `SetRotation` and `SetTransparency`.

**Field 9 picks the model** from four literals compiled into Gamecode, looked up as RDB type 1010001:

| field 9 | model |
|---|---|
| 0 | `tower_destroyed_buff&debuff.abiff` |
| 1 | `tower_destroyed_buff&debuff_LL.abiff` |
| 2 | `tower_destroyed_controller.abiff` |
| 3 | `tower_destroyed_guard.abiff` |

They are tower wrecks, which is what the Destruction nanos leave behind, and 61041-61044 take them in
order. Anything outside 0-3 falls out of the switch at `100e540c`, so stock gets no visual at all and
the control dies on its first call.

Fields: 8 duration, 10 the fade-in, 11 the fade-out, 12 the height it is lifted, 13 the turn in
radians per second. The loader pre-divides (`100e5567`), keeping `1/field10`, `1/field11` and
`duration - field11`, so Process only multiplies: alpha up over field 10, held at 1, then down over
field 11.

**Flag 0x100** stands the model on the ground under the locator rather than on the locator itself,
and **flag 0x200** lets it outlive a dead locator — note these are *not* the bits GroundRing uses for
the same two ideas (0x800 and 0x1000 there). The lift is applied to whichever height was found, and
stock re-reads the locator every call, so the port derives the height fresh each time rather than
adding the lift onto what the last call left.

Ground height is the same downward raycast as GroundRing, with the same caveat: correct on flat
ground, and sloped ground needs terrain colliders the project does not have yet.

Verified live on 61043: model 2 resolved by name and loaded from the RDB, alpha 0 to 1 over the 0.2 s
fade-in and held (its duration is 300 s), and the tower wreck renders with its own materials.

---

## 6. Port architecture

| Area | Files |
|---|---|
| Handler, creation, nano flow | `EffectHandler.cs` (`CreateControl` switch, `Create*` builders, cast/tracer/hit/buff flow, `IsStockTracer`), `EffectHandle.cs` |
| Base control | `GfxControl.cs`. The first `Process` only arms (**the body is skipped on that call**), age += dt, the locator is resolved into `WorldMatrix`, and after `OnProcess` it readies at `age ≥ duration`. Controls with stock expiry quirks keep their own duration and set the base one to `InfiniteDuration`.<br>A port-only 60 s watchdog readies anything older; buff trees are exempt (`IgnoreWatchdog`, set by `EffectHandler.AddNanoBuff` and passed on by Meta/Sequencer). |
| Locators / hit locations | `EffectLocator.cs` (`OnDynel`, `OnVisual`, `OnHitLocation`, `WorldPoint`, `Beam`, `TryGetHighlightRoot`), `EffectHitLocation.cs`, `EffectAttachIds.cs` |
| Stock sims (Unity-free, unit-tested) | `FlareType0Visual`, `FlareType0Sim`, `Tracer1Sim`, `PlasmaSim`, `Tracer4Sim`, `Tracer5Sim`, `SparksSim`, `FireSim`, `SmokeSim`, `SpiralSim`, `Tracer3Sim`, `Nano0Sim`, `ShockWaveSim`, `VulcanRocksSim`, `VolGridSim`, `SpriteSim`, `SkyFlashSim`, `Trail2Sim`, `StockUvTrack`, `ToggleSim`, `TParticle2Sim`, `Spiral2Sim`, `Tracer6Sim`, `StarsCase12`, `StarsCase14`, `StarsCase28`,
`Shield2Sim`, `Sprite2Type0Visual`, `EffectWindSim`, `Cord4Strip`, `StarsCase2`, `StarsCase3`, `StarsCase9`, `StarsCase13`, `StarsRing`, `StarsLineSparks`, `StarsLimbSparks`, `StarsBodySparks` (behind `IStarsStockCase`), `SunsSim`, `ShieldSim`, `DeformerSim`, `ElectraSim`, `StockColorRamp`, `Spell1Trail`, `ScatterSim`, `HighlightSim`, `GroundShakeSim`, `SpriteEmitterMath`, `TracerMath`, `EffectFrameRate`, `EffectTypeCatalog`, `EffectCoverage`, `AnimNoteIds` |
| Drawing | `EffectBillboardBatch.cs`:<br>• All effect draws go through `Hidden/LostEden/EffectVertexColor` (`Assets/Resources/Effects`): HDRP/Unlit's transparent forward pass with a vertex colour. It premultiplies and de-exposes as HDRP/Unlit does, blends One + One or One + OneMinusSrcAlpha, and linearises the vertex colour in the fragment with Unity's own curve (sRGB below 1, pow 2.2 from 1 up, as `Mathf.GammaToLinearSpace`), after multiplying in the additive boost (`_VertexGammaScale`), so a colour on the vertices comes out as the same colour on an HDRP/Unlit material. SRP Batcher-compatible. `VertexColorPath = false` falls back to HDRP/Unlit.<br>• `Quad`: camera-facing, one texture per frame via `EffectAtlasFrames.GetFrame`. Additive quads are batched into one mesh per texture per frame, colours on the vertices (Stars 2's 128 sparkles: 2 draws); alpha quads stay one draw each, back to front.<br>• `Strip`: a dynamic mesh with one texture and UVs, both windings, with a colour per vertex (`Colors`: Cord, Spiral, TParticle) or one colour (put on the vertices).<br>• `Strip.Quads = true`: every 4 vertices form an independent quad.<br>• `MeshDraw`: a whole mesh in one colour (Shield shells, GroundGrid, EffectMesh effects 4-6); with `VertexColors`, the mesh's own colours multiplied in (Shield wave and ripple); with `Material` set, a lit model material drawn as is, colour as `_BaseColor` (EffectMesh own material, MParticle).<br>• `EffectModels`: ABIFF models by file name, cached (submesh meshes, base transforms, textures, HDRP Lit materials).<br>Controls hand geometry over in `CollectBillboards` / `CollectStrips` / `CollectMeshes`. |
| Mesh deform | `Rendering/CatMesh/CatMeshDeformHost.cs`, `CatMeshSourceVertices.cs` |
| Anim notes | `Rendering/CatMesh/AnimNoteIds.cs`, `CatAnimRuntimeClip.Notes`, `CatAnimPlayer.NoteReached` |
| Network hooks | `Playfield/PlayfieldFactory.cs`: `OnCharacterAction` (FinishNanoCasting, SetNanoDuration), `OnBuff`, `OnCastNanoSpell` |
| Test scene / tool | `DEV/GfxTest_DEV.cs` (a caster/target duo, `SpawnNano`, `Handler`, `ClearEffects`) and the editor window **Lost Eden → GFX Test** (`Assets/Editor/GfxTest/GfxTestWindow.cs` + `.uss`: effects/nanos browser, status chips, C/T/I/H/B dots, click to select, double-click to spawn or cast) |
| Tests | `Tests/Effects/*.cs` + `LostEden.Effects.Tests.csproj`, which **links** the Unity-free sources. Run `dotnet test` in `Tests/Effects`, then delete `bin/` and `obj/`. |

Additive brightness: `EffectBillboardBatch.AdditiveHdrBoost = 3` multiplies additive RGB so HDRP bloom
has energy to catch. **Not stock**, kept by the user's choice (§9).

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

As of 2026-09-22 (after 71016's types, Stars starTypes 18, 2, 15 and 20, Sparks, Tracer5, Fire, Smoke, Spiral, Suns sunTypes 0 and 1, Shield's per-vertex alpha, Tracer3 with Nano0, ShockWave, VulcanRocks, VolGrid, Sprite, Stars #5, #9 and #13, SkyFlash, Trail2, EffectMesh's dynel variants, Toggle and TParticle2), with the buff slot counted:
**7,756 nanos have effects. 7,714 are verified, 15 unverified, 4 approx, 23 missing.** Shield2 moved
25 nanos off Missing onto Scatter, rebuilding Scatter cleared them, and Highlight — once its fade
actually rendered — cleared the last unverified port of any size. Missing rose earlier, when buff effects started being counted.

**Ids that aren't in gfxtweak.bin count as Verified** (user's decision, 2026-09-22; it replaces the earlier
rule that kept them Missing). Stock draws nothing for them, and the port draws nothing too:
- Stock loads the effect table only from `Setupf/gfxtweak.bin` (`100ce664`, through
  `AnarchyPath_t::GetSetupPath`). There is no other copy on disk.
- The dynel factory (`100d0656`) looks the id up first (`10107068`) and returns 0 when it isn't there.
  No control is made, nothing is drawn and nothing is logged.
- The port: `EffectHandler.CreateControl` returns null for such an id. It used to draw a stand-in flare
  instead, which stock never did. The buff and tracer paths warn only when an id that *is* in gfxtweak
  fails. `EffectCoverage` rates the id Verified with no gap.
- The two ids nanos reach:
  - 39745 is the buff effect (stat 413) of 39 nanos, e.g. 70300 Total Mirror Shield Mk X. Their cast,
    tracer and hit play; the buff shows nothing, in stock as in the port.
  - 71025 is one Meta child of 71016 (the big explosion). The rest of the explosion plays; that piece
    never appeared in stock either.

Next targets, by nano count (the scratch coverage tool's `rank` mode counts each gap across all nanos):
- Spiral2 (8 / 1), CrazyCone (7 / 0), Beam (6 / 0), Stars #14 and #12 (6 / 6 each), GroundRing (5 / 0)
- Stars #14 and #12 (6 / 6 each) are the largest unverified ports left
- ~~EffectMesh's last approx: effect 4 lighting on 71123, 71224 and 72623~~ — fixed 2026-09-23 with a lit additive material; the only approximation left anywhere is SkyFlash's negative field 18 (1 nano)
- The other Stars starTypes (14 and 12: 6 each)
- Deformer modes 0/4; Electra modes 0/2
- The buff-slot gaps, which haven't been ranked yet: sort the Nanos tab by the B dot.

---

## 9. Open questions and known deviations

| Item | Detail |
|---|---|
| **Stock frame rate** | `EffectFrameRate.StockProcessHz = 30` is an assumption. Stock runs Process once per frame, uncapped. Get the number from `/framerate` (Ctrl+Alt+F) in the real client while 28612's hit plays. |
| **Vertex colour** | Since 2026-09-22 every effect quad and strip, and the Shield's per-vertex meshes, draw through the vertex-colour shader (§6). Checked against HDRP/Unlit on frozen frames: additive quads alone match within 3 levels (62 pixels over 2); with alpha Smoke among them, within 7 (28 pixels over 4), because a batch of additive quads now sorts against alpha draws as one object. Animated sprites still take one draw per frame texture (Fire 2200: 16 quads, 16 draws), since `EffectAtlasFrames` hands out each frame as its own texture; drawing from the atlas with UV rects would merge them. `EffectBillboardBatch.VertexColorPath = false` restores the old HDRP/Unlit path. |
| **Additive brightness** | **Deliberate deviation (user's call, 2026-09-22).** `AdditiveHdrBoost = 3`; stock draws colour × texture unscaled (×1) with no bloom. At ×1 a sprite never exceeds 1.0, so a bloom threshold of 1 leaves effects unglowing; ×3 gives bloom energy at the cost of stock colours (pastels wash toward white). The alternative, not built, is an effects-only glow Custom Pass that keeps ×1 colours. The user tunes the bloom volume. HDRP also blends in linear space and doesn't clip overlaps at 1; D3D blended in gamma into an 8-bit buffer. |
| **Buff message semantics vs the server** | Stock: CharacterAction 98 adds a buff, and a Buff message with `Unknown1 == 0` removes it. The previous code played the buff on Buff and treated 98 (`AnimKindIds.AttackSwingAction = 0x62`) as an attack swing. Not yet checked against what the server actually sends. |
| **Buffs already running** | When a character appears with active nanos, stock recreates their effects (`FUN_1004f2bd` / `FUN_10051c0c`). Not ported. |
| **Buff conflicts** | Stock's stacking rule (`FUN_1004e9cc`) is not traced; the port only replaces the same nano. |
| **Hit-location lifetime** | When stock deletes a hit location isn't traced. Stars 19 would stop spawning once it's gone; the port never deletes one. |
| **Release clip choice** | Stock picks the release clip from stats 377/378 and a default that isn't traced; the port uses kind names spell-dir / spell-self. Unfired-note flush on clip removal isn't ported. |
| **Random tables** | `100d3005`'s shared unit-vector table and walk are replaced by fresh draws with the same distribution. |
| **Deformer source positions** | Stock feeds the wave the CATTriVertex (0x44-byte) source position from randy31.dll; the port uses mesh bind positions (believed equal, unconfirmed). |
| **Spell1 windows 3/4** | Still the earlier model (only matters when field 33 == 0). |
| **BuffPlaceHolder child delta** | **Deliberate deviation, kept for consistency with the rest of the port (user's call, 2026-09-22).** Stock runs the children once per 0.45 rad step (~28/s), each with the whole frame delta, so the Cord's trail depends on the client's frame rate. A point lives 0.5 s of summed deltas, and 32 points at most. At 30 fps that's ~15 points, ~1.1 turns: the tail fades just behind the spark. At 60 fps it's ~30 points, ~2.1 turns: the spark runs into its own trail and the overlap stacks brighter, as seen in the live client. At 100+ fps it's 32 points, ~2.3 turns, and the tail cuts off half bright. The outward drift (`v·clamp(dt, 0.01, 0.025)` per step) also grows with frame rate. The port feeds one stock frame per step (`EffectFrameRate.StockProcessSeconds`, `GfxControlBuffPlaceHolder.ChildDt`), so ours is the 30 fps look. To match a real client, feed the frame delta there instead. |
| **MParticle step** | **Port decision (2026-09-22).** Stock's bounce restitution is 100·b·dt, which follows the frame rate. At 30 fps, 71045's b = 0.5 returns 165%: a chunk that ends a frame under the ground flips and grows every frame, and runs hundreds of metres off (seen live). At 60 fps it's 83%, and at 100 fps 50%, which looks like what the data was tuned for. `GfxControlMParticle` cuts each call into pieces of at most 1/60 s, so ours is stock as it runs at 60 fps or more. This is also evidence against `StockProcessHz = 30`. |
| **Fire 2200 direction** | Stock's code sends the Rage flames down the attach-0 frame's z, i.e. out behind the character along the ground (§5.25). That's the port's result too. Not yet compared with the live client; if the client shows them rising, the attach-0 frame differs from the dynel frame somewhere in N3. |
| **Weather wind speed** | `EffectWind.WeatherSpeed` is 0: the port has no weather system, so the wind is stock's no-weather gusts. The weather controller (presets, blending, per-zone wind speed) isn't traced (§5.24). |
| ~~**EffectMesh lighting**~~ | **Fixed 2026-09-23.** Rendering effect 4's render states are recovered exactly (factory `1006c62e`, its arm at `1006c912`): `D3DRS_LIGHTING=1`, `ZWRITEENABLE=0`, `FOGENABLE=0`, `ALPHABLENDENABLE=1`, `SRCBLEND=SRCALPHA`, `DESTBLEND=ONE`, `CULLMODE=NONE` — additive **and** lit, two-sided, no depth write. It is now drawn through `EffectModels.Part.LitAdditive`, an HDRP Lit variant with `_BlendMode` additive, built beside the existing `LitFade`, carrying the model's own diffuse instead of a constant. 71123, 71224 and 72623 are Verified. Own-material models (effect 0) still use the environment's HDRP Lit materials, and effect 4 now has the same standing. |
| **Locator template on other types** | Fields 1-3 are the locator's offset and 4-6 its turn (`EffectHandler.TakesLocatorTemplate`). Stock has two sibling setters — `101067af` takes the six fields, `10106be2` takes six zeros — and every control has one ctor per locator kind, of which only the **dynel** one passes the fields; that is why the port applies it to dynel/visual locators only. The list is now RTTI-complete: every call site of `101067af` and of its wrapper `_GfxControl_t::InitDynelTemplate` (`100d2d84`) was walked back to the vftable its ctor installs. Ported types reaching it directly: Flare, FlareAlt, Nano0, Nano1, Sparks, BuffPlaceHolder, Cord, Fire, Smoke, Spiral, Sprite, VulcanRocks. Through InitDynelTemplate: Stars, Suns, BParticle, BParticle2, EffectMesh, GroundGrid, MParticle, TParticle, TParticle2, VolGrid, SkyFlash, Trail2. The 2026-09-22 pass added the second group (113 records onto their offsets; checked live on 28611 Hot Foot). A later pass the same day added **Flare, FlareAlt, Nano0, Nano1, Nano3 and Sparks**, which had been missed: that is 21 Flare records and 37 Sparks records carrying a **rotation**, so they had been drawn unturned. Found from 204580 Tattered Flame, whose hit (Meta 47386) is Stars 43713 + Sparks 43714 (field 6 = -pi/2) + Flare 43715 (fields 5/6 = -+pi/2); the port had been dropping both turns. **Electra** was removed in the same pass: its three ctors call neither setter, so stock never gives it a template (no Electra record has a non-zero field 1-6, so nothing moved). `100d2f58` is also called by unported classes (Bubble, CrazyCone, Drips, Font, GlobalSmoke, GroundRing, Hexagram, LavaBall, Mesh, MeshTest, Particle, SpinningShot, Trail, Vein, WaterRipples, AParticle, Beam, EnergyBall, GroundShake, ...); add each when it is ported. **GroundShake** and **Spiral2** were added when they were ported. Hit-location and point ctors don't use it. |
| **Spell1's locator template** | Spell1's dynel ctor (`100f333b`) builds **three** locators and passes fields 1-6 to only one of them (`100f3533`, on a different dynel argument than the two that get zeros at `100f3474` / `100f34e1`). The port has a single Spell1 locator, so applying the template to it would turn all three; Spell1 is therefore left out of `TakesLocatorTemplate` until the three are told apart. No Spell1 record's rotation has been checked against stock. |
| **Shield blend flag** | Flag 0x400 goes to the base GfxVisual (+0x190), read by randy31 (not in the Ghidra project). The port treats it as additive, like the Electra/Sol ctor flag; unconfirmed. |
| **VulcanRocks model table** | **By decision (user, 2026-09-22).** Stock's rocks are the meshes of `VisualEnvFX_t`'s model table, which only the environment effects fill (`ActivateFX`). The records' slot 4 (rock01) is loaded once any FXID 4 environment effect has run in the session, and before that stock throws no rocks at all. The port has no environment effects and takes slot 4 as loaded (`GfxControlVulcanRocks.LoadedModels`); every other slot gives nothing, as in stock (§5.31). |
| **SkyFlash torso radius** | Stock scales a negative field 18 by the target's torso sphere radius (§5.34). The port can't read that radius (see the next row) and uses 1, stock's value for a mesh without one. Only 12510 (nano 256069 Xenosquad, field 18 = −2) is affected, and it is rated Approx. |
| **Trail2 attractors** | Every Trail2 record attaches to Attractor07_special (2006) or Attractor08_special (2007). GfxTest's caster (Solitus female) has neither, so stock's lookup (`10105e6d`) and the port both fall back to `Attractor01_head` (§3.2), and the live check (§5.35) ran there. Not established: which mesh the vehicle nanos show on the character, whether it carries those two attractors, and which way their z axes point. The thrust drift runs along that z, and the 0x800 test blacks a sample that moves against the dynel's forward. So the trail's look on the real mesh, whether it streams behind or goes dark, is unchecked. To check it: find that mesh's CAT mesh id, load it on the GfxTest caster, and make buff 72308 on it. |
| **Trail2 / EffectMesh rendering switch** | Built on a dynel, Trail2 and EffectMesh hide their visual while the dynel's mesh flag is clear (Trail2 `10115071`, EffectMesh `1010c9c9`: `VisualMesh_t` +0xc → +0x94, or `VisualCATMesh_t` +0x90 → +0xd0). What those flags mean isn't traced, so the port always draws (§5.20, §5.35). EffectMesh also reads the body scale only while shown. |
| **Highlight transparency** | Stock's `RRefFrame_t::SetTransparency` fades the body itself, and every Highlight record a nano reaches is transparency-only. A `MaterialPropertyBlock` cannot do it: the body materials are opaque `HDRP/Lit` and HDRP ignores alpha in the opaque pass. `EffectMeshTint` now clones each renderer's materials, switches the clones to transparent (`HDMaterial.SetSurfaceType`, alpha blend, no z-write, queue 3000) for the tint's life and restores the originals on clear. Verified by reading the live material back — surface type 0 to 1, queue 2225 to 3000, alpha 1.00 to 0.20 and back, originals restored — and confirmed on screen by the user on 275524 Monster Cloak Nano. |
| **Highlight emissive units** | `EffectMeshTint` used to scale the stock emissive by an invented `Lerp(0.15, 2.5, transparency)`, which is why 11506's `(0.2, 0.2, 0.2)` landed at `(0.124, 0.124, 0.124)`. Removed — the ramp's rgb now goes straight into `_EmissiveColor` as stock writes it. Whether HDRP's emissive units want a scale factor is still open, but no nano-reachable record has a non-black emissive. |
| **Highlight mode 3** | Stock's mode 3 skips the body's own node (`100e2faa`) and tints only the attached items, filtered by their +4 (`100e300b`), adding `SetSpecular`. The port tints everything under the highlight root and writes specular there. Only 11507 and 11508 use mode 3 and no nano reaches them, so they are marked Approximated. |
| **Highlight flag 0x400** | With 0x400 stock's start (`100e309b`) resolves the dynel's CAT mesh and registers a per-mesh callback (`100e2e80`) instead of walking from the root; the port always walks the root. Records 61110 and 61112 set it. Nothing visibly differs in GfxTest, but the set of meshes tinted has not been compared. |
| **Scatter's locator bases** | Stock picks a copy's base by the locator's mode: a stored world point, a 4x4 it snapshots once from the host's mesh or CAT frame (`10110963`), the dynel's own position (`10155340`), or the locator object's matrix. The port uses the locator's resolved position for all four, so a Scatter whose stock base was the snapshot rather than the live frame will follow the host where stock would not. No record has been seen to depend on it. |
| **Shield2's graceful stop** | Slot 6 (`10111071`) records the age, and Process turns that into a 1-to-0 ramp in the visual's +0x20c — which **no code in GfxVisualShield2 reads**, along with the age at +0x210 and field 17 at +0x208. So a terminate is a 2 s delay before the control goes, not a fade. The port does the same. |
| **Shield2's build gate** | Stock hides the shield while the host CAT node's +0xc4 is under 0.95 (`1011120f`), which looks like a staged mesh build. The port has no such staging and draws as soon as the renderer is active. |
| **Shield2 on a mech** | Field 20 in 0..9 selects an EP03 mech `.abiff` (`10111888`) and the visual then takes the ref-frame path (`1001e03a`) instead of the host's CAT mesh. Records 71320-71339 only; no nano reaches them, and the port leaves them on the host body and marks them Approximated. |
| **TParticle2 recycled colours** | Stock's emitter respawn (`10114025`) sets the visual's position, widths and trail but not its two vertex colours, so a recycled particle carries the colour its previous life ended on for one call, and a brand new visual's are uninitialised. The port keeps the carry-over and seeds a new particle from the curves at t = 0 (§5.37). |
| **TParticle2 edge fade** | Flag 0x40000 takes the view direction from the camera to the *visual's* origin, which for these particles is the world origin, since stock keeps their vertices in world space under an identity transform. The port does the same. No record sets the flag, so it has never been seen (§5.37). |
| **Toggle mover** | Toggle's 0x2000 / 0x8000 read the dynel's `Vehicle_t` direction (±1, set by `SetDirection` from Gamecode's movement code) and speed. Which movement sets −1 isn't traced; the port takes −1 while `CharacterMotor`'s Backward flag is set and +1 once Forward is, and the motor's current speed. Not seen live: GfxTest's dynels have no motor (§5.36). |
| **Toggle playfield** | `GfxControlToggle.PlayfieldId` stands in for the current playfield (stock: `n3Playfield_t::GetPlayfieldResource` +0x1c) and nothing sets it yet; the resource's +0x50 flags aren't read. Only 72106, 3421 and 73100 use the test, and none reaches a nano. |
| **ABIFF UV transform** | randy31's `FAFAnim_t` evaluate leaves a UV track's tiling / offset on the animation (+0x84 / +0x8c); the code that applies them to the texture wasn't found. The port applies D3D's usual texture transform, u·tiling + offset, which decides the scroll's direction on the hoverbike circles (§5.20). |
| **AODB doesn't read the whole CAT mesh** | The port reads CAT meshes (RDB type 1010002) through the AODB package (`Assets/Packages/AODB.1.0.6`, `AODB.Common.RDBObjects.RDBCatMesh`). It reads textures, materials, joints, mesh groups and attractors, and leaves the rest as unnamed fields: `Unk1` (the first 32 bytes; in Solitus female 5927 they begin `CollSphere01`), `Unk2`-`Unk6`, `UnknownFloats` (4 floats each; none in 5927) and each group's `UnknownThings`. Stock parses the same record in randy31 (the FAF archive: `FAFCollisionSphere_c::Instantiate` `10016b30`, `CATGroup_t::GetColSphere` / `GetColSphereCnt` `10011d3f` / `10011d3b`). DisplaySystem's loader (`100704b8`) copies a 16-byte sphere (radius, then x, y, z) from the parsed data at +0x30 into `VisualCATMesh_t` +0xbc, read by `GetTorsoSphereRadi` (`10072c22`) and `GetTorsoSpherePos` (`10072bfc`). Which bytes of the record become that sphere isn't traced, so the port has no torso sphere, bounding sphere or collision spheres. Known users: SkyFlash's negative field 18 (above). Tracing it means reading randy31's FAF CAT mesh reader and mapping its fields onto AODB's unnamed ones. |
| **ShockWave paths no record takes** | **Not ported, by decision (user, 2026-09-22): nothing would look different.** All 11 ShockWave records (flags 0x3801 ×9, 0x2801 ×2) set 0x800 and none sets 0x8000, so two stock paths never run. (1) Without 0x800 the rings blend Zero/SrcColor, a darkening multiply. Drawing it would take a third blend setup in the vertex-colour shader, without the premultiply and the exposure scaling. (2) 0x8000 is read but not traced; it may be draw order. Port either one only if new data sets these flags. (§5.30) |
| **Game path untested live** | The FinishNanoCasting / SetNanoDuration / Buff handlers compile and follow stock, but have only been exercised through GfxTest, not against a server. |
