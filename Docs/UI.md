# Runtime UI: structure and conventions

This is the working reference for Lost Eden's in-game UI. Everything the player sees is UI Toolkit
(UIElements); there is no uGUI anywhere in the project — no `Canvas` in any scene or prefab, and no
`UnityEngine.UI` or `UnityEngine.EventSystems` in any runtime script. Read this before touching
`Assets/Scripts/UI/`.

---

## 1. Ground rules

- **`PanelRenderer`, not `UIDocument`.** Unity 6000.6 files `UIDocument` under "UI Toolkit/Legacy";
  `PanelRenderer` (Unity 6.5+) is the replacement. No `UIDocument` remains outside
  `Assets/Editor/`. Don't reintroduce one.
- **The root arrives late.** `PanelRenderer` exposes *no* public `rootVisualElement` — that
  property, `IPanelComponent.GetRootVisualElement()` and `PanelRendererRootElement` are all
  `internal`. The only supported way in is `RegisterUIReloadCallback(VersionedUIReloadCallback)`,
  which fires once the panel is attached, i.e. **after `Awake`**. A view therefore cannot bind its
  elements synchronously. Bind through a `WhenReady` hook (§4) and hold any state the caller set in
  the meantime.
- **Asset before panel settings.** Assigning `panelSettings` rebuilds the root and only clones the
  tree if `visualTreeAsset` is already set. Always assign in that order.
- **Sorting lives on the component.** `PanelRenderer` derives from `Renderer`, so stacking is
  `renderer.sortingOrder`. The per-panel `PanelSettings` carries a matching `sortingOrder` too
  (§3).
- **No element lookups by type.** Everything is found by name with `Q<T>("some-name")` against the
  content root, so UXML names are API. Renaming one silently breaks the binding.

---

## 2. The four panels

Each is one `PanelRenderer` with its own `PanelSettings`. Higher `sortOrder` draws on top.

| Panel | Script | sortOrder | UXML | Lives in |
|---|---|---|---|---|
| WorldOverlay | `UI/Overlay/WorldOverlayController.cs` | 50 | `UI/WorldOverlay` | created at runtime, `SceneInstaller.cs:62` |
| Inventory | `UI/InventoryView.cs` | 90 | `UI/AoWindow` + `UI/ItemContainerGrid` | created at runtime, `SceneInstaller.cs:72` |
| LoginScreen | `UI/LoginScreenView.cs` | 100 | `UI/LoginScreen` | `Assets/Scenes/GameScene.unity` |
| LoadingScreen | `UI/LoadingScreenView.cs` | 110 | `UI/LoadingScreen` | `Assets/Prefabs/SceneScope.prefab` |

Two of them have no authored GameObject at all — `SceneInstaller` news them up during
`InstallBindings` and `[RequireComponent(typeof(PanelRenderer))]` supplies the component. If you go
looking for "the Inventory object" in the scene, it isn't there until play mode.

`LoadingScreenView` is the one panel that does **not** go through `UserInterface.Load`; it owns its
`PanelRenderer` directly and so does not appear in `UserInterface.Menus`. It predates the shared
layer's async support and was migrated first. Folding it into `UiMenu` is a reasonable cleanup.

---

## 3. `UserInterface` — the shared layer

`Assets/Scripts/UI/UserInterface.cs`. A static facade plus the `UiMenu` type.

`Load(host, uxmlResourcePath, sortOrder, startVisible, logName, stretchContentRoot, centerPanelRoot)`
loads the `VisualTreeAsset` from `Resources/`, gets-or-adds a `PanelRenderer` on the host
GameObject, assigns asset → `sortingOrder` → `panelSettings`, and returns a `UiMenu` **immediately**
— before the root exists. It also registers the menu in `ActiveMenus`, exposed as
`UserInterface.Menus`, which is the handle a debug probe uses to inspect live panel state.

`LoadWindow(...)` wraps `Load` with the `UI/AoWindow` chrome shell and returns a `UiWindow` (§6).

### The PanelSettings cache — read this before changing it

`PanelSettingsBySortOrder` is a **static** `Dictionary<int, PanelSettings>` holding instances made
with `ScriptableObject.Instantiate` from `Assets/Resources/UI/DefaultPanelSettings.asset`. One per
distinct sort order.

With *Enter Play Mode Options → Reload Scene only* (domain reload disabled, which is how this
project runs) the dictionary survives the play session while the `PanelSettings` objects themselves
are destroyed on exit. A plain `TryGetValue` therefore hands back a **destroyed** object on the
second Play; every panel gets a null `panelSettings`, `rootVisualElement.panel` is null, and
**nothing renders at all — with no console error**, because the controllers run happily against
elements that are simply never on a panel.

`GetOrCreatePanelSettings` guards the cache hit with Unity's overloaded `!=`:

```csharp
if (PanelSettingsBySortOrder.TryGetValue(sortingOrder, out PanelSettings existing) && existing != null)
    return existing;
```

Do not weaken that to `TryGetValue` alone, and do not swap it for `??=` or `?.` anywhere near a
`UnityEngine.Object` — those use *reference* null and skip right past a destroyed object.

The same applies to `PanelRenderer`: a dead `PanelSettings` means the reload callback never fires,
so the UI would not merely be invisible, it would never bind.

---

## 4. `UiMenu` and the ready handshake

`UiMenu` owns the `PanelRenderer`, registers the versioned reload callback, and exposes:

- `Root` — the content root (`#root` inside the UXML, else the panel root). **Null until ready.**
- `PanelRoot` — the renderer's own root.
- `IsReady`, `Renderer`, `Name`
- `WhenReady(Action<VisualElement>)` — runs now if the root already arrived, else queues it
- `Show()` / `Hide()` / `SetVisible(bool)` / `HideFade(duration, onComplete)` / `StopFade()`

Visibility is held in a field and applied on arrival, so a `Show()` that lands before binding still
takes effect. `Q<T>(name)` is null-safe before ready.

The same handshake is repeated one level up wherever a consumer needs bound elements:

| Type | Hook |
|---|---|
| `UiMenu` | `WhenReady(Action<VisualElement>)` |
| `UiWindow` | `WhenReady(Action<UiWindow>)` |
| `LoginScreenView` | `WhenReady(Action)` |

`LoginScreenController.Start` does nothing but check the view exists and then defer its whole boot
sequence into `OnViewReady`. That matters: `RestoreFormDefaults()` writes `UsernameField.value`, and
the field is null until the callback lands.

---

## 5. Disposal

Every view unregisters in `OnDestroy` — `UserInterface.Unregister(menu)` removes it from
`ActiveMenus` and calls `UiMenu.Dispose`, which stops any fade, drops queued ready callbacks and
unregisters the reload callback. `UiWindow.Dispose` additionally unhooks the close button.

---

## 6. `AoWindow` — the chrome shell

`Assets/Resources/UI/AoWindow.uxml` + `.uss`. A reusable window frame with a slot in it:

```
#root            .ao-window-root    full-screen dimmer, centers the frame
└ #window-frame  UvgaBackground     the frame, 360px wide, min-height 280px
  ├ #window-header
  │ ├ #window-title   Label
  │ └ #close-button   UvgaImage
  └ #window-body   ← content UXML is nested here
```

The frame and close button draw real AO art by entry name —
`GFX_GUI_BORDER01_BACKGROUND` (StretchToFill) and `GFX_GUI_BOREDER3_CLOSEBUTTON_NORMAL`
(ScaleToFit, sized to texture). *That typo,* `BOREDER3`, is the actual AO asset name; don't "fix"
it. The USS also sets a flat `rgb(48, 52, 64)` fill with a tan border as a fallback, so the shell is
visible in UI Builder before UVGA resolves — and at runtime if the AO install path isn't set.

`UiWindow` binds `#window-title` / `#close-button` / `#window-body` from its ready callback, clears
the body, instantiates the content UXML into it at `flexGrow: 1, width: 100%`, applies the content's
own stylesheet, and wires `ClickEvent` on the close button to `Close()` → `Hide()` + the `Closed`
event. `ContentRoot` is the content's `#root`; `Body` is the shell's slot.

Known limits: the 360×280 size is hardcoded in `.ao-window-frame`, so every window is that size
unless the content overrides it, and there is no drag-to-move — the header is a title and a close
button only.

Only **`InventoryView`** uses the shell. The login and loading screens are plain full-screen panels.

---

## 7. Content views

Plain C# classes wrapping `VisualElement`s. They have no `PanelRenderer` of their own and are built
into a panel's tree by its owner.

**`UI/Overlay/`** — world-anchored HUD inside the WorldOverlay panel.
`TrackTransformView` is the abstract base (an element that follows a world transform on screen);
`ObjectPoolOverlay<T>` is the generic pool that spawns and recycles them.
`ScreenNameplateOverlay` / `ScreenNameplateView` draw unit nameplates (`UI/Unit/ScreenNameplate`,
with `NameplateState`); `HitIndicatorOverlay` / `HitIndicatorView` draw floating damage numbers
(`UI/Unit/HitIndicator`, fed by `HitIndicatorInfo`). Both are ticked from
`WorldOverlayController.LateUpdate` with the current camera. The overlay root is
`pickingMode = Ignore` so it never eats gameplay clicks.

**`UI/Inventory/`** — `ItemContainerGridView` builds a grid of `ItemCellView` sized to an
`ItemContainer`'s capacity; each cell shows the icon from `StatId.icon` via `IconTextureCache`.

---

## 8. UVGA: AO's own GUI art

`UI/Uvga/`. Lost Eden draws stock AO GUI textures straight out of the client's archives rather than
importing them as project assets.

`UvgaArchive` parses the UVGI text index / UVGA concatenated-PNG payloads; `UvgaTextureDecoder` does
the shared PNG → `Texture2D` decode. `UvgaTextureCache` is the runtime, name-keyed lazy cache (bound
into the container by `SceneInstaller` and published through `UvgaTextureSource.BindRuntime`);
`UvgaPathTextureCache` is the editor/preview equivalent that works from a bare AO install path and
never writes into `Assets/`. `UvgaTextureSource` is the facade the elements call — runtime prefers
the runtime cache, edit mode uses the path cache — and raises `Changed` when the AO path is set so
live elements repaint.

The two custom elements are the **only** `[UxmlElement]`s in the project:

| Element | Attributes | Notes |
|---|---|---|
| `UvgaBackground` | `texture-name`, `scale-mode` | paints via `generateVisualContent` so UI Builder can't strip the texture |
| `UvgaImage` | `texture-name`, `scale-mode`, `size-to-texture` | uses `Image` rather than `style.backgroundImage`, which the Builder often clears |

Both subscribe to `UvgaTextureSource.Changed` on attach and unsubscribe on detach.

---

## 8a. Fonts: stock's 1-bit Verdana

Stock draws text as one-bit glyphs: `FontInfo_t::GetGlyph` (GUI.dll `0x1012e828`) has GDI
rasterise each character into a monochrome bitmap, so there is no antialiasing at all. Lost Eden
reproduces those exact pixels. Nothing in Unity rasterises Verdana, and no `.ttf` ships.

**Source data.** `Assets/Resources/UI/Fonts/Verdana/*.pixelfont` are `pixelfont-1` atlases made
by `export_font.py` in the `aowebui` repo. It replicates stock's `CreateFontA` + `TextOut` call,
so it has to run on Windows. There is one file per entry in GUI.dll's font table at `0x10272df0`:
Verdana 12, 13, 14 and 16, and Verdana Bold 13, 20 and 24. For Bold 24, GDI's font mapper gives a
23 px cell, and the game gets the same 23 px.

    python export_font.py --face verdana --size 13 [--bold] --out <dir>
    # then copy <dir>/verdana-13.json to Fonts/Verdana/verdana-13.pixelfont

**Importer.** `Assets/Editor/Fonts/PixelFontImporter.cs` (a `ScriptedImporter` for
`.pixelfont`) turns each file into a static, bitmap `FontAsset`. It holds an Alpha8 atlas with
point filtering and the glyphs cropped to their ink, and `faceInfo.pointSize` is the cell height.

**Using it.** `Fonts/Verdana/AoFonts.uss` has one class per font, e.g.
`.ao-font-verdana-13`. The output is only pixel-exact while all of these hold:

- `-unity-text-generator: standard`. The advanced generator (the 6000.6 default) refuses static
  font assets and draws nothing, apart from a console error.
- `font-size` is exactly the cell height, so every glyph is drawn at 1:1.
- The panel scale is a whole number. `DefaultPanelSettings`, the template every panel is
  cloned from, is `ConstantPixelSize` at scale 1: the UI does not grow or shrink with the
  resolution. Scale 2 is also exact. Any non-integer scale keeps hard edges but draws stems
  unevenly, and so does a Game view zoomed away from 1x in the editor.

Fractional element positions are safe, because UI Toolkit snaps them to whole pixels.

Verified by rendering each font through a runtime panel into a RenderTexture and diffing it
against the atlas composited the way stock does it (ink only, pen advancing by `adv`). Every
font came out with zero pixel mismatches and no grey pixels, at element offsets of 0, .25, .5
and .75, and at panel scale 2.

Unity has deprecated `AtlasPopulationMode.Static`. If the standard generator or static assets are
ever removed, the fallback is to draw the glyph quads directly with `generateVisualContent`,
which is what stock does anyway.

---

## 8b. Skins: how the Ao* views look

The `Ao*` classes in `Scripts/UI/Ao/` port stock's class structure: what each view is, what
it holds and how it behaves. They do not port stock's look. Every view's look comes from a
**skin**, which is one USS file. Players will eventually write their own skins, so the split
between the two is strict:

- **C# owns structure and rules**: the view hierarchy, a container being a wrapping grid, text
  views wrapping, where the combo list floats, every state. A skin cannot change these.
- **The skin owns looks**: colour, borders, spacing, control heights, fonts, case.

**Where the skin plugs in.** `UnityDefaultRuntimeTheme.tss`, the theme every panel uses,
imports the default theme and then `Skins/Phosphor/Phosphor.uss`. Switching skin means
switching which theme a `PanelSettings` carries.

**What a skin styles.** Each view adds `ao-view` plus a block class named after its class
(`AoButton` → `.ao-button`), with its parts as `block__part` and its states as `block--state`.
UI Toolkit has pseudo-classes for hover and disabled but none for pressed or toggled, so those
two are published as classes (`--pressed`, `--on`). The full list is in the header of
`Phosphor.uss`. These class names are API: renaming one silently breaks every skin.

**Two things USS can't do, and how the skin does them anyway:**

- *Case.* UI Toolkit has no `text-transform`. A skin sets `--ao-text-transform: uppercase` on
  a `__label`, and `AoLabel` reads that custom property and applies it.
- *Glyphs outside latin1.* The pixel fonts have no triangle, so dropdown and sort arrows are an
  `AoCaret`. It draws three solid rows in its own `color`, and repaints when that colour changes.

**Stock's `font` attribute** (`LARGE`, `HUGE`, `CC17`...) becomes the class `ao-font--large`
and so on. Which face each key maps to is the skin's choice; stock's own mapping is unknown.

**Phosphor**, the default skin, uses the design language of `malis-ao-toolkit-web`'s PHOSPHOR
kit. Its colours are tokens on `:root` (`--ao-bg-0`...`--ao-accent`), so a recolour only has to
change the tokens.

**Composites are layout only.** A composite's own sheet, e.g. `LoginScreen.uss`, only places
its views. If it needs a colour it uses a skin token. The reason is precedence: a sheet
attached in UXML outranks the theme, so any look written there could never be reskinned.

Two traps:

- **`AoView` writes `flex-direction` inline** (stock's `view_layout`, default vertical), and an
  inline style beats USS. So a view is a column even if the skin says `row`. Align its
  children in column terms: `align-items` is the horizontal axis.
- **Re-importing a stylesheet rebuilds any live `UIDocument` built on it** and empties that
  document's tree.

Verified by rendering the login panels and a gallery of every view into a RenderTexture,
through a real runtime panel with the theme applied. Every text region came out as exactly
background, one ink colour and the 1px line colours, with no grey.

---

## 9. Pointer arbitration

`UI/UIInteractionManager.cs` implements `IUINotifyService`, registered in the container by
`SceneInstaller`. Elements notify it of hover and drag (`NotifyHoverStart/End`,
`NotifyDragStart/End`) and gameplay notifies it of world drags (`NotifyGameDragStart/End`); input
code then asks `IsPointerOverUI`, `IsDraggingUI` or `IsInteractingWithUI` before acting on a click.
A world drag suppresses `IsPointerOverUI` so dragging the camera across a panel doesn't stall.

It still exposes `HoveredElements` and `DraggedElement` behind a `// DEV - remove later` comment.

---

## 10. Assets

All runtime UI assets live in `Assets/Resources/UI/` and are loaded by path string, so moving or
renaming one breaks it at runtime, not at compile time.

| Asset | Used by |
|---|---|
| `AoWindow.uxml` / `.uss` | `UserInterface.LoadWindow` shell |
| `LoginScreen.uxml` / `.uss` | `LoginScreenView` |
| `LoadingScreen.uxml` / `.uss` | `LoadingScreenView` |
| `WorldOverlay.uxml` / `.uss` | `WorldOverlayController` |
| `ItemContainerGrid.uxml` / `.uss` | Inventory window content |
| `ItemCell.uxml` / `.uss` | `ItemCellView` |
| `CharacterButton.uxml` | `LoginScreenView` character select |
| `Unit/ScreenNameplate.uxml` / `.uss` | `ScreenNameplateOverlay` |
| `Unit/HitIndicator.uxml` / `.uss` | `HitIndicatorOverlay` |
| `UnityDefaultRuntimeTheme.tss` | fallback theme, `UserInterface.LoadDefaultTheme` |
| `DefaultPanelSettings.asset` | the template every panel's `PanelSettings` is cloned from |
| `Fonts/Verdana/*.pixelfont` + `AoFonts.uss` | stock's 1-bit Verdana (§8a) |
| `Skins/Phosphor/Phosphor.uss` | the default skin, imported by the theme (§8b) |

---

## 11. Open items

- **`AoWindowDemo.uxml` / `.uss` are editor-only but ship.** They're referenced solely by
  `Assets/Editor/Uvga/AoWindowDemoPreviewWindow.cs`, yet sit in `Resources/`, so they're built into
  every player. Move them out of `Resources/`.
- **`LoadingScreenView` bypasses `UiMenu`** (§2) and duplicates its fade/visibility logic.
- **`AoWindow` is fixed-size and can't be dragged** (§6).
- **`UIInteractionManager` still exposes its DEV collections** (§9).
- **Stock font keys aren't mapped yet** (§8a). `AoTextView`/`AoButton` carry `font="LARGE"`,
  `HUGE`, `CC17`, … raw. Their mapping to `FontID_e` and so to a font-table row hasn't been
  recovered. GUI.dll also lists two `.fnt` bitmap fonts (`FontTooltip9`, `FontGameShell12`) and
  a user-set chat font (`ChatFontSize` in MainPrefs.xml); neither is ported.
- **Player skins can't be loaded at runtime yet** (§8b). A skin is a USS asset, and a built
  player has no USS compiler, so a skin a player writes needs either a runtime USS parser or
  a skin format of our own.
- **`AoWindow` is not skinned** (§6). It still draws stock art through `UvgaBackground` rather
  than being built from `AoBorderView`.
- **Serialized component state.** `PanelRenderer` components added by `[RequireComponent]` at load
  carry stale or empty `panelSettings` / `visualTreeAsset` in `GameScene.unity` and
  `SceneScope.prefab`. Harmless — `UserInterface.Load` assigns both at runtime — but save both
  assets so the components themselves are persisted.

---

## 12. Debugging a blank UI

Logs won't show it: the controllers keep running and still log their normal progress while nothing
is on screen. Probe the live panel state instead, from play mode:

```csharp
foreach (var menu in UserInterface.Menus)
    Debug.Log(menu.Name
        + " ready=" + menu.IsReady
        + " panelSettings=" + (menu.Renderer.panelSettings == null ? "NULL" : "ok")
        + " panel=" + (menu.PanelRoot?.panel == null ? "NO PANEL" : "ok"));
```

`panelSettings=NULL` with `NO PANEL` across every panel at once is the stale-cache failure in §3.
A single panel ready but empty is usually a renamed element in its UXML (§1).
