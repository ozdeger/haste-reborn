Activation design
===

How the palette gets opened, and why. Everything here was verified against the shipped
assemblies of Unity 6000.0.80f1 and 6000.3.17f1 (Cecil metadata, IL reads, and runtime
reflection), not taken from documentation.

`Documentation~` is excluded from the AssetDatabase by the trailing tilde, so this file
ships with the package but is never imported.

The contract
---

One way in, always present, zero reflection:

```csharp
[Shortcut("Haste/Open Haste", DefaultKey, ShortcutModifiers.None)]
// DefaultKey: KeyCode.DoubleQuote on macOS, KeyCode.BackQuote elsewhere
```

The bare key above Tab, rebindable by the user in Edit > Shortcuts. A palette you reach for
dozens of times an hour should cost one key; anything longer and you stop reaching for it.
It was a bare Tab in 2.6.0 and 2.7.x, and the reasoning below about Tab still stands.

The KeyCode differs by platform, and it is the one claim here that was not measured. macOS
reports punctuation by the character it types, so on a Turkish-Q keyboard that key arrives as
`"`. Windows reports the physical key (`VK_OEM_3`), which Unity names `BackQuote` on every
common layout. A US Mac types `` ` `` in that position and gets nothing from the default. If
the Turkish-Q assumption turns out wrong, rebind by *pressing* the key in Edit > Shortcuts,
then read the `m_KeyCode` the profile JSON recorded in
`~/Library/Preferences/Unity/Editor-5.x/shortcuts/` — that number is the ground truth.

- The id is load-bearing. `ShortcutManager` keys user overrides by id, so renaming it
  silently discards every rebinding anyone has made.
- `ShortcutModifiers` is `None=0, Alt=1, Action=2, Shift=4, Control=8` — there is no
  `Command` member. `Action` resolves to Cmd on macOS and Ctrl elsewhere *at runtime*
  (`KeyCombination.ToKeyboardEvent`: `command = action && Application.platform ==
  RuntimePlatform.OSXEditor`), so one declaration is correct on both platforms with no
  branching. `ShortcutModifiers.Control` means the literal Ctrl key even on macOS — do not
  use it. None of that applies to the current default, which takes no modifiers, but it is
  the trap anyone rebinding this will walk into.
- `[MenuItem("Window/Haste")]` must stay exactly that, with no `%k` suffix. A shortcut baked
  into a MenuItem string is not rebindable and would compete with the ShortcutManager entry,
  giving two bindings for one command; the old `[MenuItem("Window/Haste %k")]` collided
  head-on with Unity 6's own `[MenuItem("Edit/Search/Search All... %k")]`, and the loser of
  that fight silently never opened. The exact string also keeps
  `HasteMenuItemSource`'s self-filter (`menuItem == "Window/Haste"`) working, so Haste stays
  out of its own results.

Why Tab is safe to bind
---

Measured on 6000.3.17f1, because two of the three facts below are the opposite of what they
look like.

**A rejected binding does not fail loudly.** `BindingValidator` refuses some keys outright,
and a `[Shortcut]` that names one still *compiles*: the id registers with an **empty**
binding and Unity writes a discovery warning nobody reads. The palette simply stops opening.
This is why `HasteActivationTests` asserts on the registered binding and on the invalid-key
list rather than on this file building.

**The refused list is short, and Tab is not in it.** Read out of
`BindingValidator.s_InvalidKeyCodes` at runtime, it is exactly twelve entries:

```
None, Escape, Return, CapsLock,
LeftShift, RightShift, LeftAlt, RightAlt, LeftControl, RightControl, LeftMeta, RightMeta
```

Every modifier keycode, plus three keys the editor reserves. Escape and Return are the
instructive ones: they look every bit as ordinary as Tab and are both refused.

**Tab is effectively unclaimed.** Across the 749 shortcut ids a full editor registers, Tab
appears exactly once besides Haste's: `Timeline/ToggleClipTrackArea`, declared with
`typeof(UnityEditor.Timeline.TimelineWindow)` as its context. Unity resolves a
context-scoped shortcut over a global one, so Timeline keeps Tab inside the Timeline window
and Haste gets it everywhere else.

That last point is why the collision test excludes context-scoped ids. A `ShortcutBinding`
carries no context, so on a naive comparison Haste's Tab and Timeline's are indistinguishable
— and failing the suite over a shortcut resolving exactly as designed is worse than not
testing it. The test reads contexts back out of the `[Shortcut]` attributes instead, which is
the only place they are visible.

What is **not** settled here is how a global unmodified key interacts with focus navigation
and with text fields. The routing that stops `W`/`E`/`R` firing while you type in the
Inspector lives on the native side of `ShortcutIntegration` — `HasModifiers` and
`HasAnyEntriesHandler` are both `[RequiredByNativeCode]` and are called *from* native, so
there is nothing managed left to read. Anyone changing this default should try it in a real
editor rather than reasoning about it.

Removed: double-tap Shift
---

Versions 2.1.0 through 2.5.1 also opened the palette on a double tap of Shift, off the
internal `GUIUtility.beforeEventProcessed` hook. It was removed in 2.6.0, along with its
four preference keys, when the bare-Tab default made a second way in unnecessary.

It is recorded here because it was removed for cause, and the cause generalises: **Shift is
the most overloaded key in the editor**, and every rule that made the gesture safe was a rule
that made it not fire. Shift-click range-selects in the Hierarchy, shift-drag snaps in the
SceneView, and Shift+letter is every capital letter there is. Three consecutive releases went
into one false-positive class alone — the palette opening mid-rename — and the fix landed
only when it stopped trying to infer intent from the event stream and started asking how much
text was in the focused field.

Two findings from it are durable and worth keeping:

**On macOS a bare Shift produces no key event at all.** It is an NSEvent `flagsChanged`, and
it surfaces only as the modifier bits riding on whatever event comes next — typically a
Repaint a millisecond later:

```
[Haste] modifierKeysChanged
[Haste] repaint  key=None  mods=Shift  (was None)
```

Any future gesture on a modifier key has to read modifier *transitions*, not KeyDown/KeyUp,
and consequently cannot tell LeftShift from RightShift.

**An exception in a `beforeEventProcessed` subscriber kills every shortcut in the editor.**
`ShortcutIntegration` attaches lazily via `EditorApplication.delayCall`, so an
`[InitializeOnLoad]` subscriber lands *first* in the multicast list, and anything escaping
it aborts the remaining invocations — including Unity's own. It presents as a Unity bug.
Nothing may escape a handler on that hook.

The honest summary is that the gesture cost more than it earned. One key, in the place the
editor already puts every other shortcut, does the same job with none of it.
