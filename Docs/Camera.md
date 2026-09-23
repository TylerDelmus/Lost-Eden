# Camera and movement: reverse-engineering notes and port guide

This is the working reference for porting Anarchy Online's camera and movement into Lost Eden.
Read it before touching `Assets/Scripts/Vehicle/` or `Assets/Scripts/Controllers/`.

---

## 1. Ground rules

Same as `Effects.md` §1, and for the same reasons:

- **Stock is the only truth.** Behaviour comes from the shipped binaries, not from what feels right.
- **Cite addresses.** Every recovered rule names the function or instruction it came from.
- **Put stock maths in a Unity-free class** so `Tests/Vehicle` can lock it down with plain xUnit.
  Unity types appear only in the thin `MonoBehaviour` that binds a sim to a transform.
- **Do not oversell stock.** Stock sub-steps its integrator, but that bounds frame-rate divergence
  rather than removing it (§4.1, with measurements). The standing project rule that the port must
  behave the same at any fps is *stricter than stock*, so meeting it means deviating deliberately —
  and saying so here — not claiming stock already did it.

---

## 2. Where things are

### Stock client

The camera is **not** in `Gamecode.dll`. It is split across three DLLs:

| DLL | Holds |
|---|---|
| `Vehicle.dll` (100 KB) | `Vehicle_t` — the physics/steering core, the integrator, the steering behaviours |
| `N3.dll` | `n3Camera_t` (the camera dynel and its input surface), `CameraVehicle_t` and its two subclasses, `CameraAttractor_i` |
| `Gamecode.dll` | `CharVehicle_t` → `PlayerVehicle_t` / `NPCVehicle_t` — the character side |

All three export **mangled C++ symbols**, so full method names and signatures are recoverable.
All three are in the Ghidra project (`/Vehicle.dll`, `/N3.dll`, `/Gamecode.dll`).

Scratchpad helpers (session-local, rebuild if missing): `aolib.py` (PE loader, RTTI),
`rtti.py <dll> <Class>` (vftables), `hier.py <dll> <Class>` (RTTI base list),
`vtdiff.py <dll> <Base> <Derived>...` (slot-by-slot override diff), `exp.py`/`imp.py <dll> [regex]`.

### The port

- `Assets/Scripts/Vehicle/` — the recovered model, Unity-free.
- `Tests/Vehicle/` — xUnit tests over it.

---

## 3. The architecture

This is the single most important finding.

```
Vehicle_t                        [Vehicle.dll]  physics + steering, abstract
└── DummyVehicle_t               [N3.dll]       adds surface/cell binding
    ├── CameraVehicle_t          [N3.dll]           ├── CharVehicle_t      [Gamecode.dll]
    │   ├── CameraVehicleFirstPerson_t             │   ├── PlayerVehicle_t
    │   └── CameraVehicleFixedThird_t              │   └── NPCVehicle_t
```

(RTTI base lists via `hier.py`; `CameraVehicle_t` CHD `10042e98`, `CharVehicle_t` CHD `1017603c`.)

**The camera and the player are siblings.** They are the same kind of object, moved by the same
integrator, differing only in which virtual steering hooks they override. The camera is a physical
body that flies through the world under force, collides with it, and steers toward where it wants to
be. It is not a transform parented to the player.

Separately, `n3Camera_t` is itself an `n3Dynel_t` **and** a `VehicleBody_i` — the camera is a world
entity, and it is the *body* that `CameraVehicle_t` drives:

```
n3Dynel_t, VehicleBody_i, n3DynelEventSource_t, DbObject_t, ...
└── n3Camera_t                   vftable N3 1003e3c4
```

So the split is: `n3Camera_t` takes input and owns the view; `CameraVehicle_t` decides where the
camera should be and steers it there; `Vehicle_t` integrates the result against the world.

### 3.1 Which vehicle is the AO camera?

`CameraVehicleFixedThird_t` is the normal over-the-shoulder camera; `CameraVehicleFirstPerson_t` is
the first-person one. `n3Camera_t::ToggleCameraView` (`10021 90e`) swaps them, and
`n3Camera_t::IsFirstPerson` (`10020071`) reports which is live.

---

## 4. `Vehicle_t` — the integrator

`Vehicle_t::Run(float dt)` (`Vehicle.dll 1000e849`, export ordinal 201) is the tick. It dispatches
three ways:

1. A pending relative move is queued at `+0x144` → apply it straight to the position and re-seat on
   the surface. (`SetRelPos`/`SetRelPosIgnoreCollision`.)
2. A functional path is attached at `+0x108` → evaluate the path, no physics.
3. Otherwise → **the physics step**, `FUN_1000e3d3` (`1000e3d3`), described below.

Then `UpdateListeners()` regardless.

### 4.1 The sub-stepping loop

`FUN_1000e3d3` does **not** integrate the whole frame at once:

```
if (dt > 4.0)  return;          // a frame longer than 4 s is dropped entirely
if (dt <= 0.0) return;
elapsed = 0
do {
    step = min(dt - elapsed, this->maxSubStep)   // +0x104
    Vehicle_t::s_vDeltaTimeNow = step            // global, read by sub-systems
    ...one integration step...
    if (!EnsureSurfaceAlignment(prevPos, false)) break
    elapsed += step
} while (elapsed < dt);
```

`s_vDeltaTimeNow` (`Vehicle.dll 1001a148`, a private static) is how the rest of the engine sees the
current step. The ceiling at `+0x104` is **per-vehicle**:

| Vehicle | Cap | Set at |
|---|---|---|
| `Vehicle_t` (base, so `CharVehicle_t` too) | 0.4 s | ctor `1000ce2f` |
| `CameraVehicle_t` | **0.05 s** | ctor `1001d54f` |

**Be careful what this buys.** It is tempting to call this "frame-rate independence"; it is not.
The integrator is plain semi-implicit Euler, so the step size still changes the trajectory — the cap
only bounds how bad that gets. Measured on a constant 500 N / 50 kg body over 6 s
(`scratchpad/probe.py`):

| Cap | 10 fps | 30 fps | 60 fps | 144 fps | spread |
|---|---|---|---|---|---|
| 0.4 s (characters) | 183.00 | 181.00 | 180.50 | 180.21 | 2.79 m (1.55 %) |
| 0.05 s (camera) | 181.50 | 181.00 | 180.50 | 180.21 | 1.29 m (0.72 %) |

At 30 fps and above the 0.4 s cap **never binds at all** — a character vehicle at normal frame rates
integrates exactly once per frame, the same as a naive implementation would. The cap earns its keep
on hitches: with a 0.5 s frame, the 0.4 s cap diverges 4.85 m from the 60 fps result, the camera's
0.05 s cap only 0.50 m.

So the camera is integrated at **at least 20 Hz** and characters at **at least 2.5 Hz**, and the real
purpose of both is to stop a loading spike from flinging a body across the map. If the port wants
motion that genuinely does not vary with frame rate it has to go further than stock did — lower the
cap, which is a deliberate, documented deviation, not a port of stock behaviour.

### 4.2 One integration step

Field offsets are `Vehicle_t` instance offsets, confirmed against the constructor at `1000ce2f`.

| Offset | Meaning | Default |
|---|---|---|
| `0x34` | mass | 50.0 |
| `0x38` | max force | 2.0 |
| `0x3c` | max velocity | 2.0 |
| `0x40` | **slowing distance**, written by `SetBrakeDistance` (`1000a166`). `SteeringArrive` divides by it | 0.1 |
| `0x44` | (unnamed, set 1.0) | 1.0 |
| `0x48` | **halt radius** — `SteeringArrive` stops inside it | 0 |
| `0x4c` | body radius, written by `SetRadius` (`1000a159`); also the camera's occlusion probe offset. `EnsureSurfaceAlignment` rewrites it from the surface (`1000d1f7`) | 0.01 |
| `0x50` | falling enabled (`EnableFalling` `1000c394`) | 0 |
| `0x51` | **surface hug** (`EnableSurfaceHug` `10009f49`: `mov byte [ecx+0x51], 1`) | 1 |
| `0x52` | airborne now | 0 |
| `0x90` | direction (`GetDir` `10009fb1`) | 1 |
| `0xb0` | orientation mode (`SetOrientationMode` `1000a18a`) | 0 |
| `0xfc` | collision profile; 4 = camera (see §4.5) | 0 |
| `0x54` | vertical velocity (gravity accumulator) | 0 |
| `0x58..0x60` | global position | 0 |
| `0x64..0x6c` | velocity | 0 |
| `0x80..0x8c` | body rotation quaternion | identity |
| `0x94..0x9c` | steering force accumulator | 0 |
| `0xac` | path time accumulator | 0 |
| `0xcc` | current speed, `|velocity|` | 0 |
| `0xd0..0xd8` | previous position | 0 |
| `0x104` | max sub-step | 0.4 |
| `0x108` | functional path | null |
| `0x10c` | floor / death plane Y | -9999.0 |
| `0x144..0x14c` | pending relative move | 0 |

**Watch these three.** `SetRadius` writes `0x4c` and `SetBrakeDistance` writes `0x40`, which is not
what the names suggest. `SteeringArrive`'s slow-down divisor is `0x40` and its stop test is `0x48`.
Getting `0x40` and `0x48` the wrong way round leaves the divisor at a stale 0.1, so the desired
speed saturates at `maxVel` at every distance and the camera charges its goal and overshoots — it
shows up as a wobble while walking.

Statics (`Vehicle.dll`): `s_vGravityAccel` `1001938c` = **-20.0**;
`s_cReferenceForward` `10019398` = **(0, 0, 1)**; `s_cReferenceUp` `100193a4` = **(0, 1, 0)**;
`s_bEnsureSurfaceAlignmentOnlyOnce` `10019390` = **true**.

The step, in order:

1. **Longitudinal steering** — virtual slot `0x4c` writes a force into `+0x94`.
   `Halt` → zero the force and call `Halt()`. Anything other than `Force` → zero the force.
2. **Gravity** — if airborne (`0x52`): `vy += -20 * step`. Else if **surface hug** (`0x51`) is on:
   `vy = 0` and `velocity.y = 0` (`1000e4b7`). Surface hug is what pins a walker to the ground; the
   camera turns it off, which is how it is free to move vertically.
3. **Force → velocity**, only when the steering said `Force` *or* the vehicle is airborne:
   ```
   f = truncate(steerForce, maxForce)      // clamp length, do not normalise
   velocity += (f * step) / mass
   vy = clamp(vy, -50, +50)                // MaxFallSpeed, 1000e54a
   speed = truncate(velocity, maxVel)      // clamp length, returns the new length
   ```
   `truncate(v, max)` is `FUN_1000f12a`: scales `v` down only if `|v| > max`, and returns the
   resulting length.
4. `moving = speed > 0.001`.
5. **Lateral steering** — virtual slot `0x50`. `Halt` → zero.
   `Lateral` → if not moving, truncate to `maxVel`; if moving, scale **both** the velocity copy and
   the lateral vector so that `|velocity + lateral| == speed` — strafing redirects, it never adds
   speed. Then `position += lateral * step`.
6. **Translate** — if moving or `vy != 0`: `position += velocity * step`, then
   `position.y += vy * step`.
7. **Turn steering** — virtual slot `0x54`. `Halt` → zero. `Turn` → with `len = |turn|`, if
   `len > 0.0001`: axis `= turn / len`, angle `= len * step`; when `velocity.x == 0 && velocity.z == 0`
   rotate the **body quaternion**, otherwise rotate the **velocity vector**. (`1000e77d`.)
8. `EnsureSurfaceAlignment(prevPos, false)`; a false return breaks the sub-step loop.

**One trap.** The translate step gates on the **stored** speed `+0xcc`, which is only written by the
force path, by `Halt`, by `DisableFalling` and by `SetVel` (`1000a4b1`). Assigning the velocity
vector without going through `SetVel` leaves the stored speed stale and the vehicle does not move.

### 4.3 `SteeringResult_e`

Recovered from the integrator's dispatch and the steering behaviours:

| Value | Name | Effect |
|---|---|---|
| 0 | `None` | no steering this step (also the "force blew up" escape, `1000ac6e`) |
| 1 | `Halt` | zero the channel; on the longitudinal channel also `Halt()` |
| 2 | `Force` | the vector is a force, integrate it |
| 3 | `Lateral` | the vector is a direct positional velocity |
| 4 | `Turn` | the vector is an axis, its length the angular rate |

### 4.4 `SteeringArrive` — the behaviour the camera uses

`Vehicle_t::SteeringArrive(const Vector3&, Vector3&, float)` (`1000ab28`, ordinal 223):

```
if (brakeDistance == 0) brakeDistance = this->0x48
toTarget = target - position
d2 = |toTarget|^2
if (d2 < brakeDistance^2 || d2 < 0.01)  return SteeringHalt(out)
d = sqrt(d2)
speed = min(maxVel, (d / arrivalRadius) * maxVel)     // 0x40
desired = toTarget * (speed / d)
out = (desired - velocity) * mass * 4.0               // <- a force
return |out| <= 1e7 ? Force : None
```

The `* mass * 4.0` is the whole acceleration model: the vehicle tries to reach the desired velocity
in **0.25 s**, and that acceleration is turned into a force by multiplying by mass. The force is then
clamped to `maxForce` by the integrator, which is what actually limits responsiveness.

`SteeringSeek` (`1000a87c`), `SteeringFlee` (`1000ad86`), `SteeringHalt` (`1000a2de`),
`SteeringForward` (`1000ca73`), `SteeringReverse` (`1000cab3`), `SteeringDirArrive` (`1000ac8c`)
are the rest of the set; only `Arrive`/`Halt` are needed for the camera's first pass.

### 4.5 `EnsureSurfaceAlignment` — read, not ported

`Vehicle_t::EnsureSurfaceAlignment` (`1000d1aa`, ordinal 110) is the ground clamp and collision
gate, and at 4206 bytes it is the largest function in `Vehicle.dll`. It is **deliberately not
ported**: its interior queries a `Surface_i` the port does not have, and Lost Eden already has its
own terrain and collision (`PlayfieldLocality`, mesh colliders). Porting it faithfully would mean
porting AO's surface representation too, which buys nothing for the camera.

What was recovered and *is* worth honouring at the seam:

- **It early-outs `true`** when `+0x30` is null or `GetSurface()` (vftable `+0x40`, or `+0x44` on a
  parent vehicle) returns null. `CameraVehicleFirstPerson_t::GetSurface` returns 0 — the body is
  literally a shared `return 0` stub the linker folded with `n3VisualDynel_t::GetImpactAnim`
  (`10007572`) — so **first person does no surface alignment at all**.
- `CameraVehicle_t::GetSurface` (`1001d6cb`) hands back the playfield's surface, gated on
  `DummyVehicle_t`'s surface-collision flag `+0x15c` (`EnableSurfaceCollision` `100011c9`,
  `DisableSurfaceCollision` `100011d1`). Nothing in N3 disables it, so **third person does align
  against terrain**. Note the surface is the *playfield* — buildings and props are not handled here,
  they go through the sensors and the vetoes instead.
- A **retry loop**: up to 9 attempts, each backing the move off by 10 % along the movement vector,
  while the collision test keeps reporting blocked.
- **Step height** is `+0xfc == 4 ? 0.25 : 0.01` (`1000d4d3`). Only `CameraVehicle_t`'s ctor writes 4
  (`1001d555`), and Gamecode never writes the field — so the camera may step up 0.25 m of terrain
  and characters only 0.01 m.
- A body half-extent of **0.48** and a slope term of **1.1547005** — that is `2/√3`, i.e. `1/cos 30°`,
  so a **30° slope limit** (`1000d54a`).
- The probe is offset ±0.4 vertically, and `+0x10c` (default -9999) is the floor plane.

---

## 5. `CameraVehicle_t` — how the camera decides where to be

Exports via `exp.py n3 CameraVehicle`. The shape:

- `GetLookTargetPos()` (`1001d890`) is what the camera orbits:
  `targetPosition(+0x160) + rotate(eyeLocalOffset(+0x180), targetRotation(+0x16c))`.
- `Update()` (`1001e54f`) measures the distance from the look target to the camera and stores it at
  `+0x198`, **floored at 0.9**. **It is not a per-frame tick despite the name** — see §5.2.
- `CalcSteering(Vector3&)` (`1001e797`, virtual) is the brain, in priority order:
  1. an attractor is bound at `+0x1a0` → delegate to it (`CameraAttractor_i` slot 1);
  2. a zoom rate is pending at `+0x1b8` → `ZoomSteer` (`1001db64`);
  3. `DoDirectControl` (`1001d6e5`) — user steering wins if it returns non-`None`;
  4. otherwise compute the desired point and `SteeringCamArrive` to it.
- `VetoForward(Vector3&)` (`1001df85`) and `VetoUpAlignment(Vector3&)` (`1001dfde`) are **constraint
  filters** applied to a candidate direction — this is how the camera refuses to go through
  geometry rather than being pushed out of it afterwards.
- `UpdateSensors()` (`1001e71f`) / `CalculateSensorSteerDir()` (`1001d955`) probe the world;
  `LineOfSight` (`1001d69a`) and `CanSeeFlexedPos` (`1001d8c6`) are the queries.
- `UpdateMotionConstraints(float)` (`1001e602`).
- Input verbs are `Forward/Reverse/Turn/Strafe(float)` (`1001d73e`/`1001d74e`/`1001d760`/`1001d770`)
  and `MoveUp(float)` (`1001d780`) — the same verb set a character vehicle takes.

`CameraVehicleFixedThird_t` adds `GetOptimalPos` (`1001effd`), `RecalcOptimalPos` (`1001f371`),
`DecideSnap` (`1001f537`) and `UpdateHeadingToPos` (`1001f660`), and overrides `CalcSteering`
(`1001f752`), `VetoForward` (`1001f235`), `SetEyeTargetLocalPos` (`1001f085`), `SetPrefs`
(`1001f004`), `ReposCutOnAxis` (`1001f64d`) and `ForcedUpdate` (`1001f7c9`).

`CameraVehicleFixedThird_t::VetoForward` is worth reading as the model for the rest: below a
follow distance of **0.9** it blends the base veto result against the stored reference forward at
`+0x16c` by `2 * (0.9 - distance)`, so the camera eases rather than snaps as it is crowded.

### 5.1 Occlusion is a binary search, not a push-out

`CameraVehicleFixedThird_t::RecalcOptimalPos` (`1001f371`) is the single most important function in
the camera, and it is nothing like the usual sphere-cast-and-shove:

```
dir        = stayBehind(+0x204) ? rotate(preferredDir(+0x1f8), targetRot(+0x16c)) : preferredDir
lookTarget = GetLookTargetPos()
optimal(+0x1ec) = lookTarget + dir * followDistance(+0x198)

if (GetSurface() == null) return            // first person stops here

if (LineOfSight(lookTarget, optimal + dir * nearProbe(+0x4c))) {
    adjusted(+0x208) = (0,0,0)              // sentinel: nothing in the way
} else {
    lo = 0.01; hi = 0.95; i = 0
    do {
        i++;  t = (lo + hi) * 0.5
        adjusted = optimal * t + lookTarget * (1 - t)
        if (LineOfSight(lookTarget, adjusted + dir * nearProbe))  lo = t   // visible, go further
        else                                                      hi = t   // blocked, pull in
    } while (i < 20 && hi - lo > 0.001);
}
```

So the camera searches the segment from the character's head out to the ideal spot for the furthest
fraction that can still see the head, and steers there. That is why AO's camera **slides** in when
you back into a wall instead of popping around it. A zero `+0x208` is the sentinel for "unoccluded".

One quirk kept faithfully: the loop stores the **last candidate tested**, which is not necessarily
the last one that was visible. With a 0.001 bracket it rarely shows.

`CalcSteering` (`1001f752`) is then short:

```
RecalcOptimalPos()
target = isZero(adjusted) ? optimal : adjusted
if (zoomRate(+0x1b8) != 0)  return ZoomSteer(zoomRate, out)
if (frozen(+0x214))         return None
return SteeringCamArrive(target, out, 0.01)
```

### 5.2 The per-frame driver, and what `Update()` is really for

`FUN_10022345` (reached from `n3Camera_t`'s vftable at `1003e3ec`) is the actual per-frame tick:

```
dt = n3Engine_t::instance->0x68
SetTwoShotMode(cam, false)
SetMass(cam, 20.0)
SetBrakeDistance(cam, 8.0)      // dead store, overwritten below
SetMaxForce(cam, 1800.0)        // dead store, overwritten below
baseSpeed = controlDynel ? min(characterSpeed * 6.0, 16.0) : 16.0
UpdateMotionConstraints(cam, baseSpeed)
... n3Dynel_t::Run(camera) ...
```

**The camera's speed is derived from the character's**: `min(characterSpeed * 6, 16)`, so it is lazy
when you stand still and quick when you sprint, and `UpdateMotionConstraints` floors it at 2.

`CameraVehicle_t::Update()` is called at `10022445` — only inside the branch that has just
teleported the camera with `SetRelPosIgnoreCollision`, followed by `ForcedUpdate(false)`. It is a
**re-sync** of the stored distance to wherever the camera was just put. Calling it every frame feeds
the camera's own distance back into the goal that determines that distance, and the camera walks
itself onto the character's head. `Tests/Vehicle` pins that.

### 5.3 `UpdateMotionConstraints` — where the feel comes from

`CameraVehicle_t::UpdateMotionConstraints(float)` (`1001e602`) re-derives the limits every tick:

```
d = |GetLookTargetPos() - GetGlobalPos()|            // the CAMERA's own position
if (d > 6.0)  speed = min((sqrt(d - 5.0) + 1.0) * speed, 80.0)   // catch up
speed = max(speed, 2.0)
SetMaxVel(speed)
SetMaxForce(mass * speed / 0.3)          // reach top speed in 0.3 s
SetBrakeDistance(speed * 0.3)
```

**`d` is the camera's distance from what it is looking at**, not the character's. Stock reads it
through vftable `+0x10`, which is `Vehicle_t::GetGlobalPos` (`1001e60d`). This port first measured
the character's body instead — which yields the constant eye-offset length, quietly disables the
whole catch-up curve, and leaves the camera trailing 22 m behind a character walking at 4 m/s. The
unit tests did not catch it; driving it in the editor did.

The construction-time values from the `FixedThird` factory (`1001faa1`) are mass **20**, max force
**600**, max velocity **15**, radius **0.7**, brake distance **15**, plus `SetDirection(1)`,
**`DisableFalling()`** and **`DisableSurfaceHug()`** — the camera has no gravity and does not stick
to the ground.

### 5.4 `SteeringCamArrive`

`CameraVehicle_t::SteeringCamArrive` (`1001dc46`) is `SteeringArrive` plus two camera-specific
guards:

- a **hitch guard**: a running `avg = avg/2 + step/2` of the step size, and an outright
  `SteeringHalt` if `avg * 10 < step`. It measures the *sub-step*, so with the camera's 0.05 s cap
  an ordinary frame-rate drop never trips it — only a collapse from a high baseline does.
- a **swing-around**: when the camera is closer to the head than to its goal, the goal is over 1 m
  away, the two planar directions are nearly collinear (`|sin| < 0.4`) and point the same way, the
  goal is pushed along the ground-plane perpendicular so the camera arcs around the character
  instead of straight through them.

### 5.5 Orbit: `UpdateHeadingToPos`

`CameraVehicleFixedThird_t::UpdateHeadingToPos(const Vector3&, bool)` (`1001f660`) is the orbit
primitive — "put the camera at this world position":

```
dir = pos - GetLookTargetPos()
if (stayBehind)  dir = conjugate(targetRot(+0x16c)) * dir     // into the character's frame
preferredDir(+0x1f8) = dir
if (recalcDistance)  followDistance(+0x198) = |dir|
preferredDir /= followDistance
adjusted(+0x208) = 0
RecalcOptimalPos(); DecideSnap()
```

Note it divides by the **follow distance**, not by the direction's own length — so with
`recalcDistance` false, a position at a different range rescales the direction rather than
renormalising it (`1001f6a3`).

Stock's third-person orbit driver is `FUN_1002118c` (reached from `MouseCameraControl` when the
camera mode `+0x1ec` is non-zero, and from the frame driver). Two details in it are load-bearing and
both caused visible bugs when I got them wrong:

**It rotates the camera's *current* offset**, not the preferred direction at the preferred distance
(`1002119f`: `offset = position - GetLookTargetPos()`), clamping only at 25 m (`1002151a`). In
Rubber the camera lags behind its ideal spot while you walk, so rebuilding the position at
`FollowDistance` yanks it ~1.1 m closer the moment you touch the mouse, and the steering then drifts
it back out — orbit and the rubber band appearing to fight each other.

**What happens after the move depends on the mode** (`10021560` onward):

```
SetRelPos(newPos)
if (mode == 3)                        // Lock
    UpdateHeadingToPos(newPos, false); ForcedUpdate(false)
else if (Speed < 0.02)                // 1002158d, the double at 1003e2e0
    Update(); ForcedUpdate(false)     // resync the distance, then re-seat
// else: nothing at all — the goal is left alone
```

So in Trail and Rubber, dragging **while the camera is moving** does nothing but move it; the goal
is untouched and the steering pulls it back. Re-seating unconditionally is wrong twice over, because
`UpdateHeadingToPos` divides by `FollowDistance` rather than the offset's own length (`1001f6a3`):
a lagging camera writes a preferred direction longer than one, the goal moves out to wherever the
camera currently is, and every further drag ratchets it outward again.

Measured after fixing both: walking at 4 m/s the camera sits 5.83 m out, a drag leaves it at 5.83,
and eight repeated drags hold 5.83. Stopped, it holds 4.84 across drags.

`n3Camera_t::MouseCameraControl(dx, dy)` (`10021689`) accumulates yaw at `+0x158` and pitch at
`+0x15c`, scaled by per-axis sensitivities at `+0x22c` / `+0x230`, and **clamps pitch to
±1.553343 rad = ±89.0°**.

`+0x230` defaults to **-1.0** (`10021cdb`), and the helper at `100200c3` takes its `fabs` and flips
the sign from a flag — so **the sign is invert-Y and the magnitude is the sensitivity**. `+0x22c` is
set symmetrically alongside `+0x224`/`+0x208` in the same constructor block, so stock's two axes have
equal magnitude.

#### Where the sensitivity slider actually lives

Traced end to end:

```
[input layer, above Gamecode — not in these DLLs]
  -> n3EngineClientAnarchy_t::N3Msg_CameraMouseLookMovement(dx, dy)   Gamecode 10015ff9
     (a bare pass-through; it also sets the mouse-look-active flag DAT_102e3588)
  -> n3Camera_t::MouseCameraControl(dx, dy)                           N3 10021689
       mode 0 : yaw(+0x158) += (+0x22c) * dx
                pitch(+0x15c) += (+0x230) * dy,  clamped to +/-1.553343 rad
                SetRotAngles(pitch, yaw)
       mode !=0 (10021722):
                FUN_1002118c( dx , (+0x230) * dy , 0.0 )
```

`FUN_1002118c` takes those as **radians**. So in third person there is **no camera-side yaw
sensitivity at all** — `dx` is passed through untouched. `+0x22c` applies only in first person, and
`MouseTurnSensitivity` (a `DistributedValue`, read at `100229ff`) scales only the **keyboard**
rotate rates of +/-0.02 rad, never the mouse.

The slider therefore lives entirely in the input layer above Gamecode, which is outside `N3.dll`,
`Gamecode.dll` and `Vehicle.dll`. The port's `_lookSensitivity` fills exactly that role; there is no
stock constant it should have been derived from.

The default of **5 on both axes** (0.5 deg per mouse pixel after `InputController`'s 0.05 x 2
pre-scale, about 0.0087 rad/pixel) was matched against the real client at Sensitivity 100. The two
axes are equal for a recovered reason, not by guess: third-person yaw is unscaled and pitch is
multiplied by `+0x230`, whose magnitude is 1.0.

An earlier default of 0.3 with a 1.25:1 axis ratio was **invented** from an FPS convention
(360 degrees per 40 cm at 800 DPI); it was roughly 16x too slow and the ratio had no basis.

**Invert-Y** is `+0x230`'s sign: the helper at `100200c3` takes `fabs` of it and conditionally
negates. Not exposed in the port yet.


#### The pitch pole

Stock does **not** clamp third-person pitch to an angle. `FUN_1002118c` adds the pitch delta to the
direction's Y and, if `|result| > 0.9999`, **zeroes the delta** and applies no pitch at all —
refusing the movement rather than limiting it.

That distinction is load-bearing. An angle clamp (which this port had) lets the direction cross
vertical, and once it does, the pitch axis `cross(up, dir)` flips sign and every subsequent input
inverts; land exactly on the pole and the axis degenerates, so **both pitch and yaw become no-ops**
and the camera locks with no way out. Measured with the clamp: driving up reached 89.583 degrees
having wrapped to the far side, pitching "back" moved it a further +0.25, and a 15 degree yaw came
out as -165. With the refusal: stops at 87.4, azimuth unchanged, pitch back works, yaw exact, and
500 mixed steps never exceed |y| = 0.9996.

First person is different and *does* clamp, to +/-1.553343 rad (`100216cf`) — safe there because it
keeps explicit yaw/pitch accumulators instead of re-deriving the angle from a direction vector.

### 5.6 Zoom

Zoom is a **distance-remaining state machine**, not a rate the user holds.

`CameraVehicle_t::ZoomSteer(rate, out)` (`1001db64`):

```
toLook = GetLookTargetPos() - position
d2     = |toLook|^2
if ((d2 > 0.49 && rate > 0) || (d2 < 625.0 && rate < 0))
     return SteeringSeek(GetLookTargetPos() + toLook * (rate + rate), out)
else return SteeringHalt(out)
```

The comparisons are against **squared** distance, so those two constants are exactly **0.7 m** and
**25 m** — the zoom limits. (0.7 is also the camera's `SetRadius`.) A positive rate puts the seek
goal past the character, so the camera closes in; a negative one puts it behind the camera. It uses
`SteeringSeek`, not arrive, so zoom goes flat out.

The rate itself is set by `CameraVehicle_t::Forward(float)` (`1001d73e`) — despite the name it does
nothing but store the zoom rate at `+0x1b8`. The driver calls it through vftable `+0x1c`.

**But `ZoomSteer` is only used by modes 0-2.** In **mode 3**, the one Lost Eden runs, the driver
takes a completely different path (`1002265d`-`100227fb`) and moves the camera directly:

```
dir      = normalize(optimalPos - lookTarget);  distance = |optimalPos - lookTarget|
move     = |pending| >= 1.0 ? dt * pending * 3.0 : dt * 3.0 * sign(pending)   // 1005c03c = 3.0
distance -= move;  pending -= move
if (sign(pending) flipped || |pending| < 0.3)  pending = 0
distance = clamp(distance, 0.78, 25.0)
SetRelPos(lookTarget + dir * distance)
if (zooming out && the camera did not reach it)  { SetRelPos(whereItWas); pending = 0 }
Update(); ForcedUpdate(false)
```

So mode 3 zoom tracks the wheel instead of steering toward it, its floor is **0.78** rather than
`ZoomSteer`'s 0.7, and zooming out into geometry reverts rather than shoving through. Measured after
porting: 2 m in 0.45 s, 4 m in 0.68 s, −3 m in 0.58 s, −8 m in 0.90 s; floor exactly 0.78, ceiling
exactly 25.

The driver's state machine (`10022808`-`1002298d`) keeps the *distance still to cover* at
`n3Camera_t +0x204` and last frame's distance at `+0x208`:

```
d = |lookTarget - cameraPos|
if (d < 0.8 && pending > 0)  skip            // don't service an inward zoom this close
if (pending != 0) {
    wasPositive = pending > 0
    pending = (d - previousDistance) + pending        // subtract what was actually covered
    if ((pending > 0) == wasPositive && |pending| >= 0.3)  Forward(pending)
    else { pending = 0; Forward(0); Halt(); ForcedUpdate(true); }
}
previousDistance = d
```

Stopping on a sign flip is the overshoot guard. **`ForcedUpdate(true)` is what commits the zoom** —
`CameraVehicleFixedThird_t::ForcedUpdate` (`1001f7c9`) stamps the camera's current position in via
`UpdateHeadingToPos(pos, recalcDistance)`, so `true` adopts the new distance as the preferred one.
Without it the camera would snap back to its old distance the moment the zoom ended.

Measured in the editor after porting: requests of 2 / 4 / −3 / −8 m move 1.72 / 3.72 / −2.72 /
−7.72 m — consistently 0.28 m short, which is the 0.3 m stop threshold — the floor holds at 0.68 m,
the ceiling at 25.02 m, and the distance sticks after release.

#### There is no zoom "step" in stock

Stock's zoom is a **continuous held-key rate**, not a per-notch distance. The driver builds the call
to `FUN_1002118c` at `10022a59`-`10022a84`:

```
yaw   = MouseTurnSensitivity * (+/-0.02)   from +0x184 bits 0x02 / 0x04  (StartRotateLeft/Right)
pitch = MouseTurnSensitivity * (+/-0.02)   from +0x184 bits 0x20 / 0x40  (StartRotateUp/Down)
zoom  = +/-dt                              from +0x184 bits 0x80 / 0x100 (StartZoomOut/In)
```

and inside `FUN_1002118c` that third argument is applied as `offset += dir * zoom * ZoomSpeed`. So
holding the key moves the camera `ZoomSpeed` metres per second; there is no quantum. That is why
every write to `+0x204` in N3 is a decrement or a zero — nothing ever adds a step to it.

`N3Camera._zoomStep` (metres queued per wheel notch) therefore has **no stock counterpart**. It is a
necessary adaptation — a wheel is discrete, stock's design is a held key — not a number that was
missed. Useful range is about 1-2: below ~0.3 the queued distance dies immediately on
`ZoomStopThreshold`, and above ~10 a single notch reaches the 25 m ceiling.

---

### 5.7 The vetoes decide facing, not position

The name misleads. `VetoForward` / `VetoUpAlignment` are **not** position constraints — they are
called from the body orientation update (`Vehicle.dll FUN_1000c616`, reached only from
`EnsureSurfaceAlignment`) and choose the camera's rotation. Orientation mode 3, which
`CameraVehicle_t`'s ctor selects, is:

```
up      = surfaceNormal                 // passed in
forward = GetBodyForward()
VetoUpAlignment(&up)                    // vftable +0x78
VetoForward(&forward)                   // vftable +0x7c
bodyRot = LookRotation(forward, up)
```

`CameraVehicle_t::VetoForward` (`1001df85`) is just "point at the look target", normalised, falling
back to `s_cReferenceForward` if the camera sits exactly on it.

`CameraVehicle_t::VetoUpAlignment` (`1001dfde`) takes world up, **discards the surface normal it was
handed** — the camera is never banked by the ground — orthogonalises it against the forward, and
swaps in `s_cReferenceForward` when looking straight up or down. That is exactly what Unity's
`Quaternion.LookRotation(forward, Vector3.up)` already does, so the port's original naive rotation
happened to be correct.

`CameraVehicleFixedThird_t::VetoForward` (`1001f235`) adds the one piece that is *not* free: inside
0.9 m it blends the facing from "look at the character" toward "face the way the character faces",
by `t = 2 * (0.9 - distance)`, complete at 0.4 m. Since the zoom floor is 0.78 m, this is reachable
at full zoom-in, and without it the view swings wildly when the camera is jammed against the head.

`DecideSnap` (`1001f537`) is **gated entirely on `+0x214`** — the locked-camera flag — so it does
nothing in mode 3. `DoDirectControl` (`1001d6e5`) is only reached from `CameraVehicle_t::CalcSteering`,
which `FixedThird` overrides, so it is likewise not in mode 3's path. `UpdateSensors` runs for
mode 1 only (`100223a1`). `FUN_1001ec3a`, the swing-around scale, is simply `x < 0 ? -1 : 1`.

### 5.8 The stock default camera position

`CameraVehicleFixedThird_t`'s ctor (`1001f0bd`) loads `PreferredCamPosX/Y/Z` and `PreferredCamDist`
from prefs; with none stored it falls back to **(0, 1.5, -4.5)** (`1001f1d2`), then normalises it —
the length becomes the follow distance and the vector becomes a unit direction. So out of the box
AO's third-person camera sits **4.743 m from the head, 18.4° above the horizon, directly behind**.
`+0x204` (stay behind) defaults to 1, so that direction is expressed in the character's frame.

`SetPrefs` (`1001d82f` / `1001f004`) **writes** those preferences back out; it does not load them.

### 5.9 Left drag orbits the camera; right drag turns the character

These are two different systems, and conflating them is why the port first had the character chasing
the camera whenever the right button was down.

**Right drag is mouse-look**, handled by `n3EngineClientAnarchy_t::N3Msg_MouseMovement`
(`Gamecode 100196b3`):

- it calls `MouseCameraControl(0.0, dy)` — **yaw is hardcoded to zero**, so the camera never yaws
  from mouse-look — and only when the `RMBMouseLook1st` / `RMBMouseLook3rd` DistributedValue matches
  the current view;
- the horizontal delta goes to the **character**, as `TurnRightMouse` (0xa) / `TurnLeftMouse` (0xd)
  to begin and action **`0x2b`** to continue;
- and the pitch is only applied **at all** when the matching preference is set (`10019768`):
  `enabled = (RMBMouseLook1st && IsFirstPerson) || (RMBMouseLook3rd && !IsFirstPerson)`. Both
  default **on** in the live client — a right drag there turns the character with the horizontal
  and pitches the camera with the vertical — so `N3Camera._rightDragPitchesCamera` defaults on too.
  Neither default is in these DLLs: the only reference to either name is this read;
- the camera then swings round on its own, because it is stay-behind.

**Order matters, and it is not optional.** Both halves happen inside the one call, in this sequence:

1. **the camera moves**, against the rotation the body still has — `MouseCameraControl(0.0, dy)` at
   `10019836`, which is `FUN_1002118c`, including Lock's re-seat of its goal at `10021572`;
2. **then the body turns** — `MovementChanged` → `VehicleForwardUpdate` at `10019a85`;
3. and the camera is placed behind the body's *new* rotation before anything is drawn.

`N3Camera` runs the same three steps inside one `LateUpdate`: `OrbitThirdPerson`, then
`TurnCharacter` (which re-reads the target pose), then `Vehicle.Tick`.

Either departure from that order breaks Lock visibly. Turning the body before the camera's own move
makes Lock re-seat a bearing measured against the old facing, so the camera walks off the
character's back a little further every frame; deferring the turn to the next frame leaves the
camera running a frame ahead of the body for the whole drag. `CameraViewModeTests` pins all three
orderings — with the right one, Lock holds its bearing to under 0.01° through a full 360° turn.

`N3Msg_MovementChanged` (`10018bc4`) shows what `0x2b` means: it rewrites it to `Update` (0x16) and
flags it **local-only** (`10018be8`), applying it through `n3Dynel_t::VehicleForwardUpdate(pos, rot,
dx, dy)` rather than sending it. `CheckMotionUpdate` sends a real `Update` to the server later, once
the pose has drifted past a threshold.

`n3Dynel_t::VehicleForwardUpdate` (`N3 10004ebd`, export 882) is where the character actually turns.
Given the body's current rotation it builds two quaternions — **dx about world up** `(0,1,0)`, and
**dy about the body's own right axis** (`(1,0,0)` rotated by the current rotation) — applies the
pitch first behind a guard (the pitched forward must still have a positive dot with the unpitched
one, `> 0.001`, so it cannot tip past vertical), then multiplies the yaw in, normalises, and commits
through `Vehicle_t::SetRelPosRot` / `SetRelRot`. So the dy that reaches here pitches the **body**,
not the camera; on foot it is always zero, because `N3Msg_MouseMovement` only forwards dy when the
control mode is 7 and otherwise hands it to `MouseCameraControl`.

The same function holds the **turn-to-strafe remap** (`10018d1a`): while mouse-look is active
(`DAT_102e3588`), `TurnLeftStart`→`StrafeLeftStart`, `TurnRightStart`→`StrafeRightStart`, and the
matching stops. That is the A/D-becomes-strafe rule. On mouse-look *start*, an already-active
keyboard turn is converted the same way (`10019736`).

`FUN_10070a2f()` is the movement mode (`MovementState`, so 7 = `Fly`) and `FUN_10070a4f()` is the
turn direction (0 none, 3 left, 4 right). Pitch is only inverted (`s_nInverted`) while flying.

**Left drag** orbits the camera and does not touch the character, which is why standing still and
left-dragging moves nothing.

`N3Msg_EndCameraMouseLook` (`100195ff`) hands back the accumulated yaw via
`EndMouseCameraControl(float&)` but **only applies it to the character in first person**
(`IsFirstPerson` gate at `1001961e`).

### 5.11 The four view modes

`n3Camera_t +0x1ec` is the mode, persisted as the int preference `"PreferredCameraMode"`
(`10020032`). `FUN_10020290` is the dispatch, and the disassembly is unambiguous:

```
mode 0 -> FUN_1001fb7e()           CameraVehicleFirstPerson_t
mode 1 -> FUN_1001f9c9()           CameraVehicle_t   (the base)
mode 2 -> push 0; FUN_1001faa1(0)  CameraVehicleFixedThird_t(+0x214 = false)
else   -> push 1; FUN_1001faa1(1)  CameraVehicleFixedThird_t(+0x214 = true)
```

| Mode | Class | Behaviour |
|---|---|---|
| 0 First person | `CameraVehicleFirstPerson_t` | camera **at** the eye |
| 1 Trail | `CameraVehicle_t` | steered by the base brain, with obstacle sensors |
| 2 Rubber | `FixedThird(false)` | physically steered to the ideal spot — lags and settles |
| 3 Lock | `FixedThird(true)` | not steered; `DecideSnap` places it rigidly |

**2 is the springy one, 3 is the rigid one.** The `+0x214` flag makes `CalcSteering` return
`None` and is also what gates the whole of `DecideSnap`, so the two are exact opposites.

Mode 0 is never written to the preference (`10020043`), so the game never starts in first person.
Its fresh-install default is not recovered; this port uses Rubber.

**First person** does no steering at all — `CalcSteering`, `CalcLateralSteering` and
`CalcTurnSteering` are each literally `xor eax,eax; ret 4` (`1001ecf7`, `1001ecfc`, `1001ed01`).
`SetEyeTargetLocalPos` (`1001ed86`) ends with `SetRelPosRot(GetLookTargetPos(), ...)`, i.e. the
camera is put **at** the eye. `SetRotAngles(pitch, yaw)` (`1001ef0d`) builds yaw about `(0,1,0)` and
pitch about `(1,0,0)` and composes them into `+0x1ec`; `Update` (`1001ef7d`) sets the world rotation
to `characterRotation * thoseAngles`. And its `GetSurface` returns null, so no occlusion solve.

**Trail** is `CameraVehicle_t::CalcSteering` (`1001e797`): attractor, then zoom, then
`DoDirectControl` (`1001d6e5`, a throttle at `+0x1a4` driving `SteeringForward`/`SteeringReverse`),
then arrive at the look target pushed back along the camera's *current* direction — so it keeps
whatever angle it has and simply trails. It lifts the goal by 0.4 when the camera is below the look
target (`1001eb3c`). `UpdateSensors` (`1001e71f`) is the only thing the driver runs for mode 1, and
it feeds an avoidance nudge at `+0x1c0`. **That nudge is a seam in the port** — it comes from
`FUN_1002046f`, a locality query that has not been read, so Trail currently avoids nothing.

**Lock**'s `DecideSnap` (`1001f537`) pulls in instantly but eases outward, closing a tenth of the
gap per call (`current * 0.9 + goal * 0.1`).

Measured in the editor, walking at 4 m/s: first person holds 0.000 m from the eye, Lock exactly
4.743 m, Rubber 5.83 m, Trail 5.88 m.

### 5.12 `n3Camera_t` — the input surface

Not a controller in the Unity sense; a command sink. `exp.py n3 n3Camera_t` gives all 28. Notably:

- `StartRotateLeft/Right/Up/Down` + `StopRotate…`, `StartZoomIn/Out` + `StopZoom…` — **held-state
  commands**, not per-frame deltas. AO's keyboard camera control is start/stop, and the rate lives
  in the vehicle.
- `MouseCameraControl(float, float)` (`10021689`), `EndMouseCameraControl(float&)` (`1002094f`),
  `SetMousePos(float, float)` (`10020571`).
- `ToggleCameraView()` (`1002190e`), `IsFirstPerson()` (`10020071`).
- `SetCameraAttractor(int, int, bool, int, float)` (`10021a19`), `GetNextVisibleAttractor`
  (`10021987`), `GetPreviousVisibleAttractor` (`10020faa`).
- `GetObjectUnderColLine()` (`1002069c`) and `GetNextTarget(const Identity_t&)` (`10020723`) —
  **targeting lives on the camera in stock**, through `n3CameraCollLine_t` (a `CollLine_t`).
  Click-to-select and tab-target are camera queries, not a separate targeting system.

---

## 6. Port status

109 xUnit tests in `Tests/Vehicle`, all green (`dotnet test`), plus in-editor runs (below).

**Measured in the Unity editor** (`Unity_RunCommand`), stock defaults, no occluders:

| Scenario | Result |
|---|---|
| idle, settled | offset `(0, 1.47, -4.42)`, distance **4.656 m** (preferred 4.743; the arrive brake stops it just short) |
| walking 2 m/s | distance **4.81 m** (worst 4.87) |
| walking 4 m/s | distance **4.93 m** (worst 4.95) |
| walking 8 m/s | distance **5.27 m** (worst 5.38) |
| occluded by a wall 2 m behind the head | settles at **1.994 m**, 10 search iterations |
| frame rate 10 / 30 / 60 / 144 fps, walking 4 m/s | 4.42 / 4.86 / 4.93 / 4.93 m |

Trailing grows gently with speed and the following distance is stable across frame rates. The
project compiles with **0 errors**.

| Piece | Status |
|---|---|
| `Vehicle_t` integrator + sub-stepping | **Ported**, unit-tested |
| `SteeringResult_e` | **Ported** |
| `SteeringArrive` / `SteeringHalt` / `SetVel` / falling + surface-hug flags | **Ported**, unit-tested |
| `CameraVehicle_t`: look target, follow distance, `UpdateMotionConstraints`, driver tick | **Ported**, unit-tested |
| `SteeringCamArrive` incl. hitch guard + swing-around | **Ported** (swing magnitude approximated, §8) |
| `CameraVehicleFixedThird_t`: `RecalcOptimalPos`, `CalcSteering`, default offset | **Ported**, unit-tested |
| `EnsureSurfaceAlignment` | **Read, not ported** — a seam (§4.5); defaults to "always aligned" |
| `UpdateHeadingToPos` (the orbit primitive) | **Ported**, unit-tested |
| Unity binding — `N3Camera` on the sim: line of sight, orbit, zoom, view-mode dropdown | **Ported**, verified in editor |
| `ZoomSteer` + `Forward` + `ForcedUpdate` + the driver's zoom state machine | **Ported**, unit-tested, verified in editor |
| Third-person pitch pole guard | **Ported**, unit-tested |
| `RMBMouseLook1st` / `RMBMouseLook3rd` gate | **Ported** as `_rightDragPitchesCamera`, default on |
| `n3Dynel_t::VehicleForwardUpdate` (mouse-look character turn) | **Read** — dx about world up, dy about body right, dot guard, `SetRelRot` |
| `MouseTurnSensitivity` chain | **Traced** — the slider is above these DLLs; `_lookSensitivity` fills its role |
| `SteeringSeek` | **Ported** |
| `UpdateSensors` / `CalculateSensorSteerDir` / `LineOfSight` | Seam (`Func<Vec3,Vec3,bool>` → `Physics.Raycast`); stock bodies not read |
| `VetoForward` / `VetoUpAlignment` incl. the 0.9 m facing blend | **Ported**, unit-tested |
| Mode 3 direct zoom (`SetRelPos` path) | **Ported**, verified in editor |
| `DecideSnap` | **Ported** — it is Lock's entire positioning mechanism |
| `DoDirectControl` | **Ported** (Trail) |
| `CalculateSensorSteerDir` / `UpdateSensors` | **Seam** — needs `FUN_1002046f`, unread; Trail avoids nothing |
| All four view modes + `PreferredCameraMode` + `ToggleCameraView` | **Ported**, unit-tested |
| `ReposCutOnAxis` | Not started |
| `FUN_1002118c` (stock's full orbit driver) | Partially — the port drives `UpdateHeadingToPos` directly |
| `CameraVehicleFirstPerson_t` | **Ported**, unit-tested |
| Attractors | Not started |
| `n3Camera_t` input surface (toggle view, picking, tab-target) | Not started |
| `CharVehicle_t` / `PlayerVehicle_t` | Not started |

---

### 6.1 How the port is wired

The binding is `Assets/Scripts/Controllers/N3Camera.cs`. It is stock's `n3Camera_t` — it owns the
view, takes the input verbs, holds the mode at `+0x1ec`, dispatches the
vehicle (`FUN_10020290`) and runs the driver `FUN_10022345`, which is itself reached from
`n3Camera_t`'s vftable at `1003e3ec`. That mirrors the effects port's split between a `GfxControl*`
binding and its Unity-free `*Sim`.

The `*Sim` classes keep their stock names, since they already mirror `Vehicle_t` /
`CameraVehicle_t` / `CameraVehicleFixedThird_t` / `CameraVehicleFirstPerson_t` exactly.

`PlayerController` holds it in a field called `N3Camera`. The Controllers prefab stores that
reference **by field name**, so the prefab key must match it; the component itself binds by the
script GUID `790e4331…`.

The public surface is `Camera`, `TargetAttached`, `SetInputs`, `SetTarget`, `ClearTarget`,
`SetFreePose` and `GetViewAngles`, used by `PlayerController`, `EffectRuntimeHost`,
`PlayfieldFactory`, `LoginScreenController` and `WorldOverlayController`. Everything underneath is
the sim.

**One deliberate deviation worth knowing:** stock's per-frame driver belongs to `n3Camera_t`, but the
port puts `Tick(dt, characterMaxSpeed)` on `CameraVehicleSim` instead. That keeps the driver testable
from `Tests/Vehicle` without Unity; moving it to `N3Camera` to match stock's ownership would make it
untestable.

The binding supplies:

- `LineOfSight` → `Physics.Raycast` against `_collisionMask`, defaulting to `GameLayers.GroundMask`.
- the eye offset, sampled **once** on attach from the head attractor's local height — stock calls
  `SetEyeTargetLocalPos` rather than re-reading the bone, and re-reading it would let the walk
  animation's head bob drive the whole camera.
- orbit, by swinging the current world direction and handing the result to `UpdateHeadingToPos`,
  which is the same entry point stock's orbit driver uses. No yaw/pitch is stored between frames, so
  stay-behind keeps working when the character turns.

---

## 7. Open questions

- `LineOfSight` (`1001d69a`) and `CanSeeFlexedPos` (`1001d8c6`) are unread; `LineOfSight` is a seam
  in the port, backed by `Physics.Raycast`.
- `CalculateSensorSteerDir` (`1001d955`) / `UpdateSensors` (`1001e71f`) are unread, but the driver
  only calls them for camera mode 1, so they are out of mode 3's path.
- `DoDirectControl` (`1001d6e5`) is unread; it is reached only from `CameraVehicle_t::CalcSteering`,
  which `FixedThird` overrides, so it is out of the third-person modes' path. It matters for Trail.
- `+0x44` (1.0) is set by the constructor and not traced to a use.
- `EnsureSurfaceAlignment` is read (§4.5) but not ported, so `+0x4c` stays at the constructor's 0.01
  instead of being rewritten from the surface every step.
- The `RMBMouseLook1st` / `RMBMouseLook3rd` defaults are not in these DLLs — the only reference to
  either name is the read at `10019768`. The port takes them from the live client's behaviour, and
  collapses stock's two flags into one field.
- Stock's fresh-install `PreferredCameraMode` is unrecovered; the port defaults to Lock.
- The two `mov dword [esi+0x104]` at `1002641b` / `100265ac` in N3 are a different class at the same
  offset, not vehicle sub-step caps — worth re-checking if sub-stepping ever looks wrong.
