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

---

## 11. Open items

- **`AoWindowDemo.uxml` / `.uss` are editor-only but ship.** They're referenced solely by
  `Assets/Editor/Uvga/AoWindowDemoPreviewWindow.cs`, yet sit in `Resources/`, so they're built into
  every player. Move them out of `Resources/`.
- **`LoadingScreenView` bypasses `UiMenu`** (§2) and duplicates its fade/visibility logic.
- **`AoWindow` is fixed-size and can't be dragged** (§6).
- **`UIInteractionManager` still exposes its DEV collections** (§9).
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
