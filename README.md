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
