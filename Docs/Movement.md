# Character movement: reverse-engineering notes and port guide

Companion to `Docs/Camera.md`. That document covers `Vehicle_t` (the integrator) and the camera
vehicles; this one covers the **character** vehicles and the **surface** stack they walk on.

Read `Docs/Camera.md` §4 first — `Vehicle_t`'s field map, the sub-stepping loop and the integration
step are recorded there and are not repeated here.

## 3. The class graph

Verified by RTTI (`scratchpad/hier.py`), not inferred:

```
Vehicle_t                    [Vehicle.dll]   integrator + steering behaviours  (PORTED, see Camera.md)
└── DummyVehicle_t           [N3.dll]        surface binding
    ├── CameraVehicle_t      [N3.dll]        {FirstPerson, FixedThird}         (PORTED, see Camera.md)
    └── CharVehicle_t        [Gamecode.dll]  vftable 101619b4, 45 slots
        ├── PlayerVehicle_t  [Gamecode.dll]  vftable 10161bfc
        └── NPCVehicle_t     [Gamecode.dll]  vftable 10161b2c
```

`CharVehicle_t`'s full base list (`hier.py gc CharVehicle_t`) is 7 entries:
`CharVehicle_t`, `DummyVehicle_t`, `Vehicle_t`, `LocalitySource_t`, `SpaceCellAble_i`,
`LocalityListener_i` (mdisp 36), **`n3DynelEventListener_i` (mdisp 352)**.

The last one is worth noting: a character vehicle *listens to dynel events*. The camera work's
diagram did not have it.

### 3.1 What each subclass overrides

`scratchpad/vtdiff.py gc CharVehicle_t PlayerVehicle_t NPCVehicle_t` — 45 slots, both subclasses
override the same shape:

| slot | `CharVehicle_t` | `PlayerVehicle_t` | `NPCVehicle_t` | meaning |
|---|---|---|---|---|
| 0 | `100702f0` | `10071938` | `10071492` | dtor |
| 18 | `1013f1e8` | `10071516` | `10070e8e` | |
| **19** | `10132428` | **`10071537`** | `10070ed6` | **longitudinal steering** (`Vehicle_t` slot `+0x4c`) |
| **20** | `1012d800` | **`100716d5`** | `10070f9b` | **lateral steering** (`+0x50`) |
| **21** | `1012d800` | **`10071795`** | `10070fa0` | **turn steering** (`+0x54`) |
| 26 | `1006f49d` | `100717d9` | `10071149` | |
| 35 | `1013f1e8` | `1013047c` | `100a0ce3` | |
| 36 | `1013f1e8` | `10071519` | `100a0ce3` | |
| 37 | `10079931` | `10079931` | `10070e39` | (NPC only) |
| 38 | `1013f1e8` | `100718f0` | `100713de` | |
| 42 | `1006f55f` | `10071850` | `1007135a` | |
| 43 | `1006f587` | `100718a6` | `10070fea` | |
| 44 | `100702da` | `100702da` | `1007142c` | (NPC only) |

Slots 1-17, 22-25, 27-34, 39-41 are shared with `CharVehicle_t` and are the real base behaviour.

### 3.2 `NPCVehicle_t` is a different mover, not a variant

Read 2026-09-24. The four-axis input model in §4 is **`PlayerVehicle_t`'s alone**. `NPCVehicle_t`
overrides the same three steering slots and answers them completely differently:

| slot | `NPCVehicle_t` | what it does |
|---|---|---|
| 19 longitudinal | `10070ed6` | path / follow-target, below |
| **20 lateral** | `10070f9b` | **`xor eax,eax; ret 4`** — no lateral steering at all |
| **21 turn** | `10070fa0` | **`xor eax,eax; ret 4`** — no turn steering at all |

So an NPC has **no strafe and no turn channel**. It cannot be driven by the player's axes.

`NPCVehicle_t::CalcLongitudinalSteering` (`10070ed6`):

```
if (GetMovementState() == 1)                 return SteeringHalt(out)              // 101552d0
if (HasFollowTarget())                                                             // 1006f4cb
    return SteeringDirArrive(FUN_10070776(0), out)                                 // 101552d4
if (this->path(+0x360).Empty())              return None                           // Path_t::Empty
target = this->path.Size() > 1
       ? this->guide(+0x38c).GetGuidePos()   // PathGuide_t::GetGuidePos, 101552d8
       : this->path.Size() == 1 ? this->path.GetWaypoint(0) : none                 // 101552dc
*out = target
```

**`+0x360` is not a float on an NPC.** Where `PlayerVehicle_t` keeps `forwardDrive` at `+0x360`,
`NPCVehicle_t` embeds a **`Path_t`** there and a **`PathGuide_t`** at `+0x38c`. The two subclasses
reuse the same offsets for entirely different members, so the field table in §4 must not be applied to
an NPC.

The NPC factory (`1005796f`, the block that §8 listed as unread) has the same shape as the player
factory at `10057826` and the same visible constants — `10`, `1`, `0.5`, `1.5` — so the physical
setup looks common; the difference is all in the steering.

**What this means for the port.** `N3CharVehicle` is `PlayerVehicle_t`. It is currently attached to
NPCs as well, but nothing drives their axes — only `PlayerController.SetInputs` feeds any of them — so
their four floats sit at zero and all three channels return `None`. Everything an NPC actually relies on
today (gravity, the ground clamp, statel collision, the orientation update) is in the shared
`CharVehicle_t`/`Vehicle_t` base and is correct for both. So this is **latent, not a live bug**.

Details:

- **`Path_t::AddWaypoint` discards the Y** (`10005c08`: it loads `[eax]` and `[eax+8]` and pushes a
  literal zero between them). Paths are flat, which is why the steering forces the target back to the
  body's own height. A waypoint repeating the previous one is dropped — stock builds the segment
  direction and tests it with `Vector3::IsZero` (`10005c5f`).
- `Path_t` keeps a parallel per-segment **(unit direction, length)** list at `+0x14`, 16 bytes an entry,
  built incrementally, plus a running total at `+0x28`.
- `MapPathDistanceToPoint` (`10005a2e`) walks segments while `remaining > segLen` and returns
  `waypoint[seg] + dir[seg] * remaining`. Past the end it returns the **final waypoint** (`10005a77`
  takes `last - 1`), it does not extrapolate.
- **`PathGuide_t` is 24 bytes** and the whole model is
  `guidePos = path.MapPathDistanceToPoint(maxSpeed * time)`. `GetGuidePos` (`10006a47`) is a bare
  `lea eax,[ecx+8]; ret`, so the point only moves when an update call runs. The constructor
  (`100069d9`) stores its arguments and does **not** map — only `RestartGuide`, `UpdateTime` and
  `UpdateAddTime` do. `UpdateAddTime` is the one that does **not** guard the path, so in stock it walks
  a null or empty one.
- **`Vehicle_t::SteeringDirArrive`** (`1000ac8c`) is arrive-unless-overshot: it dots "target from here"
  against the same vector measured from the previous position, in XZ (`+0x58`/`+0x60` against `+0xd0`/`+0xd8`) and
  **halts when that goes negative**, which is what stops an NPC orbiting a waypoint it cannot land on.
  Otherwise it is `SteeringArrive` with a brake distance of **0.2** (`10012298`).

`FollowTargetMessage.PathInfo` carries the waypoint list the server sends for an NPC, which is what
`Path_t` consumes; `AddWaypoint` discarding Y matches a flat path list.

`CharMovementStatus` is the other movement channel the server uses, and it maps onto the **four input
axes** (`FwdState`/`FwdDir`/`StrafeState`/...), i.e. onto `PlayerVehicle_t`'s model. `NPCVehicle_t` has no
axes, so a status update carries no steering for one; only a path does.

## 4. Movement input is four float axes, not flags

Stock does **not** carry a bitmask of movement flags on the mover. The vehicle holds four floats, and the steering
channels read them directly:

| offset | meaning | written by |
|---|---|---|
| `+0x360` | **forward drive**. `> 0` forward, `< 0` reverse, `0` no longitudinal steering | `100717ef` |
| `+0x364` | **strafe**, along body right | `1007180f` |
| `+0x368` | **turn rate**, rad/s about world Y | `100717ff` |
| `+0x36c` | **vertical**, along world up | `10071840` |

The setters (disassembled at `100717ef`..`1007184d`):

```
100717ef  SetForwardDrive(float)   this+0x360 = arg          ret 4
100717ff  SetTurnRate(float)       this+0x368 = arg          ret 4
1007180f  SetStrafe(float, ?)      this+0x364 = f(arg0) * virtual_slot34(arg1)   ret 8
10071840  SetVertical(float)       this+0x36c = arg          ret 4
```

`SetStrafe` is the odd one: it calls the vehicle's own virtual at byte offset `+0x88` (slot 34,
`1006fddd`, not overridden by either subclass) with its second argument, passes the first through
`100718f8`, and stores the product. **Not yet resolved** — see §9 Open questions.

### 4.1 `PlayerVehicle_t` longitudinal steering — `10071537`

```
if (movementState is 9, 8 or 1)      return None           // FUN_10070a2f() == the movement state
if (hasFollowTarget) {                                      // FUN_1006f4cb()
    ... a SimpleChar_t dynamic-cast branch that may call FUN_1007034b(0, 2.0f, 0) ...
    return SteeringDirArrive(FUN_10070776(1), out)
}
if (this->0x360 != 0)
    return this->0x360 > 0 ? SteeringForward(out) : SteeringReverse(out)
return None
```

`FUN_10070a2f()` is the movement state (the camera work already recorded `7 = Fly`). States **1, 8
and 9 refuse longitudinal drive entirely** — identify them before porting; do not guess.

### 4.2 `PlayerVehicle_t` lateral steering — `100716d5`

Taken from **disassembly** (`100716d5`..`10071792`), because the decompiler lost the operand order:

```
if (this->0x364 == 0 && this->0x36c == 0)  return None

CalcBodyRight(this, &s_right)            // Vehicle.dll import [0x101552b4], into a function-static
lateral = s_right * this->0x364  +  (0,1,0) * this->0x36c
return Lateral                           // 3
```

The `(0,1,0)` is built inline at `10071740`..`10071754` (`fldz`/`fld1`/`fldz`), so the vertical axis
is **world up**, not body up. `1007030f` is `Vector3 operator*(float)` and `10023b81` is
`operator+`; both are `__thiscall` taking `(out, arg)`, i.e. two stack slots each, and MSVC
interleaves their pushes.

### 4.3 `PlayerVehicle_t` turn steering — `10071795`

```
if (this->0x368 == 0)  return None
out = (0, this->0x368, 0)
return Turn                              // 4
```

Per `Camera.md` §4.2 step 7, the integrator treats a `Turn` result as an axis whose **length is the
angular rate**, and — the part that matters for feel — it rotates the **body quaternion** when
`velocity.x == 0 && velocity.z == 0`, and the **velocity vector** otherwise. So a standing character
turns on the spot and a moving one arcs its velocity.

### 4.4 The constructors

`PlayerVehicle_t::PlayerVehicle_t(dynel, identity)` (`100714b1`) does almost
nothing: it chains `CharVehicle_t`'s ctor, zeroes the four axes (`+0x360`..`+0x36c`), and installs
three vftables — at `+0`, `+0x24` and `+0x160`, which match the RTTI base list's mdisp 0 / 36 / 352
exactly.

`CharVehicle_t::CharVehicle_t(dynel, identity)` (`1006f5c9`) chains
`DummyVehicle_t`, stores the identity at `+0x168`/`+0x16c` and the dynel at `+0x158`, sets
`+0x174 = 0.01f`, `+0x300 = 1.0f`, `+0x30c = 2.0f`, zeroes a 31-entry `Vector3` array at `+0x190`,
and allocates a 0x34-byte sub-object into `+0x178`.

**It sets no body constants.** No mass, no max velocity, no radius. Those come from the factory.

### 4.4a The player factory — `10057826`

```
new PlayerVehicle_t                     // object size 0x370 = 880 bytes
SetMass(50.0)
SetRelPosRot((0,0,0), AxisAngle((0,1,0), 0))
SetVel((0,0,0))
SetMaxForce(10.0)
SetMaxVel(1.0)
SetRadius(0.5)
SetBrakeDistance(1.5)
EnableFalling()
DisableSurfaceHug()
```

**This corrects `Camera.md` §4.2.** That table says surface hug defaults to 1 and calls it "what pins
a walker to the ground", with the camera turning it off. The player turns it off too, *and* enables
falling. A character is a **falling body**: gravity accumulates on `+0x54` and ground contact is made
entirely by `EnsureSurfaceAlignment`. There is no hugging.

`maxVel` starts at 1.0 and is overwritten every time the movement state changes — see §4.5.

### 4.5 Speed, force and brake distance — `1006f9eb`

Called from **27 sites**, i.e. on every movement-state change. This is the whole locomotion speed
model.

```
mass = this->0x34;  if (mass == 0) mass = 10.0;  SetMass(mass)

switch (movementState)                        // FUN_10070a2f()
  case 3:                                     // dir = FUN_10070a37()
      dir == 2 ->  base = 3.0,  slope = 0.7/275,    max = 9.099999, min = 1.05
      else     ->  base = 5.0,  slope = 1/275,      max = 13.0,     min = 1.5
      v = clamp(runStat * slope + base, min, max)
  case 4:        base = 3.0,  slope = 0.625/275,    max = 8.0,      min = 1.5
  case 7:        base = 7.0,  slope = 1/275,        max = 15.0,     min = 1.5   + DisableFalling()
  case 5:        v = 1.0      (constant, via 1006f7fe)
  case 2:        v = 1.5      (constant, via 1006f7fe)
  default:       v = 1.5      (constant, via 1006f7fe)

F = 2 * v * mass;  if (F > 100000) F = 100000
brake = (v * v * mass) / F * 0.5
if (F > 10000) F = 10000                      // the effective cap is 10000, not 100000
SetMaxVel(v);  SetMaxForce(F);  SetBrakeDistance(brake)
```

The base is also stored on the vehicle at `+0x170` before the stat is applied.

The constant-speed helper `1006f7fe` is the same arithmetic with a floor:
`if (v < 0.01) v = 0.1`, then the same `F = 2vm` and `brake = (v²m)/F·0.5`.

**`brake` is always `v / 4`.** `(v²m) / (2vm) · 0.5` reduces exactly. Every branch uses the same
formula, so the brake distance is a quarter of the max speed, always. Worth knowing before porting
it as four separate expressions.

**`F = 2·m·v` means the body reaches its max speed in 0.5 s** (`F = ma`, `a = v/0.5`).

## 5. The surface stack

`Surface_i` (`Vehicle.dll`, vftable `100121b0`) is a **9-slot** interface. Confirmed two ways: the
slot count of `FlatSurface_t`'s vftable (`100121d8`), and `n3TilemapSurface_t`'s vftable
(`1003c97c`) whose slots 0-8 are all `Surface_i` overrides and whose slot 9 onward belongs to its
second base.

| slot | signature (from the exported mangled names — no guessing needed) |
|---|---|
| 0 | `~Surface_i()` |
| 1 | `void CalculateClosestPoint(const Vector3&, Vector3& outPoint, Vector3& outNormal, LiquidMediumData_t*, LocalitySource_t*) const` |
| 2 | `void CalculateNormal(const Vector3&, Vector3& out) const` |
| 3 | `bool GetLineIntersection(const Vector3& a, const Vector3& b, Vector3& outHit, bool, LocalitySource_t*) const` |
| 4 | `bool GetLineIntersection(const Vector3& a, const Vector3& b, Vector3& outHit, Vector3& outNormal, bool, LocalitySource_t*) const` |
| 5 | `bool GetSphereIntersection(const Vector3& c, float r, Vector3& out) const` |
| 6 | `bool GetSphereIntersection(const Vector3& c, float r, Vector3& out, Vector3& outNormal, float& outDepth) const` |
| 7 | `bool IsInside(const Vector3&, LocalitySource_t*) const` |
| 8 | `bool VetoPosition(Vector3&, LocalitySource_t*, const Vector3* const) const` |

`FlatSurface_t` is a **stub**: it overrides only the destructor, inherits `Surface_i::IsInside`
(`10002026`), and leaves every other slot on `_purecall` (`1001086c`). It is not a working surface
and is not worth porting.

### 5.1 Implementations

| class | DLL | role |
|---|---|---|
| `n3TilemapSurface_t` | N3 | **outdoor terrain from the heightmap** |
| `n3RoomSurface_t` | N3 | dungeons / rooms |
| `KDTreeSurface_c` | N3, Gamecode | static meshes |
| `n3SurfaceResource_t` | N3 | teleportals, invisible walls |
| `CellSurface_t` | N3 | |
| `HouseSurface_c` | Gamecode | player housing |
| `FlatSurface_t` | Vehicle | stub, see above |

### 5.2 The heightmap chain

The chain from a ray to a `u16` sample:

```
n3TilemapSurface_t::GetLineIntersection            slot 4 — walks tiles along the ray
  └─ Intersect_Tile(tx, tz, &hit, &normal)         1001891d, 70 lines
       ├─ corner fetch                             1001769d, 73 lines
       │    ├─ outdoor height                      10017c3e  = sample(x,z) * HeightMod
       │    │    └─ chunk lookup                   10017bb9
       │    │         └─ sample read               10017b46  = u16 at patch[lz*(chunk+1) + lx]
       │    └─ indoor floor height                 10016454
       └─ ray/triangle                             100190f8   (×2, one per triangle)
```

**`n3Tilemap_t` is a thin wrapper.** `+0xc` is the `RDBTilemap_t` resource itself
(`GetTileMapResource` `10016320` is `mov eax,[ecx+0xc]; ret`), and every accessor reads straight
through it:

| `RDBTilemap_t` offset | meaning | accessor |
|---|---|---|
| `+0x18` | mode: **0 = ground/outdoor, 1 = dungeon/indoor** | `IsGround` `100163b0`, `IsDungeon` `100163bc` |
| `+0x1c` | height scale (AODB `HeightMod`) | read by `10017c3e` |
| `+0x8258` | `AnarchyGroundDataDB_t*` | `GetGroundData` `100163c9` |
| `+0x825c` | map width | `GetWidth` `1001639c` |
| `+0x8260` | map height | `GetHeight` `100163a6` |
| `+0x8264` | tile size (AODB `MapScale`) | read by `1001769d` |

**`AnarchyGroundDataDB_t` fields** used by the sampler:

| offset | meaning |
|---|---|
| `+0x31` | sample width flag — **0 means 8-bit**, and the read is then `(u16 >> 8)` |
| `+0x40` | chunk step — the patch stride is `+0x40 + 1`, so **chunks share their edge samples** |
| `+0x44` | `log2(step)`, the shift used to find the chunk |
| `+0x48` | `step - 1`, the mask used to find the sample within the chunk |
| `+0x50` | **grid width** — chunks per row |
| `+0x58` | patch array base, stride `0x54` bytes |

```
chunk  = patches[(z >> shift) * gridWidth + (x >> shift)]
sample = *(u16*)(chunk->0x1c + ((z & mask) * (step + 1) + (x & mask)) * 2)
if (!sixteenBit)  sample >>= 8
height = sample * HeightMod
```

**Corner fetch and the diagonal** (`1001769d`) — outdoor:

```
x0 = tileSize * tx;   z0 = tileSize * tz
c0 = (x0,          h(tx, tz),  z0         )
c1 = (x0+tileSize, h(xp, tz),  z0         )      xp = min(tx+1, width  - 1)
c2 = (x0+tileSize, h(xp, zp),  z0+tileSize)      zp = min(tz+1, height - 1)
c3 = (x0,          h(tx, zp),  z0+tileSize)
diagonal = (~tz ^ tx) & 1                        // a positional CHECKERBOARD
```

Indoor clamps the other way (`max(t-1, 0)`), uses `10016454` for the height, and the diagonal is
always `1`.

`Intersect_Tile` then tests **two triangles**, chosen by that flag:

| diagonal | first triangle | second triangle |
|---|---|---|
| 0 | `(c3, c0, c2)` | `(c0, c1, c2)` |
| 1 | `(c3, c0, c1)` | `(c1, c2, c3)` |

with a cheap reject first: if `max(c0.y..c3.y)` is below both ends of the ray, skip the tile. The
ray is passed through **file-scope statics** (`DAT_1005c7d0`, `DAT_1005c7e8`), not parameters — the
tile walk is not reentrant.

**The diagonal is the detail that matters.** It is not stored in the data; it is
`(~tz ^ tx) & 1`, i.e. tiles where `tx` and `tz` have the same parity split one way and the rest the
other. Get it wrong and collision disagrees with the rendered mesh by up to a tile's height
difference, on half of all tiles.

### 5.3 The tile walk — `GetLineIntersection` slot 4, `10018b72`

A textbook Amanatides–Woo DDA over the tile grid.

```
GetLineIntersection(start, end, outHit, outNormal, clipToMap, locality):

  s_rayStart = start;  s_rayEnd = end;  s_rayDir = end - start   // the file-scope statics
                                                                 // Intersect_Tile reads (5.2a)
  tileSize = tilemap.resource[+0x8264]
  width    = tilemap.GetWidth()
  height   = tilemap.GetHeight()

  if (clipToMap && !ClipRayToMap(&s_rayStart, &s_rayEnd, &clip))   // 10018540
      return false

  // a nested surface at this+0xc is tested FIRST, against the same ray
  hitChild = (this->0xc != null)
           && this->0xc->GetLineIntersection(s_rayStart, s_rayEnd, &childHit, &childNrm, clip, loc)

  // into tile space
  x0 = s_rayStart.x / tileSize;   z0 = s_rayStart.z / tileSize
  x1 = s_rayEnd.x   / tileSize;   z1 = s_rayEnd.z   / tileSize

  // per axis, with a shared 100.0 scale that cancels between tMax and tDelta
  if (x1 - x0 <= 0) { tDeltaX = -100/(x1-x0); stepX = -1; tMaxX = x0 - floor(x0)       }
  else              { tDeltaX =  100/(x1-x0); stepX = +1; tMaxX = floor(x0) + 1 - x0   }
  tMaxX *= tDeltaX                                        // and the same for Z

  n = |ix1 - ix0| + |iz1 - iz0|                            // Manhattan iteration bound
  ix = ix0;  iz = iz0

  while (n >= 0) {
      if (ix in [0,width) && iz in [0,height) && Intersect_Tile(ix, iz, &hit, &nrm)) {
          len2 = |s_rayEnd - s_rayStart|^2
          if (|hit - s_rayStart|^2 <= len2 && |hit - s_rayEnd|^2 <= len2) {   // inside the segment
              if (!hitChild) return true
              return (|childHit - s_rayStart| <= |hit - s_rayStart|) ? child : tile
          }
      }
      if (tMaxZ <= tMaxX) { iz += stepZ; tMaxZ += tDeltaZ }
      else                { ix += stepX; tMaxX += tDeltaX }
      n--
  }

  if (!hitChild) { outHit = (-1, -1, -1); return false }
  outHit = childHit;  outNormal = childNrm;  return true
```

Points that matter for the port:

- **The segment test is done with squared lengths against `|end - start|^2` from *both* ends.** A
  tile whose plane the ray meets outside the segment is rejected, not returned.
- **A miss writes `(-1, -1, -1)` into the out parameter**, it does not leave it untouched.
- **`this+0xc` is a nested `Surface_i`** tested before the terrain and merged by nearest-to-start.
  That is the hook by which static meshes / rooms compose with the heightmap, and it means
  `KDTreeSurface_c` and friends do not need separate walker support — they hang off here.
- `FUN_10001340` is `operator-`, `FUN_100013a9` is squared length.

### 5.4 AODB's tilemap parsing

Two further experiments closed this out.

**The client's header fields are not in the `CHGA` blob.** Every plausible `int16`/`int32` field
offset in the first 256 bytes of all 336 outdoor records was tested against the invariant
`(value - 1) % (side - 1) == 0`, where `side` is corroborated by a real inflated stream length. The
best offset held for only **45 %** of records; nothing held universally. So the record AODB parses is
not in the `fun::Message_c` named-field form that `AnarchyGroundData_t`'s constructor reads, and
the closed form in §5.4 cannot be applied to it.

**AODB's inferred grid is nevertheless exact.** Measured over all 336 outdoor maps, using the chunk
count and chunk-seam agreement (both edges, vertical and horizontal) as independent checks:

| rule | chunk count divides grid | seam mean | seam worst | perfect seams |
|---|---|---|---|---|
| **AODB `DeriveGridWidth`** | **336 / 336** | **1.0000** | **1.0000** | **334 / 334** |
| `ceil(width / step)` | 283 / 336 | 0.9392 | 0.4123 | 282 / 334 |

AODB's heuristic reconstructs the chunk grid **perfectly on every outdoor playfield in the
database**. There is nothing to fix.

Note also that the blob's width field is *not* the chunk-grid extent: record 570 is `w = 1080` with
`side = 65`, yet the real grid is 24 chunks wide (24 x 64 = 1536). Whatever that field measures, it
is not what the patch grid spans — another sign this blob is a different serialisation from the one
the DisplaySystem constructor reads.


### 5.5 The heightmap path

`n3TilemapSurface_t::Initialize(const n3Tilemap_t*, const n3Playfield_t*)` (`1001879d`) binds the
surface to a tilemap. `n3Tilemap_t` (`10016320`..) exposes `GetTileMapResource() -> RDBTilemap_t`,
`GetWidth`, `GetHeight`, `IsGround`, `IsDungeon`, **`GetGroundData() -> AnarchyGroundDataDB_t*`**,
`GetTileTypeGround(x,y)`, `GetTileTypeNoWalls(x,y)`, `ClampInside(Vector3&)`.

The surface's own workers: `GetCellIdFromPos`, `GetSurfaceForCell`, `Intersect_Tile(int,int,Vector3&,Vector3&)`
(`1001891d`), `AddSurfaceToArea` / `RemoveSurfaceFromArea`, `Sign(float)` (`1001884c`).

**The port already has this data.** `Assets/Scripts/Rendering/Terrain/TerrainParser.cs` parses the
same tilemap out of AODB — `ushort[,] Heightmap` per chunk, `HeightMod`, `MapScale`, `ChunkSize`,
`GridWidth` — and builds render meshes from it. What is missing is a world-space sampler, which is
exactly what `n3TilemapSurface_t` is.

## 6. `Vehicle_t::EnsureSurfaceAlignment` — the ground clamp

`1000d1aa`, ordinal 110, **4206 bytes / 716 decompiled lines** — the largest function in
`Vehicle.dll`. It is the walker's entire contact with the world.

Signature: `bool EnsureSurfaceAlignment(const Vector3& prevPos, bool force)`.

Called from five places: `SetRelPos` (`1000e265`), `SetRelPosRot` (`1000e375`), `Run` twice
(`1000e8a5`, `1000e9df`) and the sub-step loop `FUN_1000e3d3` (`1000e81a`). A `false` return breaks
the sub-step loop.

**All 716 lines read.** The shape, in order:

```
if (this->0x30 == null || GetSurface() == null)  return true        // no surface -> nothing to do
saved   = this->globalPos (0x58..0x60)
forced  = (!falling(0x50) || airborne(0x52) || force)
this->radius(0x4c) = body_vtable[+0x10]()                            // re-read from the body

// (1) VETO RETRY — up to 9 attempts
step = (pos - prevPos) * 0.1
for (i = 9; i > 0; i--) {
    if (!surface->VetoPosition(&pos, this, 0))  break                // slot 8, vtable +0x20
    pos = prevPos + step * i                                         // back off 10% per attempt
}

// (2) SWEPT MOVE — only when the move has length, and only when !force
//     FUN_1000b2e5(&pos, 0.4, &dir, &horizBudget, &totalBudget, surface, &ceilingY,
//                  falling ? 3 : 1, 10, falling && !parented ? 0.5 : -1.0)
//     See §6.6 — a full continuous-collision solver with wall sliding.

// (3) GROUND PROBE — a TRIPOD of three downward rays, radius 0.04
dirs = normalize(1,0,0), normalize(-1,0,1), normalize(-1,0,-1)       // lazily-built statics
for each dir:
    high = (pos.x, raisedY, pos.z) + normal*h + dir*0.04
    low  = (pos.x, 0.0,     pos.z)            + dir*0.04
    if (!surface->GetLineIntersection(high, low, &hit, &nrm, true, this))   // slot 4, vtable +0x10
        hit = low;  nrm = (0,1,0)                                    // a miss means flat ground

normal = normalize(cross(hit3 - hit1, hit2 - hit1))
if (normal.y < 0)  normal = -normal
this->0xa0 = normal

// (4) A SECOND TRIPOD when orientationMode(0xb0) == 1 — radius 0.2, reach 0.8,
//     directions (-1,0,0), (1,0,1), (1,0,-1). Otherwise the normal is just (0,1,0).

// (5) GROUND HEIGHT = max(hit1.y, hit2.y, hit3.y), then clamped up to the liquid height

// (6) SLOPE GATE   normal.y < (this->0x13c ? 0.001 : 0.5)  -> treated as unwalkable
// (7) STUCK COUNTER this->0x138 — incremented when Y did not change while falling;
//                   after 4 consecutive, the function gives up and returns false
// (8) NORMAL SMOOTHING
//     this->0x12c = normalize(this->0x12c * 0.9  +  normal * 0.1)
//     this->0xa0  = the same value
//     if (this->0xa4 /*normal.y*/ < 0.5)  normal = (0,1,0)
// (9) FALL / LAND transitions — FUN_1000a1a7(this) starts falling, LandNow(this, y) lands
// (10) LIQUID — switch on the collision profile this->0xfc (0..4); see below
// (11) final surface->VetoPosition, write globalPos, then FUN_1000c616(this, &normal)
//      for the body orientation
return changed
```

### 6.1 `+0x10c`, the slope limit, and the retry loop

That section was written from a partial read. Three things in it are wrong:

- **`+0x10c` is not a floor / death plane.** It is a **liquid depth**, written `-10000.0`
  (`0xc61c3c00`, line 640) when the body is clear of liquid, and `liquidHeight - y` when submerged.
  It is passed to `Surface_i::CalculateClosestPoint` as the `LiquidMediumData_t*` out-parameter. The
  default of `-9999` recorded in `Camera.md` §4.2 is also wrong; the reset value is `-10000.0`.
- **The slope limit is 60°, not 30°.** The gate is `normal.y >= 0.5` (lines 557 and 588), i.e.
  `cos θ >= 0.5`. The `1.1547005` constant is a *different* quantity — it scales the horizontal
  movement length into a probe half-extent (`|horizMove| * 1.1547005 + 0.48`), it is not the slope
  limit.
- **The retry loop backs off in fixed tenths, not by 10 % compounding.** `pos = prevPos + step*i`
  with `step = (pos - prevPos) * 0.1` and `i` counting **9 → 1**, so the attempts are at 90 %, 80 %,
  … 10 % of the original move.

### 6.2 The normal smoothing is per call, not per second

Step (8) blends the surface normal `0.9 / 0.1` **every call**, with no `dt` anywhere. At 30 fps the
normal converges in about a third of the time it takes at 100 fps. Under the project's standing rule
(`[[unify-frame-rate]]`, `Docs/Effects.md` §3.7) this is exactly the kind of per-frame constant that
must be driven from a fixed-rate clock or converted to a time constant rather than copied literally.


### 6.3 Fields this function uses

Beyond `Camera.md` §4.2's table:

| offset | meaning |
|---|---|
| `0xa0..0xa8` | **surface normal** (written here, read by the orientation update) |
| `0x100` | liquid depth threshold |
| `0x10c` | **liquid depth** (`-10000.0` when clear) |
| `0x120` | in-liquid flag; entering/leaving call vtable slots 32 (`+0x80`) and 33 (`+0x84`) |
| `0x12c..0x134` | **smoothed surface normal** (the 0.9/0.1 accumulator) |
| `0x138` | stuck counter, gives up at 4 |
| `0x13c` | when set, the slope gate relaxes from `0.5` to `0.001` |

### 6.4 Vector helpers in `Vehicle.dll`

Identified from their call shapes; all `__thiscall` taking `(out, arg)` so each call consumes two
pushed slots: MSVC interleaves the pushes of nested calls.

| address | meaning |
|---|---|
| `10001000` | `operator+` |
| `1000103e` | `operator-` |
| `1000107c` | `operator*(float)` |
| `10001398` | cross product |
| `100013c3` | squared length |
| `100026a4` | normalise and scale |
| `10002673` | scale to length |
| `10002a59` | is-zero test |
| `1000b2e5` | **the swept collision solver** — 10 iterations; §6.6 |
| `1000a1a7` | begin falling |
| `1000a719` | `LandNow` |
| `10009b1d` | position-changed test |
| `1000c616` | body orientation update (the camera's vetoes hang off this — `Camera.md` §5.7) |

Statics (`Vehicle.dll`): `s_vGravityAccel` `1001938c` = **-20.0**, `s_cReferenceForward` `10019398`
= `(0,0,1)`, `s_cReferenceUp` `100193a4` = `(0,1,0)`,
`s_bEnsureSurfaceAlignmentOnlyOnce` `10019390` = **true**.

### 6.6 The swept collision solver — `1000b2e5`

`__stdcall`, ten parameters, `1000b2e5`..`1000c391`. The function does **not** end at the `ret 0x28`
(`1000c23f`): the compiler moved four cold blocks — the "a budget ran out" partial moves — out of
line to `1000c242`, `1000c294`, `1000c2e8` and `1000c33d`, past the epilogue. Miss them and the
budget logic looks like it has no partial case at all.

Its only caller is `EnsureSurfaceAlignment` at `1000d41d`, and the argument setup there
(`1000d295`..`1000d41d`) is **part of the algorithm**, not glue:

```
move    = position - prevPos                       // the integrator's step for this frame
forced  = (!falling(0x50) || airborne(0x52) || force) ? 1.0 : 0.0        // 1000d1cf, a FLOAT
dir     = (move.x, move.y * forced, move.z)                              // 1000d390
          dir = dir.isZero ? (0,-1,0) : normalize(dir)                   // 1000d3ab / 1000d3b6
horizB  = |(move.x, 0, move.z)|                                          // 1000d317 + sqrt
totalB  = forced ? |move|
        : (|horizMove| >= 1e-6 ? 10000.0 : |move|)                       // 1000d33c..1000d37e
pos     = prevPos + (0, 0.4, 0)                                          // 1000d2c9, the probe lift
ceilY   = prevPos.y + 0.4                                                // 1000d2d4
mode    = 2 * falling(0x50) + 1                                          // 1000d3f6/1000d3fe
slope   = (falling(0x50) && !relaxGate(0x13c)) ? 0.5 : -1.0              // 1000d3c4..1000d3e2

FUN_1000b2e5(&pos, 0.4, &dir, &horizB, &totalB, surface, &ceilY, mode, 10, slope)
```

**Three things in that setup decide everything downstream, and all three are easy to get wrong:**

1. **`+0x50` is "gravity enabled", not "in the air".** `EnableFalling` sets it once and it stays set
   for the whole life of a character; "currently airborne" is `+0x52`. So **every character** gets
   `mode = 3` and `slopeLimit = 0.5`. Only the camera, which never enables falling, gets `mode = 1`
   and `slopeLimit = -1.0` — i.e. no shoulder probe and no slope limit at all.
2. **A grounded walker sweeps along a perfectly horizontal direction.** `forced` is `0.0` unless the
   body is airborne or the caller forced the update, and it *multiplies* `move.y`. Gravity's vertical
   motion is then handled by the tripod ground clamp, not by the sweep. This is why the steep-slope
   test inside the sweep is written `!(0 > move.y)` and not `move.y > 0`: with `move.y == 0` it must
   still fire.
3. **The walking case has an effectively unlimited total budget** (10000) and is limited only by
   `horizB`, the horizontal distance the integrator asked for.

```
target = pos + dir * 10.0                        // 1000b2ee, re-seated on every free move and slide
while (iterations-- > 0) {                       // 1000b328 dec / 1000c233 jg -- exactly `iterations`
    move = target - pos
    if (|move|² < 1e-08)  return                 // 1000b3ea
    move = normalize(move)                       // 1000b404

    if (mode > 1) {                              // 1000b409 -- on for every character
        // two LATERAL probes, each shortened to what it hits
        side  = (move.y² < 0.999999) ? normalize(cross(move, (0,1,0))) * halfWidth
                                    : (halfWidth, 0, 0)
        other = -side
        if (surface->GetLineIntersection(pos, pos + side,  &h, &n, 1, this))  side  = h - pos
        if (surface->GetLineIntersection(pos, pos + other, &h, &n, 1, this))  other = h - pos
        if (|side|² != |other|²) {               // 1000b5b3 -- asymmetric, so recentre
            a = pos + side;  b = pos + other
            pos = (a + b) / 2                    // the caller's position really does move
            side = a - pos;  other = b - pos
            if (pos.y > *ceilingY)  *ceilingY = pos.y
        }
        side *= 0.5;  other *= 0.5               // 1000b681

        // a forward ray from each shoulder (1000b6d3 / 1000b772)
        for each (offset, hit, normal) in {(side, hitL, nL), (other, hitR, nR)} {
            if (offset.isZero)                      -> invalid
            if (!GetLineIntersection(pos + offset, target + offset, &hit, &normal, 1, this))
                                                    -> invalid
            if (dot(normalize(offset), normal) > 0)  -> invalid     // back face, 1000b730
            denom = dot(move, normal)
            if (denom == 0)                          -> invalid     // 1000b9c5
            hit = pos + move * (dot(hit - pos, normal) / denom)     // onto the movement line
        }
    }

    centre = surface->GetLineIntersection(pos, target, &hitC, &nC, 1, this)   // 1000b801

    if (no hit at all) {                         // 1000b821..1000b839
        if (pos == target)  return                                 // 1000b845
        delta = (target - pos) * ((|target - pos| - halfWidth) / |target - pos|)
        if (advance(delta) != Moved)  return
        target = pos + dir * 10.0
        continue
    }

    // NEAREST of the three, a miss contributing 10000 (1000bbb7..1000bc74)
    (hit, normal) = argmin over {(hitL,nL), (hitR,nR), (hitC,nC)} of |hit - pos|²
    //   the tie-breaks are exactly: left only if dR > dL AND dC > dL; else right only if dC > dR
    //   -- the SHOULDER RAY'S OWN NORMAL is kept; stock never re-queries it
    pushDist = halfWidth

    if (!(0 > move.y) && slopeLimit > normal.y) {          // 1000bc7e, 1000bc90
        pushDist = normal.y² * halfWidth + halfWidth       // 1000bca3
        // 1000bcb3..1000bd0a, IN THIS ORDER and with THESE quantities:
        if      (normal.y <= -0.99)        normal = -move          // facing away
        else if (|normal|² <= 0.001)       normal = -move          // degenerate
        else if (|normal.x| > 0.001)       normal = normalize(normal.x, 0, normal.z)
        else if (|normal.z| > 0.001)       normal = normalize(normal.x, 0, normal.z)
        else                               normal = -move          // no horizontal part at all
    }

    back = -move
    d    = |dot(back, normal)|                            // 1000bd99 negates in place
    newPos = pos                                          // 1000bee6 -- the DEFAULT IS TO REFUSE
    if (d > 0) {
        t = pushDist / d
        if (!(|hit - pos|² < t²))  newPos = hit + back * t          // 1000bde6
    } else if (!(halfWidth² >= |hit - pos|²)) {
        newPos = hit + back * halfWidth                             // raw halfWidth, not pushDist
    }
    if (newPos != pos)  { if (advance(newPos - pos) == Exhausted) return }   // 1000bef8

    // ---- slide (1000c0a7) ----
    toHit = pos - hit
    if (toHit.isZero)                       return
    toHit = normalize(toHit)
    sep = |toHit - normal|²
    if (1e-05 > sep)                        return        // already flush with the face
    if (sep > 3.99999)                      return        // directly opposed
    if (normal.isZero)                      return
    slide = normalize(cross(cross(toHit, normal), normal))
    //    == normalize(normal*(toHit.normal) - toHit): the way the body was going, laid on the face
    if (!(dot(slide, dir) > 0))              return        // 1000c1d6, note >= 0 also returns
    target = pos + slide * 10.0
    if (normal.y == 0 && pos.y < target.y)  target.y = pos.y        // never climb a vertical wall
}
```

`advance(delta)` is the same code twice — `1000b8c3`..`1000b99d` for the free move and
`1000bf18`..`1000c002` for the collision response — plus the four out-of-line partial blocks. Two
independent arms, and **which arm runs is decided before either budget is compared to the step**:

```
horiz = |delta ⊙ (1,0,1)|                     // sqrt skipped when the squared length is <= 0
if (*horizBudget > 0 && horiz > 0) {          // 1000b8fb, 1000b90d
    if (*horizBudget <= horiz) {              // 1000b91c -> the out-of-line block
        pos += delta * (*horizBudget / horiz);  ceiling update
        *totalBudget = 0;  *horizBudget = 0;  return Exhausted        // ends the whole sweep
    }
    pos += delta;  ceiling update              // the TOTAL budget is not consulted at all here,
    *horizBudget -= horiz                      // so it can legitimately go negative
    *totalBudget -= |delta|
    return Moved
}
if (!(0 < |delta|²))  return Degenerate        // free move returns; collision falls to the slide
step = sqrt(|delta|²)
if (step <= 0)        return Degenerate
if (*totalBudget <= step) {                    // 1000bb14 -> the out-of-line block
    pos += delta * (*totalBudget / step);  ceiling update
    *totalBudget = 0;  *horizBudget = 0;  return Exhausted
}
pos += delta;  ceiling update
*horizBudget -= horiz;  *totalBudget -= step
return Moved
```

The ceiling update is always `if (pos.y > *ceilingY) *ceilingY = pos.y`.

**What this means for a character that walks into a face it cannot climb.** `move.y` is 0, so the
steep branch fires; `normal.y < 0.5`, so the slope is replaced by the **vertical wall underneath it**
and `pushDist` grows to `normal.y²·0.4 + 0.4`. The body is already within that distance of the wall,
so the push-out **refuses** the move outright and `newPos == pos`. The slide then computes
`toHit`, finds it is the flattened normal itself, `sep < 1e-05`, and returns. Nothing moves — not a
fraction of a frame, not a millimetre. The character runs on the spot, which is exactly what stock
looks like. Sliding *back down* is a different mechanism entirely: the slope gate in
`EnsureSurfaceAlignment` step (6) leaves the body **airborne**, and gravity does the rest — and once
airborne `forced` becomes 1.0, so the sweep sees a downward `move.y`, skips the steep branch, keeps
the real normal and slides along the face.

Helper functions used (all in `Vehicle.dll`, all verified from disassembly, not names):

| address | meaning |
|---|---|
| `10001000` / `1000103e` | `Vector3 operator+` / `operator-` — `(out, rhs)`, `this = ecx`, returns `out` |
| `1000107c` / `100010b8` | `operator*(out, float)` / `operator/(out, float)` |
| `10001351` | `this = cross(this, arg)`, **in place**, `ret 4` |
| `100013c3` | `LengthSquared()`, result in `st0` |
| `10001f4e` | `1.0 / Length()` |
| `10002673` / `100026a4` | `SetLength(float)` in place / `Normalized(out, float)` |
| `10002a59` | `IsZero()` — all three components exactly `0` |
| `10009ae1` / `10009b1d` | `operator==` / `operator!=` |
| `10009b59` | component-wise multiply, `out = rhs ⊙ this` |
| `10010952` | `sqrt` (an import thunk) |

x87 comparison idioms, since every branch in the function is one of these:

| pattern | meaning |
|---|---|
| `test ah,0x44` + `jnp` / `jp` | `st0 == src` / `st0 != src` |
| `test ah,5` + `jnp` / `jp` | `st0 < src` / `st0 >= src` |
| `test ah,0x41` + `jne`/`jnp` / `je`/`jp` | `st0 <= src` / `st0 > src` |
| `test ah,1` + `jne` | `st0 < src` |

### 6.7 Which `Surface_i` slots the walker uses

Only **three** of the nine, which sets the porting order for §5:

| slot | used for |
|---|---|
| 4 `GetLineIntersection(a, b, hit, normal, bool, locality)` | the tripod ground probe — **the hot path**, 3 or 6 calls per step |
| 8 `VetoPosition(pos, locality, prev)` | the retry loop and the final placement |
| 1 `CalculateClosestPoint(p, point, normal, liquid, locality)` | liquid/ceiling query |

`CalculateNormal`, both `GetSphereIntersection`s and `IsInside` are not reached from here. They may
be used elsewhere (the sweep at `1000b2e5` is unread), but they are not required for a walker to
stand on terrain.

## 7. Orientation, direction, and the `1000d1aa` stack frame

### 7.1 The stack frame of `EnsureSurfaceAlignment` (`1000d1aa`)

Its algorithm is recovered (§6) but its **frame layout is not**, and that is what a port needs.
The function juggles roughly forty stack locals through x87, and the decompiler's rendering of them
is demonstrably unreliable here: `local_54` is a `(0, 0.4, 0)` lift in one place and a surface normal
in another, `local_8` is `previousPosition.y + 0.4` and then `max(that, pos.y)`, and `local_20`
is decremented by `0.4` immediately after being used as the position's Y.

Two bugs came from trusting decompiler output over disassembly — the `log2`/`ctz`
misreading (§5.4) and the inverted tile diagonal (§5.3), the latter of which survived 67,200
real-terrain probes. Writing a forty-local collision function from the same source would be the
third, and this one would be hard to detect: a ground clamp that is subtly wrong still puts the
player roughly on the ground.

**What it needs first:** a disassembly pass over `1000d1aa`..`1000e217` mapping each `[ebp-0x??]`
slot to a named quantity, the way `Intersect_Tile`'s corners were pinned in §5.3. Until that is
done the honest state is "recovered, not ported".

#### The frame map so far (partial — `1000d1aa`..`1000d37e`)

Registers: `ebx` is `this`, `esi` is `previousPosition` in the second block.

| slot | meaning |
|---|---|
| `[ebp+8]` | `previousPosition` (the `const Vector3&` parameter) |
| `[ebp+0xc]` | `force` (the `bool` parameter) |
| `[ebp-4]` | `previousPosition.Y + 0.4` — the probe's raised Y |
| `[ebp-0x10]`..`[ebp-8]` | the **horizontal** movement, `(delta.x, 0, delta.z)` |
| `[ebp-0x14]` | the retry counter (9…1), **then reused** as the movement length |
| `[ebp-0x20]` | `previousPosition + (0, 0.4, 0)` — the probe origin |
| `[ebp-0x24]` | \|horizontal movement\|, squared then rooted in place |
| `[ebp-0x28]` | `forced`, held as a **float** 0.0 / 1.0, not a bool |
| `[ebp-0x38]` | the `Surface_i*` from `GetSurface` |
| `[ebp-0x44]`..`[ebp-0x3c]` | the movement delta, `position - previousPosition` |
| `[ebp-0x50]`..`[ebp-0x48]` | the constant lift vector `(0, 0.4, 0)` |
| `[ebp-0x5c]` | scratch: the delta, then `tenth * i` |
| `[ebp-0x74]`..`[ebp-0x6c]` | a zeroed `Vector3` |
| `[ebp-0xbc]` | `previousPosition + tenth * i` — the backed-off retry position |
| `[ebp-0xe0]` | **the working position**, copied from `this+0x58` |
| `[ebp-0xf8]` | the tenth step, `(position - previousPosition) * 0.1` |
| `[ebp-0x11c]`..`[ebp-0x114]` | a zeroed `Vector3` |

Two things this already corrects:

- **`[ebp-0x14]` is reused.** It is the veto retry counter and then the movement length. Ghidra
  renders both as `local_18`, which is what made §6's reading of that section ambiguous.
- **`VetoPosition`'s third argument is `null` at this call site** (`push 0` at `1000d24a`), not the
  previous position. The parameter is `const Vector3* const`, so the port's `Vec3 previous` should
  be treated as optional — the tilemap implementation ignores it either way.

`forced` being a float rather than a flag also matters: it is later *compared against zero* to
choose between two lengths (`1000d33c`), not tested as a boolean.

#### Mapped `1000d37e`..`1000d4e2` — the swept move and the step clamp

| slot | meaning |
|---|---|
| `[ebp-0x18]` | the working position's **Z** (so `[ebp-0x20]` is a `Vector3`) |
| `[ebp-0x1c]` | the working position's **Y** |
| `[ebp-0x20]` | the working position passed to the sweep — starts at `previousPosition + (0, 0.4, 0)` |
| `[ebp-0x28]` | **reused a third time**: `forced`, then the slope limit |
| `[ebp-0x68]`..`[ebp-0x60]` | the **normalised movement direction**, or `(0, -1, 0)` when the move was zero |
| `[ebp-0x74]` | the closest point from the surface |
| `[ebp-0x11c]` | the closest point's normal |
| `[ebp-0xf8]` | **reused**: the tenth step, then a copy of the closest point |

**`forced` is a multiplier, not a flag.** At `1000d390` the movement delta's **Y is multiplied by
it** before the sweep, so `forced == 0` makes the swept move purely horizontal. That is why stock
keeps it as a float — and why reading it as a bool would silently lose the behaviour.

**The slope limit reuses the same slot**, overwritten at `1000d3e3`:
`falling && !this->0x13c ? 0.5 : -1.0`.

**The swept call, resolved** (`1000d3f9`..`1000d41d`, arguments right-to-left):

```
FUN_1000b2e5(
    pos          = &[ebp-0x20],     // previousPosition + (0, 0.4, 0) -- NOT the current position
    halfWidth    = 0.4,
    dir          = &[ebp-0x68],     // normalised, (0,-1,0) if the move was zero
    horizBudget  = &[ebp-0x24],     // |horizontal movement|
    totalBudget  = &[ebp-0x14],     // |movement|
    surface      =  [ebp-0x38],
    ceilingY     = &[ebp-4],        // previousPosition.Y + 0.4
    mode         =  2 * (falling != 0) + 1,     // 3 when falling, else 1
    iterations   =  10,
    slopeLimit   =  [ebp-0x28])
```

When `force` is set the sweep is skipped entirely and the position is simply
`previousPosition + (0, 0.4, 0) + delta` (`1000d424`..`1000d453`).

**Then the step clamp** (`1000d457`..`1000d4e2`):

```
ceilingY = max(ceilingY, position.Y)
position.Y -= 0.4                                   // undo the lift before querying
surface->CalculateClosestPoint(&position, &closest, &closestNormal, &this->0x10c, this)
step = closest.Y + (this->0xfc == 4 ? 0.25 : 0.01)
if (position.Y < step)  position.Y = step
```

`this->0x10c` is passed as the `LiquidMediumData_t*` out-parameter, confirming §6.1: that field is
liquid data, not a floor plane.

**Constants, all read from the image:** `0.4` (`100127a0` / `100127f8`), `0.1` (`10012704`),
`1e-06` (`100127f4`), `0.5` (`10012134`), `-1.0` (`100127f0`), `0.01` (`100124e0`),
`0.25` (`100127e8`).

#### Mapped `1000d4e2`..`1000d5c0` — the slope term and the probe directions

```
if (force)  ceilingY = position.Y + 1.0                  // 10012190
else        ceilingY = max(ceilingY, position.Y)

halfExtent = 0.48                                        // 100127e0
if (falling && !airborne && !force) {
    len        = |movement delta|
    halfExtent = len * 1.154700517654419 + 0.48          // 100127d8 / 100127d0
    if (ceilingY < halfExtent)  halfExtent = ceilingY
}
```

`[ebp-0x28]` is now reused a **fourth** time — `forced`, then the slope limit, now the probe
half-extent. And at `1000d530` the **`force` parameter slot `[ebp+0xc]` is overwritten** with the
movement length, so the parameter is dead from that point on; anything later reading "force" is
reading a float. Ghidra renders both as `param_2`.

This settles §6.1's third correction with the actual arithmetic: `1.1547005` scales the **movement
length** into a probe extent and has nothing to do with a slope limit. The real slope gate is the
`normal.y >= 0.5` test further down.

The three tripod directions are function-level statics behind a one-time init mask at
`[0x1001a1c0]`, built and normalised on first use:

| static | built from | normalised |
|---|---|---|
| `[0x1001a1b4]` | `(1, 0, 0)` | `(1, 0, 0)` |
| `[0x1001a1a8]` | `(-1, 0, 1)` | `(-0.7071, 0, 0.7071)` |
| `[0x1001a19c]` | `(-1, 0, -1)` | `(-0.7071, 0, -0.7071)` |

Still to map: `1000d5c0`..`1000e217` — the tripod probes themselves, the second tripod, the liquid
branch and the fall/land transitions. Roughly half the function is now named.

### 7.2 The slope gate

`EnsureSurfaceAlignment` computed a `walkable` flag and then all but ignored it. Stock's logic
(`1000d539`-`1000d560`, dispatched at `1000d596`) is:

```
airborne = true                                  // starts true
if (position.Y - halfExtent <= groundHeight) {   // the body is at the ground
    if (groundRef < closestPoint.Y) groundRef = closestPoint.Y
    if (descending && falling) position.Y = groundRef + stepHeight
    gate = this->0x13c ? 0.001 : 0.5
    if (normal.Y >= gate && descending) airborne = false
}
if (falling) { if (airborne) BeginFalling(); else if (wasAirborne) LandNow(position.Y); }
```

**The gate does not block movement. It leaves the body AIRBORNE**, and gravity then pulls it down
the face. That is what sliding off a steep slope is — there is no separate "slide" code. Refusing
to *climb* is a different mechanism entirely, in the swept solver (§6.6), which flattens a
too-steep normal into a vertical wall.

Also added with the fix: the body half-extent, `|movement| * 1.1547 + 0.48` capped at the ceiling
(`1000d508`-`1000d557`), which gates whether the body counts as being at the ground at all.

Verified by `Tests/Vehicle/SlopeTests.cs`: a 0.3-per-tile rise stays grounded, a 4-per-tile rise
goes airborne, the boundary sits between normal Y 0.53 and 0.47, and `RelaxSlopeGate` makes a steep
face walkable.

### 7.3 `UseSurfaceNormal` — orientation mode 1 is the character's

Found from a user report that holding forward climbed an unwalkable slope.

**Gamecode calls `DummyVehicle_t::UseSurfaceNormal()`** (`N3 100011b7`, which is
`SetOrientationMode(1)`) for character vehicles at `1006eb9d`, `1006ec56` and `1006ee00`. The
orientation update was not ported at all, so the body stayed level.

`FUN_1000c616` mode 1 (`1000c65c`..`1000c92e`):

```
forward = (storedSpeed == 0) ? GetBodyForward() : velocity      // the VELOCITY when moving
this->0xb4 = surfaceNormal                                      // the stored up
if (direction < 0) forward = -forward
bodyRotation(0x80) = LookRotation(forward, up)
```

and `LookRotation` (`FUN_1000f1f2`) is:

```
if (up.y <= 0)  up = (0,1,0)                  // 1000f1fe -- never invert the body
forward.y -= dot(forward, up) / up.y          // 1000f241 -- a Y-ONLY projection onto the plane
normalise
```

That Y-only projection is what tilts a walker's forward along a slope. `Quat.LookRotation` now
carries both details.

**Measured effect** (drive into a rise, 300 frames): rise 0.3 (normal Y 0.958) and rise 1.0 (0.707)
climb and stay grounded, as they should; **rise 1.9 (0.466, just past the gate) now stops and goes
airborne instead of climbing.**

The steep-slope branch is reached by a **level** move, not only a rising one: stock's test is
`!(0 > move.y)` (`1000bc7e`), and a grounded walker always has `move.y == 0` because the caller zeroes
it (§6.6). Squaring the normal up to `(0,1,0)` for the orientation update (`1000d588`) does not stop
that branch being the one that refuses the move.

### 7.4 `Direction` (`+0x90`) and orientation mode 1

With the drive at `-1` on flat ground and `Direction` left at `1`, the body moves one frame
backwards and then oscillates between two positions, velocity flipping sign every frame.

`EnsureSurfaceAlignment` is not involved: it leaves the position untouched and returns true. The
oscillation is the orientation update turning the body to face its own backward velocity, after which
`SteeringReverse` pushes it the other way.

#### The switch at `1000c616` was mis-read

`FUN_1000c616` dispatches on `+0xb0` with a `dec`/`je` chain, so the bodies are in a different order
than the addresses suggest:

| mode | body | notes |
|---|---|---|
| 0 | `1000c940` | |
| **1** | **`1000c88c`** | **the character's** — Gamecode calls `UseSurfaceNormal()` = `SetOrientationMode(1)` at `1006eb9d`, `1006ec56`, `1006ee00` (§7.3) |
| 2 | `1000c78a` | builds a right vector with `cross(forward, s_cReferenceUp)` |
| 3 | `1000c730` | takes the forward from the argument and two vtable slots (`+0x78`, `+0x7c`) |
| 4 | `1000c650` | **falls through** from the dispatch; normalises the velocity to unit length first |

The port had been written against `1000c650` — mode **4** — because that is the block the dispatch
falls into, and the modes were never enumerated. Mode 4 differs in two ways that matter: it
normalises the velocity, and it builds the rotation with `FUN_10009dc5` rather than mode 1's
`FUN_1000f1f2`.

#### What mode 1 actually does

```
forward = (this->0xcc /*speed*/ == 0) ? *GetBodyForward() : this->0x64 /*velocity, NOT normalised*/
this->0xb4 = *arg                                     // the surface normal, stored as the up
if (this->0x90 /*Direction*/ < 0) {                   // 1000c8a7 / 1000c8f1
    this->0x80 = LookRotation(-forward, up)           // the VISIBLE rotation, negated
    FUN_1000a47a(this, &-forward)                     //   +0xc0 = -forward
    this->0x70 = LookRotation( forward, up)           // the un-negated one
} else {
    this->0x80 = LookRotation( forward, up)
    FUN_1000a47a(this, &forward)                      //   +0xc0 = forward
    this->0x70 = this->0x80                           // 1000c8e5, a 16-byte copy
}
return true                                           // 1000c781: changed = 1
```

Three fields that were missing from the field table:

| offset | meaning |
|---|---|
| `0x70..0x7c` | a second rotation, the **un-negated** facing — i.e. the way the body is actually travelling. Written by mode 1, `SetDirection` and the halt; **nothing in `Vehicle.dll` reads it**, so Gamecode's visual/animation code must. |
| `0xc0..0xc8` | the **cached forward**, what `GetBodyForward` returns. Held verbatim, *not* normalised (`1000a4a8`). |
| `0x90` | `Direction`, ±1 — see below. It is not a hint; it is the backpedal state. |

`GetBodyForward` (`1000c5f3`) returns `&this->0xc0` and rebuilds it only when `10009c11(&this->0x80)`
is true — and that function tests **all four** quaternion components against zero, so it is a
lazy-init guard for a never-set rotation, not a dirty flag. The cache is instead kept fresh by every
writer of `+0x80` calling `FUN_1000a47a` immediately afterwards: the standing-still turn
(`1000e7d3` → `1000e7dc`), `SetRelRot` (`1000d13a` → `1000d14c`) and mode 1 itself
(`1000c8d2` → `1000c8da`). Miss that and a turn on the spot is undone on the very next frame, because
mode 1 rebuilds the rotation *from* the cached forward while the body is stopped.

#### `Direction` is set by the movement commands, and it halts

`SetDirection(int)` (`1000a6e4`, ordinal 205):

```
if (d != this->0x90) {
    this->0x70 = this->0x80          // 16 bytes
    FUN_1000a688(this)               // halt if moving:
                                     //   if (speed != 0) { speed = 0; velocity = 0;
                                     //                     0x70 = 0x80; FUN_1000a47a(null);
                                     //                     vtable[+0x68]() }
}
this->0x90 = d
```

Gamecode's three longitudinal command handlers always pair the two:

| command | address | body |
|---|---|---|
| forward | `1006ef8d` | `SetDirection(1)` **then** `if (vtbl[0x8c]()) SetForwardDrive(1.0)` |
| backward | `1006f122` | `if (vtbl[0x8c]()) SetForwardDrive(-1.0)` **then** `SetDirection(-1)` |
| release | `1006f23a` | `if (vtbl[0x8c]()) SetForwardDrive(0.0)`; `Halt()`; `SetDirection(1)` |

Only the **release** handlers call `Halt` (`1006f0ca`/`1006f0d2` and `1006f257`/`1006f25f`). The forward
handler has none of its own and does not need one, because `SetDirection` halts by itself whenever the
value changes — which is what makes a reversal start from rest.

#### And `SetRelRot` closes the loop

`SetRelRot(const Quaternion&)` (`1000d11d`, ordinal 219) re-aims the velocity whenever the body is
turned from outside:

```
if (this->vtbl[0xc]() < 0)  return
this->0x80 = q;  this->0x70 = q;  FUN_1000a47a(this, null)
velocity = *GetBodyForward() * |velocity| * (float)this->0x90        // 1000d15f: fild [ebx+0x90]
```

The `* Direction` is the other half of backpedalling: a body with `Direction == -1` travels *opposite*
to where it points, so turning while backing up swings the path the right way instead of flipping it.

#### Verified

Flat ground, body facing `+X`, 90 frames at 60 Hz:

| input | net movement | facing at the end | speed |
|---|---|---|---|
| drive `+1` | `+7.10` X | `(1, 0)` | 6.0 |
| drive `-1` | `-7.10` X | `(1, 0)` | 6.0 |
| strafe `+1` | `-4.50` Z | `(1, 0)` | 0 (the lateral channel is a position channel) |
| drive `-1` + strafe `+1` | `-6.12` X, `-3.45` Z | `(1, 0)` | 6.0 |

The body backpedals at full speed while still facing forward, and strafing never yaws it.

**One behaviour change worth checking against the live game.** Because `+0xc0` caches the velocity
*verbatim* while the body is moving, `SteeringForward` returns `velocity * maxForce` — a force that
grows with speed and is then clamped to `maxForce` by the integrator's `Truncate`. The acceleration is
therefore **not linear**: from rest the first frame gets full force, and after that speed compounds by
about 20 % a frame (0.200, 0.240, 0.288, 0.346, 0.415 …), reaching the 6 m/s run speed in roughly
0.9 s instead of 0.5 s. That is what `1000a4a8` does — it stores the vector with no normalise — but
it is a feel change, so it is flagged rather than assumed.

### 7.5 `SetRelRot` — an externally-set heading re-aims the velocity

An externally-assigned body rotation takes effect while the body is stopped and is discarded while it
is moving.

That split is the signature of orientation mode 1's two sources. `1000c88c` takes its forward from the
**cached forward** when `+0xcc` (speed) is zero and from the **velocity** when it is not. So a glue
path that only assigns the body rotation works while stopped — the rotation's own setter refreshes the
cache, and mode 1 rebuilds from it — and is silently thrown away while moving, because mode 1 rebuilds
from a velocity nothing changed.

Stock's answer is `Vehicle_t::SetRelRot` (`1000d11d`, §7.4): set the rotation, refresh the cache,
**and re-aim the velocity** as `bodyForward * |velocity| * Direction`. `n3Dynel_t::SetRelRot`
(`N3 1000409b`) is a bare `jmp` to it on `this->0x50`, and Gamecode reaches it from five sites, so this
is the normal way anything hands a dynel a new heading.

Three places in `N3CharVehicle` had the raw assignment; all three are now `SetRelRot`:

| site | what it does | why it mattered |
|---|---|---|
| `ApplyYawDelta` | the camera's right-drag |
| the path follower | turns toward the next waypoint, then sets the drive to 1 | would never have turned while running |
| `Warp` | teleport | only visible with `resetVelocity: false`; stock's teleport is `SetRelPosRot` (`1000e2af`), still unread, so this applies the verified half |

**All view modes were affected equally, and one fix covers them all.** `N3Camera.ApplyOrbit` routes a
right-drag's horizontal delta to `TurnCharacter` → `ApplyYawDelta` with no mode test at all — the
per-mode difference is only in what the *camera* does with the vertical delta. `ApplyYawDelta` is the
sole route from the camera to the character's heading.

**Not ported, and recorded rather than guessed:** `SetRelRot`'s first line is
`if (vtable[+0xc]() < 0) return`, i.e. `LocalitySource_t::GetZone()` (`10002009`, a plain
`return this->0x14`). It drops the rotation for a vehicle that is not placed in a zone. This sim has no
zone id and a simulated character is always in a playfield, so the guard has nothing to test.

## 8. Non-terrain collision — the statel surfaces

Reversed 2026-09-24. The walker collides with terrain only because the port stops at
`n3TilemapSurface_t`'s heightmap. Stock reaches everything else through the **same single
`Surface_i*`**, by composition. Every link below is read from disassembly.

### 8.1 The chain

```
Vehicle_t::EnsureSurfaceAlignment                                    [Vehicle 1000d1aa]
  surface = GetSurface()                                             [Vehicle 1000efb1]
      if (this->parentVehicle(+0x2c))  return parent->vtbl[+0x44]()
      return this->vtbl[+0x40]()                                     // virtual, slot 16

DummyVehicle_t::GetSurface                                           [N3 100011e0]
      if (!this->surfaceCollisionEnabled(+0x15c))  return null
      if (!this->cached(+0x154)) {
          pf = FUN_1000cfae(this->playfieldId(+0x158))                // std::map lookup, 0x1005b7e8
          if (pf) this->cached = pf->0x60                             // n3Playfield_t +0x60
      }
      return this->cached

n3TilemapSurface_t::GetLineIntersection  (slot 4)                    [N3 10018b72]
      cell = this->GetCellSurface(+0xc)                              [N3 1000330b]
      cellHit = cell ? cell->GetLineIntersection(a, b, &h, &n, clip, loc) : false    // 10018c3e
      ... the DDA tile walk over the heightmap (already ported) ...
      // 10018ea0: whichever hit is NEARER to `a` wins; a tile miss returns the cell hit
      // 10018e82: no hit at all writes (-1,-1,-1) into outHit and returns false

CellSurface_t::GetLineIntersection  (slot 4)                         [Collision 1000140a]
      GridSpace_t::IterateCellsAlongLine(a, b, worker,
            cellsX(+0x14), cellsZ(+0x18), worldW(+0x1c), worldD(+0x20))   [Vehicle 10003d7d]

CellRayWorker::DoCell(cellId)  (CellWorker_i slot 0)                 [Collision 1000250f]
      if (hitFound)      return                  // stops at the FIRST cell that yields a hit
      if (cellId == -1)  return
      for (Surface_i* s : *self->GetSurfaceForCell(cellId))   // slot 9, vtbl +0x24
          if (!s) continue
          if (!s->GetLineIntersection(a, b, &h, &n, false, null)) continue   // clip=false, loc=null
          cz = cellId / cellsX;  cx = cellId % cellsX
          if (h.x <  cx      * cellSizeX)  continue     // the hit must lie INSIDE this cell,
          if (h.x > (cx + 1) * cellSizeX)  continue     // or it belongs to a cell further along
          if (h.z <  cz      * cellSizeX)  continue     // NOTE: cellSizeX on BOTH axes (1000259a,
          if (h.z > (cz + 1) * cellSizeX)  continue     //       100025cc) -- stock uses one size
          keep it if it is the nearest so far by |h - a|
```

Because `IterateCellsAlongLine` visits cells in ray order and `hitFound` is never cleared, the first
cell to produce a hit ends the traversal — that is what makes it the nearest hit, and the in-cell
bounds test is what stops a surface that straddles a boundary from winning early.

### 8.2 `CellSurface_t` field map

Ctor `CellSurface_t(int cellsX, int cellsZ, float worldWidth, float worldDepth)`
(`Collision 1000185d`):

| offset | meaning |
|---|---|
| `+0x4` | the cell array — `cellsX * cellsZ` entries, each a `std::vector<const Surface_i*>` |
| `+0x14` / `+0x18` | cellsX / cellsZ |
| `+0x1c` / `+0x20` | total world width / depth |
| `+0x24` / `+0x28` | `worldWidth / cellsX`, `worldDepth / cellsZ` — the cell sizes |
| `+0x2c` | owns-surfaces flag (`SetSurfaceOwnership`) |

`GetCellIdFromPos(p)` (`1000135f`): `cx = (int)(p.x / cellSizeX)`, `cz = (int)(p.z / cellSizeZ)`,
out of range → `-1`, else `cellsX * cz + cx`.

Collision.dll exports the whole API by name, so nothing here needed guessing:
`SetSurfaceForCell(int, Surface_i*)`, `RemoveSurfaceForCell`, `GetSurfaceForCell(int)`,
`GetSurfaceForPos(const Vector3&)`, `CalculateIndexesForRectArea`, `Trace`, `GetCellSizeX/Z`,
`GetWidthOfCellArray`, `SetSurfaceOwnership`.

### 8.3 Where the surfaces come from

`n3Zone_t::LoadSurface(CellSurface_t*)` (`N3 1001a947`):

```
identity = { type = 0xf424d /* 1000013 */, instance = this->0x4 }
res      = FUN_1001b8f0(&identity)          // load the resource
surface  = FUN_1001ae41(res)                // build a Surface_i from it
this->0x8 = surface
if (arg)  arg->SetSurfaceForCell(this->0x c /*cellId*/, surface)
```

It has three callers. Gamecode `100c9bc2` loops every zone and passes **0**, which only builds the
surface and hangs it on the zone. The registering caller is **`N3 10003574`**:

```
pf   = FUN_1000cfae(this->playfieldId(+0x34))
zone = pf->FUN_1000c73d(zoneInstance)
zone->LoadSurface(pf->0x60 /*the tilemap surface*/ ->0xc /*its CellSurface*/)
```

and `SetSurfaceForCell` has **exactly one** call site, inside `LoadSurface` itself (`1001a9b8`).

**`n3Zone_t +0xc` is its instance id** (`SetInstanceID` `10003313`, `GetInstance` `1000330b` — note
the linker folded `GetInstance` and `n3TilemapSurface_t::GetCellSurface` into one body, both being
`mov eax,[ecx+0xc]`). That id is handed straight to `SetSurfaceForCell` as the **cell index**, so
**a zone is a cell, not an object**: one `SurfaceResource` record per populated cell, and the geometry
is AODB record type **1000013**, `SurfaceResource`.

`n3SurfaceResource_t` derives from `KDTreeSurface_c` (`N3 1003d644` is its `KDTreeSurface_c`
vftable), and `KDTreeSurface_c` is header-only: it has RTTI in Collision.dll, N3.dll *and*
Gamecode.dll with no exported methods, so every copy is inlined. Collision.dll's vftable is
`10016394`.

### 8.4 The geometry, and its world-space coordinates

Measured against the live RDB:

- **237,877** `SurfaceResource` records; **all 237,877 decode, 0 failures**, footer byte correct.
  3,501 are empty. 2,397,660 meshes, **42,018,776 triangles** in total.
- The instance id is composite: **`(playfieldId << 16) | cellIndex`**. 328 of the 461 tilemap ids
  appear as the high 16 bits and account for **99.1%** of all records. Every cell can therefore be
  enumerated straight from the RDB index (`RecordTypeToId[1000013]` filtered on the high half) —
  **no placement list needs reversing.** The low half is sparse because only populated cells get a
  record (pf 595: 7,929 records for a 90x110 = 9,900 cell grid, about 80% occupancy).
- **The vertices are already in playfield world coordinates** — which is why `LoadSurface` applies no
  transform anywhere:

| playfield | tilemap | world extent | collision vertex bounds |
|---|---|---|---|
| 595 | 900×1100 @ 4 | 3600 × 4400 m | X −4.9..3612.1, Y −185.2..337.7, Z −9.4..4401.0 |
| 505 | 860×1160 @ 4 | 3440 × 4640 m | X 0.5..3440.7, Y −196.5..246.3, Z 14.1..4642.3 |

Busiest playfields carry 6,000–8,000 zones and ~600k triangles (pf 595: 7,929 zones, 617,183
triangles — about 78 triangles per zone).

### 8.5 The cell grid: 10 tiles per cell, and square

`n3Playfield_t::InitializeSurface` (`N3 1000c64b`) allocates a 24-byte `n3TilemapSurface_t`
(`vtable 1003c97c`) for an outdoor map, a 28-byte `n3RoomSurface_t` (`1003d1a4`) otherwise, and stores
it at `n3Playfield_t +0x60`. It leaves the cell surface pointer **null**; the grid is built in
`n3TilemapSurface_t::Init` (`N3 1001879d`):

```
this->4    = tilemap
this->8    = playfield
this->0x10 = (float) playfield->vtbl[+0x30]()      // world width  = (int)mapScale * tileWidth
this->0x14 = (float) playfield->vtbl[+0x34]()      // world depth  = (int)mapScale * tileHeight
this->0xc  = new CellSurface_t(
                 cellsX = tileWidth  / playfield->0x50,     // FUN_1000c50d
                 cellsZ = tileHeight / playfield->0x50,     // FUN_1000c520
                 worldWidth, worldDepth)
this->0xc->ownsSurfaces(+0x2c) = 0                 // the zones own them, not the grid
```

**`playfield->0x50` is 10.** Two independent lines of evidence:

- It is the largest divisor that keeps every observed cell index inside `cellsX * cellsZ` across all
  328 playfields (11 overflows 298 of them; 8, 9 and 10 all fit, so 10 is the tightest bound).
- It is the only divisor that makes the cells **exactly square**, which stock *requires*:
  `IterateCellsAlongLine` divides both axes by `worldWidth / cellsX` and never reads its
  `worldDepth` argument at all (`[ebp+0x20]` is unreferenced), and the per-cell worker uses
  `cellSizeX` for the Z bounds test too (`1000259a`, `100025cc`). With 10:
  pf 595 `3600/90 = 4400/110 = 40`, pf 505 `3440/86 = 4640/116 = 40`, pf 695 `4000/100 = 40`.
  With 8 or 9 the two axes disagree.

So for a scale-4 map the collision grid is **40 m cells**.

Note the latent inconsistency in stock: `GetCellIdFromPos` divides Z by `cellSizeZ` (`+0x28`) while the
ray walk and the in-cell test use `cellSizeX`. They agree only because the grid is square. Do not
"fix" it.

### 8.6 Geometry is duplicated per cell, and the in-cell test

Measured on pf 595: a record's XZ span is **51.3 m median, 83.4 m at p90** against a 40 m cell, and
only 1,508 of 7,906 records fit wholly inside the cell their index names. So a cell's record is not
clipped to the cell — it holds whole objects that overhang.

That is exactly why the per-cell worker rejects a hit outside the cell's own XZ bounds
(`1000259a`..`100025ec`). An object straddling a boundary appears in **every** cell it touches, and the
bounds test makes precisely one of them report each hit — so the ray walk, which stops at the first
cell that yields a hit, still returns the nearest one. Port the bounds test, or objects get hit from
the wrong cell and the "nearest" guarantee breaks.

### 8.7 `CalculateClosestPoint` also goes through the cells

`n3TilemapSurface_t::CalculateClosestPoint` (slot 1, `N3 10018f0c`) calls
`CellSurface_t::GetCellIdFromPos` (`10018f2c`), then `GetSurfaceForCell` (slot 9, `10018fcb`), then
that surface's own `CalculateClosestPoint` (`10019007`) — so a statel floor holds the walker up through
the tripod as well as through the ray. The port's `TilemapSurface.CalculateClosestPoint` is missing
this too. **`10018f0c` is not fully read.**

`CellSurface_t::CalculateClosestPoint` (`Collision 10001729`) is deliberately cheap: it takes
`GetSurfaceForPos(p)` and calls the **first non-null surface in that cell, then returns** — it does not
compare distances across the cell's surfaces. `GetSphereIntersection` (`10001764`) instead loops until
one returns true.

### 8.8 `GridSpace_t::IterateCellsAlongLine` — `Vehicle 10003d7d`

The same Amanatides–Woo walk as the tile DDA, over cells:

```
cellSize = worldWidth / cellsX                  // worldDepth is NEVER USED
ax = a.x/cellSize;  az = a.z/cellSize;  bx = b.x/cellSize;  bz = b.z/cellSize
cellX = (int)ax;  cellZ = (int)az               // ftol, i.e. TRUNCATE toward zero, not floor
endX  = (int)bx;  endZ  = (int)bz
for each axis:  if (delta > 0) { step = +1; tDelta =  100/delta; tMax = (floor(v)+1-v) * tDelta }
                else           { step = -1; tDelta = -100/delta; tMax = (v-floor(v))   * tDelta }
budget = |endX - cellX| + |endZ - cellZ|        // Manhattan; the loop runs budget+1 times
row = cellZ * cellsX;  rowStep = stepZ * cellsX
do {
    if (cellX < 0 || cellZ < 0 || cellX >= cellsX || cellZ >= cellsZ)  return   // RETURN, not skip
    worker.DoCell(row + cellX)
    if (tMaxZ <= tMaxX) { tMaxZ += tDeltaZ; cellZ += stepZ; row += rowStep }
    else                { tMaxX += tDeltaX; cellX += stepX }
} while (--budget >= 0)
```

The `100.0` (`100124d8` / `100124d0` hold +/-100.0 as doubles) is a shared scale on both axes, so it
cannot change which axis steps first; keeping it is faithful and harmless. `10010946` is an imported
`floor`, `10010a50` an imported float-to-int truncation.

## 9. Open questions

1. **`FUN_10070a2f` movement-state values.** Partially recovered from `1006f9eb` §4.5: states
   **2, 3, 4, 5, 7** all have speed branches, and `7 = Fly` (it disables falling, and `Camera.md`
   already had it). States **1, 8, 9** refuse longitudinal steering (`10071537`). The names behind
   the numbers are still not confirmed — do not assume they match the port's existing
   `MovementState` enum until checked.
2. **`FUN_10070a37`** — the direction selector used by state 3 (`1` vs `2`, giving the forward and
   backward speed curves). Probably `Vehicle_t`'s `+0x90` direction, not confirmed.
3. **`FUN_1006f2fc`** — the run-speed stat lookup. Whether the health penalty lives inside it is not
   yet known.
4. **`SetStrafe`'s scale factor** — the virtual at slot 34 (`1006fddd`, which sits immediately after
   `1006f9eb`) and the helper `100718f8`.
9. **The liquid/collision-profile switch** (`EnsureSurfaceAlignment` step 10, `this->0xfc` 0-4) is
   read but not yet written up per branch. Profile 0 is the character's; 4 is the camera's.
6. **Where the four axes are written from.** The input verbs (`N3Msg_MovementChanged` `10018bc4`,
   the turn→strafe remap at `10018d1a`, action `0x2b`) are recorded in `Camera.md` §5.9; they have
   not yet been traced through to the four setters. The 27 callers of `1006f9eb` (`1006db3d`,
   `1006dc33`, `1006dcdb`, `1006dd6a`, `1006de91`, `1006dfa6`, `1006e0ed`, …) are the movement verb
   handlers and are the place to look.
7. *(closed — the NPC factory at `1005796f` has the same shape and the same visible constants as the
   player factory, and `NPCVehicle_t`'s steering is read in §3.2.)*
8. **`DummyVehicle_t`** — the N3 layer between `Vehicle_t` and `CharVehicle_t`: `SetSurface`,
   `GetSurface`, `UseSurfaceNormal`, `EnableSurfaceCollision` / `DisableSurfaceCollision`
   (`1000119e`, `100011e0`, `100011b7`, `100011c9`, `100011d1`). Not yet read.
