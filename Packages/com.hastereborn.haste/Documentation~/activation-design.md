Activation design
===

How the palette gets opened, and why. Everything here was verified against the shipped
assemblies of Unity 6000.0.80f1 and 6000.3.17f1 (Cecil metadata plus IL reads), not taken
from documentation.

`Documentation~` is excluded from the AssetDatabase by the trailing tilde, so this file
ships with the package but is never imported.

Three tiers
---

**Tier 0 — the contract. Always present, zero reflection.**

```csharp
[Shortcut("Haste/Open Haste", null, KeyCode.K, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
```

Ctrl+Shift+K on Windows and Linux, Cmd+Shift+K on macOS, rebindable by the user in
Edit > Shortcuts.

- `ShortcutModifiers` is `None=0, Alt=1, Action=2, Shift=4, Control=8` — there is no
  `Command` member. `Action` resolves to Cmd on macOS and Ctrl elsewhere *at runtime*
  (`KeyCombination.ToKeyboardEvent`: `command = action && Application.platform ==
  RuntimePlatform.OSXEditor`), so one declaration is correct on both platforms with no
  branching. `ShortcutModifiers.Control` means the literal Ctrl key even on macOS — do not
  use it.
- The default is free on both platforms. Across all 167 shortcut attributes in 6000.0 and
  172 in 6000.3: `KeyCode.K` is used twice, both by the Animation module and both without
  `Action`; the `Action|Shift` combination is used only for `N` and `Mouse1`; and no
  `MenuItem` uses `%#k` or `#%k`.
- The old `[MenuItem("Window/Haste %k")]` must lose its `%k`. Unity 6 ships
  `[MenuItem("Edit/Search/Search All... %k")]` on its own Search window — a direct
  collision, and the only other `%k` in the editor. Dropping the suffix also leaves exactly
  one rebindable entry in Edit > Shortcuts instead of two competing ones, and keeps
  `HasteMenuItemSource`'s exact-string self-filter (`menuItem == "Window/Haste"`) working so
  Haste does not index itself.

**Tier 1 — double-tap Shift.** A toggleable extra, never the only way in. **Implemented**
in `HasteDoubleTapShift` (the hook) and `HasteDoubleTapShiftGesture` (the recognition),
split that way because the second half is pure and can therefore be tested — see below.

**Tier 2 — degradation.** If the internal fields disappear, log once and offer
`EditorApplication.modifierKeysChanged` (public, parameterless) as an explicitly less
precise mode, or nothing. Tier 0 is unaffected either way because it is attribute-registered.

Why double-tap Shift cannot use the public API
---

`ShortcutManager` can never express it. `BindingValidator.s_InvalidKeyCodes` contains every
modifier keycode, and a malformed binding does not fail loudly — the id registers with an
*empty* binding and only a discovery warning is logged. Verified at runtime with the literal
editor log line: `Binding uses invalid key code LeftShift`. No `ShortcutAttribute`
constructor accepts a key sequence either.

The hook that matters, and the trap
---

Use **`UnityEngine.GUIUtility.beforeEventProcessed`** as the primary detector:

```
internal static Action<EventType, KeyCode, EventModifiers> UnityEngine.GUIUtility.beforeEventProcessed
// UnityEngine.IMGUIModule.dll — identical and non-obsolete in 6000.0.80f1 and 6000.3.17f1
```

The obvious choice, `EditorApplication.globalEventHandler`, is **wrong for detection**, and
this is the whole reason this document exists.

`globalEventHandler` is the *post-consumption* hook — Unity's own source comment describes it
as "events that were not handled by anyone". Meanwhile
`EditorGUI.MightBePrintableKey(Event)` decides whether a focused IMGUI text field consumes a
KeyDown, and its jump table (base keyCode 273; indices 27–40, i.e. keyCodes 300–313, which
include `LeftShift=304` and `RightShift=303`) returns **false** for every modifier.

Put together: a focused text field consumes the *letter* between two Shift taps but lets the
bare Shift KeyDowns through. That is the exact inverse of what the gesture needs — the
guard "reset on any other KeyDown" becomes a no-op precisely while the user is typing
CamelCase. Typing `Hello World` in a rename field looks indistinguishable from Shift, Shift.

`beforeEventProcessed` is genuinely pre-consumption: in `GUIUtility.ProcessEvent(int,
IntPtr, out bool)` it is invoked at `IL_003D`, after `m_Event.CopyFromPtr` at `IL_0014` and
before `result = false` at `IL_0044` and the `processEvent` dispatch at `IL_0047`. It hands
over `(type, keyCode, modifiers)` as raw native parameters.

Unity itself splits these two hooks exactly this way: it *resets* shortcut state from
`beforeEventProcessed` (`ShortcutIntegration.BeforeEventProcessedHandler →
ShortcutController.ResetShortcutState`) and *acts* from `globalEventHandler`. Reset from the
hook that sees everything; act on the hook that sees leftovers.

Subscribe to both and deduplicate on `(type, keyCode, frameCount)`. Over-delivery is benign
and testable; under-delivery is silent and fatal.

False-positive rules
---

Shift is the most overloaded key in the editor: shift-click range-selects in the Hierarchy,
shift-drag snaps in the SceneView, and Shift+letter is every capital letter.

Suppress the gesture entirely while a text field is being edited, using public API —
`EditorGUIUtility.editingTextField`, `EditorGUIUtility.textFieldHasSelection`,
`GUIUtility.keyboardControl`, `GUIUtility.hotControl` are all public, static and
non-obsolete in both editors. This removes the largest false-positive class outright:
Hierarchy renames, Inspector fields, search boxes, Haste's own query field, and IME input
where bare Shift is a mode toggle. Ctrl/Cmd+Shift+K still works everywhere.

The remaining invariants, none of them user-configurable:

- fire on the second release, not the second press
- ~~require a KeyUp between the two KeyDowns~~ — subsumed. Reading the modifier BIT makes
  key repeat structurally invisible: holding Shift leaves the bit set, so a repeat is not a
  transition at all
- each tap held under ~120 ms; a longer press was a hold, not a tap
- both taps must be the same physical Shift key — **best-effort only**, see below: modifier
  bits cannot tell Left from Right
- reject if `modifiers` contains anything but Shift, after masking off the incidental
  `FunctionKey`, `Numeric` and `CapsLock` bits
- reset on any MouseDown/MouseDrag/MouseUp/ScrollWheel, and on `focusChanged`
- suppress in play mode and while `Haste.IsApplicationBusy`

Only the timing window is configurable (`DoubleTapWindowMs`, default 250), because typing
rhythm varies and the gesture cannot appear in Edit > Shortcuts, so Haste's own preferences
page is the only place a user can tune or escape it.

Never call `Event.current.Use()` on the Shift events — consuming them breaks shift-click and
shift-drag. Observe only.

Failing soft is mandatory, not polite
---

Because `ShortcutIntegration` attaches lazily via `EditorApplication.delayCall`, an
`[InitializeOnLoad]` subscriber lands **first** in the multicast list. An exception escaping
our handler therefore aborts the remaining invocations — killing Unity's own
`ShortcutIntegration.EventHandler`, the shortcut helper bar, the maximize gesture, and the
trailing `Event.current = null`. Every shortcut in the editor dies, and it presents as a
Unity bug.

So:

1. Resolve each `FieldInfo` in a try/catch and require the exact expected `FieldType`. A
   type mismatch means unavailable, not cast-and-pray.
2. Wrap the whole handler body in try/catch. Reset gesture state in the catch.
3. Second consecutive exception → unsubscribe permanently, log once.
4. Runaway breaker: more than 3 fires in 10 s → self-disable and log once with the
   settings path. This is the net for whatever false-positive class we did not anticipate,
   and it is headlessly testable.
5. Combine, never assign. `Delegate.Remove` then `Delegate.Combine`. Assigning would wipe
   Unity's own subscribers — the actual failure mode seen in careless plugins.
6. Defer the open through `EditorApplication.delayCall`; opening a window during event
   dispatch corrupts Unity layout state.

`[InitializeOnLoad]` re-hooks every domain reload, and statics reset, so cross-reload
duplicates are structurally impossible. Guard against double-hooking within one domain.

How the invariants are actually tested
---

Every rule in this document is a *rejection* rule, and a rejection rule that silently does
not apply is only discovered by a user whose palette keeps opening mid-sentence. So the
recognition is a pure state machine with an injected clock —
`HasteDoubleTapShiftGesture.Feed(type, key, modifiers, time, suppressed)` — and
`HasteDoubleTapShiftTests` drives it directly: key repeat from holding Shift, a tap held too
long, taps too far apart, mismatched Shift keys, an intervening letter, a chord modifier,
incidental CapsLock/NumLock/fn bits, mouse activity, suppression mid-gesture, and the
runaway breaker.

What that does **not** cover is the hook: whether the events arrive at all, in that order,
with those keycodes. That is the part below.

What is still unproven
---

Two things could not be settled from a Windows machine in batch mode, and both are why
Tier 0 exists. The first is now partly answered — the fields are confirmed present on
6000.3.17f1 with the exact expected types, checked by reflection on the running editor —
but delivery of real keystrokes is not, and cannot be:

- **No physical keystroke has ever been observed.** `-batchmode -nographics` cannot inject
  input. That a bare Shift press produces `EventType.KeyDown` with `keyCode ==
  KeyCode.LeftShift` rests on the documented event sequence, on
  `KeyCombination.k_KeyCodeToEventModifiers` mapping LeftShift/RightShift to
  `EventModifiers.Shift`, and on `Trigger.HandleKeyEvent` being gated on KeyDown/KeyUp.
  High confidence, not observation.
- ~~**macOS delivery is still unobserved.**~~ **Settled by observation, and it changed the
  design.** On 6000.3.17f1/macOS a bare Shift produces **no key event at all** — the
  suspicion about `NSEvent flagsChanged` was right. What it does produce, captured from a
  real editor:

  ```
  [Haste] modifierKeysChanged                            t=12153.246
  [Haste] repaint  key=None  mods=Shift  (was None)      t=12153.247
  ```

  So the press is observable, just not as a keystroke: it surfaces as the **modifier bits
  on whatever event arrives next**, one millisecond later. `HasteDoubleTapShiftGesture`
  therefore recognises **transitions of the Shift bit** rather than KeyDown/KeyUp, which
  works on every platform since a real Shift KeyDown carries the bit too.

  Two consequences worth knowing:

  - "Both taps must be the same physical Shift key" is now **best-effort**. Modifier bits
    cannot distinguish Left from Right, so the rule is enforced when the events happen to
    carry a keycode and waived when they do not — which on macOS is always. Enforcing it
    there would disable the gesture outright.
  - Key repeat stops being a rule and becomes structural: holding Shift leaves the bit
    set, so a repeat is not a transition and nothing happens. The old "require a KeyUp
    between the two KeyDowns" rule is subsumed.

  `EditorApplication.modifierKeysChanged` fires too, and is subscribed for diagnostics
  only. It is parameterless — it cannot say which modifier or which direction — so it
  cannot drive the gesture. Its real use is that it appears to *provoke* the repaint that
  carries the bits.

Also: `UnityEngine.Event` carries no timestamp — only the internal `Event.GetDoubleClickTime()`
exists — so the window is measured on the dispatch clock with
`EditorApplication.timeSinceStartup`, and an editor stall can coalesce two far-apart presses.
Reject implausibly long gaps rather than trusting the clock.

Acceptance test, runnable headlessly
---

Assert on the *binding*, not on compilation, because a malformed `[Shortcut]` compiles
cleanly:

```csharp
ShortcutManager.instance.GetShortcutBinding("Haste/Open Haste")  // non-empty
ShortcutManager.instance.GetAvailableShortcutIds()               // contains the id
```

Wrap every `GetShortcutBinding` call — an unknown id throws `ArgumentException`.
