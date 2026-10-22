# RaylibGameFramework.Input

## Mouse movement (0.1.5)

Use the existing binding model with device `MouseAxis`:

```json
{
  "Bindings": [
    { "Device": "MouseAxis", "Input": "MouseXNegative", "Action": "LookLeft" },
    { "Device": "MouseAxis", "Input": "MouseXPositive", "Action": "LookRight" },
    { "Device": "MouseAxis", "Input": "MouseYNegative", "Action": "LookUp" },
    { "Device": "MouseAxis", "Input": "MouseYPositive", "Action": "LookDown" }
  ]
}
```

Device and input names are case-insensitive. `InputBinding` is unchanged.

Call `Update()` once per Raylib frame. It samples `Raylib.GetMouseDelta()` once,
including relative movement when the cursor is disabled. `GetValue(action)`
returns the matching signed component of that frame's delta: X negative is left,
X positive is right, Y negative is up, Y positive is down. The opposite direction
returns zero. Both components can be active on a diagonal.

Values preserve fractional magnitude and are not clamped to [-1, 1], normalized,
smoothed, or multiplied by frame time. These are Raylib mouse delta units per
frame, not stick deflections or velocity. Callers choose sensitivity and any
normalization; use `Math.Abs(value)` if a directional magnitude is wanted.
As with existing bindings, the largest absolute value wins when multiple bindings
map to the same action.

Any nonzero matching movement makes `IsDown` true. `WasPressed` is true when
movement starts, and `WasReleased` when it stops (or reverses out of that
direction). There is no gameplay deadzone. Mouse movement, including movement
below the rebind threshold, marks `KeyboardMouse` as active.

Both unrestricted rebinding and `BeginRebind(action, InputDeviceFamily.KeyboardMouse)`
capture mouse axes. The dominant component must reach 8 delta units in a single
frame; movement is not accumulated. Horizontal wins exact diagonal ties.
Keyboard presses and mouse button presses take precedence over mouse movement.
Gamepad-only rebinding ignores mouse movement. Capture returns the usual
`InputRebindResult(action, "MouseAxis", input)`; call `ApplyRebind(result)`
to replace the action's first keyboard/mouse-family binding. Actions remain
suppressed during capture, matching existing rebinding behavior.

Use `InputController.GetInputDisplayName(binding.Device, binding.Input)` (also
accepts a rebind result's strings) for "Mouse Left", "Mouse Right", "Mouse Up",
and "Mouse Down". `LastPhysicalInputName` uses those same labels for meaningful
mouse movement at the capture threshold. Mouse buttons retain device `Mouse`;
existing gamepad axis and trigger handling is unchanged.

## Validation

```text
dotnet build
dotnet test tests/InputChecks/InputChecks.csproj
```

The xUnit checks compile the production input sources against a
deterministic Raylib polling substitute. They cover direction values, state
transitions, family detection, rebinding, keyboard/mouse/gamepad buttons, sticks,
and triggers. The package build separately verifies the real Raylib-cs API.
Physical hardware and cursor capture still need an interactive smoke test.

## Gameplay ownership and analog input (0.2.0)

Existing `new InputController(config)` remains unrestricted across keyboard,
mouse and Raylib slots 0–3. Existing `GetValue`, action edges, menu thresholds
(sticks/triggers 0.5), and mouse capture threshold (8 delta units) are unchanged.

Opt into ownership per controller instance:

```csharp
var gameplay = new InputController(config, new InputControllerOptions
{
    DeviceFamily = InputDeviceFamily.Gamepad,
    GamepadIndex = 2
});
gameplay.Update();
float right = Math.Abs(gameplay.GetRawValue("MoveRight"));
float left = Math.Abs(gameplay.GetRawValue("MoveLeft"));
float forward = Math.Abs(gameplay.GetRawValue("MoveForward"));
float backward = Math.Abs(gameplay.GetRawValue("MoveBackward"));
Vector2 move = InputMath.ApplyRadialDeadzone(
    new(right - left, forward - backward), 0.18f);
```

`DeviceFamily = KeyboardMouse` excludes all pads. A null family permits both
families; a non-null index restricts only gamepad input. Null index permits all
four slots. The immutable options are validated on construction and
`Configure(options)`; invalid families and indices outside 0–3 throw.
Configure takes effect on the next Update, including rebinding, labels and
active-family detection. ActiveDeviceFamily remains a last-activity hint, not
an ownership or connectivity property.

`GetRawValue(action)` is sampled by Update. It returns signed directional stick
components without the menu threshold, clamped to [-1,1]. Negative direction
bindings therefore return negative values; use Math.Abs for directional
magnitudes. Buttons return 0/1. Bare LeftTrigger/RightTrigger bindings map
Raylib's [-1,1] to [0,1] (rest -1 -> 0; center 0 -> 0.5; full 1 -> 1).
Trigger direction suffixes are unsupported. Mouse values remain signed,
unclamped per-frame deltas, with no time multiplication. Strongest absolute
binding wins; filter by family to avoid mixing mouse displacement with stick
deflection. Non-finite gamepad samples contribute zero.

`InputMath.ApplyRadialDeadzone(vector, deadzone)` accepts finite deadzones in
[0,1), removes drift and rescales remaining length to [0,1] while preserving
direction. Values outside the unit circle are limited to length 1.
Non-finite vectors return zero. Apply it after composing the two stick axes,
separately for movement and look; never apply it to mouse deltas.
It does not affect menu navigation, digital action edges, device detection or
rebind thresholds. Small raw stick movement can be nonzero while IsDown is false.

Action edges describe aggregate filtered state on consecutive Updates.
A lost/disconnected held input releases once; reconnecting while held presses
once. Switching between sources that both hold the same action keeps it down
without a duplicate press. Switching to an idle source releases it.
There is no automatic controller takeover or release-to-arm policy.
Rebinding still suppresses action states; Configure clears axis capture latches,
and disconnection clears the disconnected slot's latches.

Configuration loading, controller construction and ApplyRebind now reject
unsupported devices/inputs and blank binding fields with InvalidDataException.
Supported bindings are defined non-null keyboard keys, mouse buttons, defined
non-Unknown gamepad buttons, the four signed stick axes with Positive/Negative
suffixes, bare triggers, and the four MouseAxis directions shown above.
Names remain case-insensitive. Previously ignored malformed bindings must be
corrected. ApplyRebind validates its replacement before changing the config.

### CCM migration (separate follow-up; CCM is unchanged)

1. Upgrade CCM's Input reference/snapshot to 0.2.0, avoiding duplicate old source
   and package definitions. Keep Config/input.json unchanged; its bindings are valid.
2. Keep DeviceSelection and its release/press/release title gate unchanged.
   After selection completes, create one gameplay InputController with
   DeviceFamily = selected.Family and GamepadIndex set to selected.Gamepad only
   for Gamepad (null for KeyboardMouse). Do not use ActiveDeviceFamily to update
   ownership. Keep the unrestricted menu controller separate.
3. Replace GamepadActions.Value calls in SessionInput with
   Math.Abs(gameplay.GetRawValue(action)), calling gameplay.Update once per frame.
   Compose Move and Look using the existing action differences. For gamepad,
   replace GamepadActions.Deadzone with InputMath.ApplyRadialDeadzone using
   settings.MoveDeadzone and settings.LookDeadzone. Keep keyboard diagonal
   normalization in CCM.
4. Preserve UsesMouse from selection.Selected.Family. Keep mouse sensitivity
   applied to delta with no elapsed-time factor; keep controller look speed
   multiplied by elapsed time in DungeonSimulation. Movement, camera tuning
   and Config/dungeon.json remain CCM-owned.
5. Keep selected-slot availability checking and the disconnected-controller
   keyboard escape route in CCM. Use a separate KeyboardMouse-filtered
   InputController for that fallback and update it each frame. Preserve the
   existing combined backDown/backPressed edge tracking across fallback:
   down = ControllerDisconnected ? keyboard.IsDown("MenuBack")
   : gameplay.IsDown("MenuBack"). This avoids duplicate presses when switching
   between two held sources. For normal gameplay without fallback, the library
   WasPressed/WasReleased APIs suffice.
6. Remove Application/GamepadActions.cs after replacing its callers and migrate
   its tests to the library raw-value/deadzone contract. Its IGamepadSource may
   be removed or retained/renamed solely for CCM's availability policy/tests.
   Keep title-selection tests and verify a second pad cannot take ownership,
   the title confirmation is released before gameplay, and keyboard escape
   remains available when the selected pad disconnects.

No additional title-screen selection API is required for this migration:
CCM already identifies the pressed device/slot and gates its release.
The library consumes that selection; startup routing and title arbitration
remain application policy. Hardware availability can still be queried through
Raylib.IsGamepadAvailable(selected.Gamepad).

Release validation:

```text
dotnet build
dotnet test tests/InputChecks/InputChecks.csproj
dotnet pack -c Release
```
