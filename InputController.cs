using System.Numerics;
using System.Text;
using Raylib_cs;

namespace RaylibGameFramework.Input;

public sealed class InputController
{
    private const int MaximumGamepads = 4;
    private const float StickThreshold = 0.5f;
    private const float TriggerThreshold = 0.5f;
    private const float MouseAxisCaptureThreshold = 8f;

    private readonly Dictionary<(int Gamepad, GamepadAxis Axis), bool> _activeAxes = [];
    private readonly Dictionary<(string Device, string Input), string> _bindings;
    private readonly List<ConfiguredBinding> _configuredBindings = [];
    private readonly Dictionary<string, InputActionState> _actionStates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly InputConfig _config;
    private Vector2? _previousMousePosition;
    private Vector2 _mouseDelta;

    public InputController(InputConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _config = config;
        _bindings = new Dictionary<(string Device, string Input), string>(InputBindingKeyComparer.Instance);
        RebuildBindings();
    }

    public string LastPhysicalInputName { get; private set; } = "Press an input";

    public string LastActionName { get; private set; } = "Unmapped";

    /// <summary>The device family detected from meaningful physical input during Update.</summary>
    public InputDeviceFamily ActiveDeviceFamily { get; private set; } = InputDeviceFamily.KeyboardMouse;

    public bool IsRebinding => RebindingAction is not null;

    public string? RebindingAction { get; private set; }

    public InputDeviceFamily? RebindingDeviceFamily { get; private set; }

    public InputRebindResult? CompletedRebind { get; private set; }

    public bool IsDown(string action) => GetActionState(action).IsDown;

    public bool WasPressed(string action) => GetActionState(action).WasPressed;

    public bool WasReleased(string action) => GetActionState(action).WasReleased;

    /// <summary>Returns the strongest binding value. Mouse axes return signed, unclamped
    /// per-frame Raylib delta: left/up negative, right/down positive.</summary>
    public float GetValue(string action) => GetActionState(action).Value;

    public InputBinding? GetBinding(string action, InputDeviceFamily deviceFamily)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ValidateDeviceFamily(deviceFamily);

        return _config.Bindings.FirstOrDefault(binding =>
            string.Equals(binding.Action, action, StringComparison.OrdinalIgnoreCase)
            && TryGetDeviceFamily(binding.Device, out InputDeviceFamily bindingFamily)
            && bindingFamily == deviceFamily);
    }

    public void BeginRebind(string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        RebindingAction = action;
        RebindingDeviceFamily = null;
        CompletedRebind = null;
    }

    public void BeginRebind(string action, InputDeviceFamily deviceFamily)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ValidateDeviceFamily(deviceFamily);

        RebindingAction = action;
        RebindingDeviceFamily = deviceFamily;
        CompletedRebind = null;
    }

    public void CancelRebind()
    {
        RebindingAction = null;
        RebindingDeviceFamily = null;
        CompletedRebind = null;
    }

    /// <summary>
    /// Replaces the first binding for the result's action and device family, or adds one when that
    /// family is unbound.
    /// </summary>
    public void ApplyRebind(InputRebindResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.Action);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.Device);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.Input);

        bool hasDeviceFamily = TryGetDeviceFamily(result.Device, out InputDeviceFamily deviceFamily);
        int bindingIndex = _config.Bindings.FindIndex(binding =>
            string.Equals(binding.Action, result.Action, StringComparison.OrdinalIgnoreCase)
            && hasDeviceFamily
            && TryGetDeviceFamily(binding.Device, out InputDeviceFamily bindingFamily)
            && bindingFamily == deviceFamily);
        var replacement = new InputBinding
        {
            Action = result.Action,
            Device = result.Device,
            Input = result.Input
        };

        if (bindingIndex >= 0)
        {
            _config.Bindings[bindingIndex] = replacement;
        }
        else
        {
            _config.Bindings.Add(replacement);
        }

        RebuildBindings();
    }

    public void Update()
    {
        _mouseDelta = Raylib.GetMouseDelta();
        UpdateActiveDeviceFamily();

        if (IsRebinding)
        {
            UpdateRebindCapture();
            ResetActionStates();
            return;
        }

        UpdateActionStates();

        if (TryGetKeyboardInput(out PhysicalInput input)
            || TryGetMouseInput(out input)
            || TryGetMouseAxisInput(out input)
            || TryGetGamepadButtonInput(out input)
            || TryGetGamepadAxisInput(out input))
        {
            LastPhysicalInputName = input.DisplayName;
            LastActionName = _bindings.GetValueOrDefault((input.Device, input.Input), "Unmapped");
        }
    }

    private void UpdateActiveDeviceFamily()
    {
        Vector2 mousePosition = Raylib.GetMousePosition();
        bool mouseMoved = _previousMousePosition is Vector2 previous && mousePosition != previous;
        _previousMousePosition = mousePosition;

        // Match the existing keyboard/mouse-first priority for simultaneous input.
        if (TryGetKeyboardInput(out _)
            || TryGetMouseInput(out _)
            || mouseMoved
            || _mouseDelta != Vector2.Zero
            || Raylib.GetMouseWheelMoveV() != Vector2.Zero)
        {
            ActiveDeviceFamily = InputDeviceFamily.KeyboardMouse;
            return;
        }

        if (TryGetGamepadButtonInput(out _))
        {
            ActiveDeviceFamily = InputDeviceFamily.Gamepad;
            return;
        }

        // Poll independently so device detection never consumes rebind axis edges.
        for (int gamepad = 0; gamepad < MaximumGamepads; gamepad++)
        {
            if (!Raylib.IsGamepadAvailable(gamepad))
            {
                continue;
            }

            foreach (GamepadAxis axis in Enum.GetValues<GamepadAxis>())
            {
                float value = Raylib.GetGamepadAxisMovement(gamepad, axis);
                bool isTrigger = axis is GamepadAxis.LeftTrigger or GamepadAxis.RightTrigger;
                if (isTrigger ? value >= TriggerThreshold : Math.Abs(value) >= StickThreshold)
                {
                    ActiveDeviceFamily = InputDeviceFamily.Gamepad;
                    return;
                }
            }
        }
    }

    private void UpdateRebindCapture()
    {
        PhysicalInput input = default;
        bool captured = RebindingDeviceFamily switch
        {
            InputDeviceFamily.KeyboardMouse =>
                TryGetKeyboardInput(out input)
                || TryGetMouseInput(out input)
                || TryGetMouseAxisInput(out input),
            InputDeviceFamily.Gamepad =>
                TryGetGamepadButtonInput(out input)
                || TryGetGamepadAxisInput(out input),
            null =>
                TryGetKeyboardInput(out input)
                || TryGetMouseInput(out input)
                || TryGetMouseAxisInput(out input)
                || TryGetGamepadButtonInput(out input)
                || TryGetGamepadAxisInput(out input),
            _ => false
        };

        if (!captured)
        {
            return;
        }

        CompletedRebind = new InputRebindResult(RebindingAction!, input.Device, input.Input);
        RebindingAction = null;
        RebindingDeviceFamily = null;
        LastPhysicalInputName = input.DisplayName;
        LastActionName = _bindings.GetValueOrDefault((input.Device, input.Input), "Unmapped");
    }

    private void RebuildBindings()
    {
        _bindings.Clear();
        _configuredBindings.Clear();

        foreach (InputBinding binding in _config.Bindings)
        {
            if (!string.IsNullOrWhiteSpace(binding.Device)
                && !string.IsNullOrWhiteSpace(binding.Input)
                && !string.IsNullOrWhiteSpace(binding.Action))
            {
                _bindings[(binding.Device, binding.Input)] = binding.Action;
                _configuredBindings.Add(new ConfiguredBinding(binding.Device, binding.Input, binding.Action));
                _actionStates.TryAdd(binding.Action, default);
            }
        }
    }

    private void ResetActionStates()
    {
        foreach (string action in _actionStates.Keys.ToArray())
        {
            _actionStates[action] = default;
        }
    }

    private InputActionState GetActionState(string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return _actionStates.GetValueOrDefault(action);
    }

    private void UpdateActionStates()
    {
        var values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        foreach (ConfiguredBinding binding in _configuredBindings)
        {
            float value = GetBindingValue(binding);
            if (Math.Abs(value) > Math.Abs(values.GetValueOrDefault(binding.Action)))
            {
                values[binding.Action] = value;
            }
        }

        foreach (string action in _actionStates.Keys.ToArray())
        {
            InputActionState previous = _actionStates[action];
            float value = values.GetValueOrDefault(action);
            bool isDown = value != 0f;
            _actionStates[action] = new InputActionState(
                isDown,
                !previous.IsDown && isDown,
                previous.IsDown && !isDown,
                value);
        }
    }

    private float GetBindingValue(ConfiguredBinding binding)
    {
        if (binding.Device.Equals("Keyboard", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse(binding.Input, true, out KeyboardKey key)
            && key != KeyboardKey.Null)
        {
            return Raylib.IsKeyDown(key) ? 1f : 0f;
        }

        if (binding.Device.Equals("Mouse", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse(binding.Input, true, out MouseButton mouseButton))
        {
            return Raylib.IsMouseButtonDown(mouseButton) ? 1f : 0f;
        }

        if (binding.Device.Equals("MouseAxis", StringComparison.OrdinalIgnoreCase))
        {
            return binding.Input.ToUpperInvariant() switch
            {
                "MOUSEXNEGATIVE" => Math.Min(_mouseDelta.X, 0f),
                "MOUSEXPOSITIVE" => Math.Max(_mouseDelta.X, 0f),
                "MOUSEYNEGATIVE" => Math.Min(_mouseDelta.Y, 0f),
                "MOUSEYPOSITIVE" => Math.Max(_mouseDelta.Y, 0f),
                _ => 0f
            };
        }

        if (binding.Device.Equals("GamepadButton", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse(binding.Input, true, out GamepadButton gamepadButton)
            && gamepadButton != GamepadButton.Unknown)
        {
            for (int gamepad = 0; gamepad < MaximumGamepads; gamepad++)
            {
                if (Raylib.IsGamepadAvailable(gamepad)
                    && Raylib.IsGamepadButtonDown(gamepad, gamepadButton))
                {
                    return 1f;
                }
            }

            return 0f;
        }

        if (binding.Device.Equals("GamepadAxis", StringComparison.OrdinalIgnoreCase)
            && TryParseAxisInput(binding.Input, out GamepadAxis axis, out AxisDirection direction))
        {
            float strongestValue = 0f;

            for (int gamepad = 0; gamepad < MaximumGamepads; gamepad++)
            {
                if (!Raylib.IsGamepadAvailable(gamepad))
                {
                    continue;
                }

                float value = Raylib.GetGamepadAxisMovement(gamepad, axis);
                bool isTrigger = axis is GamepadAxis.LeftTrigger or GamepadAxis.RightTrigger;
                bool isActive = isTrigger
                    ? value >= TriggerThreshold
                    : direction == AxisDirection.Negative
                        ? value <= -StickThreshold
                        : value >= StickThreshold;

                if (isActive && Math.Abs(value) > Math.Abs(strongestValue))
                {
                    strongestValue = value;
                }
            }

            return strongestValue;
        }

        return 0f;
    }

    private static bool TryParseAxisInput(
        string input,
        out GamepadAxis axis,
        out AxisDirection direction)
    {
        if (input.EndsWith("Negative", StringComparison.OrdinalIgnoreCase))
        {
            direction = AxisDirection.Negative;
            return Enum.TryParse(input[..^"Negative".Length], true, out axis)
                && axis is not GamepadAxis.LeftTrigger and not GamepadAxis.RightTrigger;
        }

        if (input.EndsWith("Positive", StringComparison.OrdinalIgnoreCase))
        {
            direction = AxisDirection.Positive;
            return Enum.TryParse(input[..^"Positive".Length], true, out axis)
                && axis is not GamepadAxis.LeftTrigger and not GamepadAxis.RightTrigger;
        }

        direction = AxisDirection.Positive;
        return Enum.TryParse(input, true, out axis)
            && axis is GamepadAxis.LeftTrigger or GamepadAxis.RightTrigger;
    }

    private static bool TryGetKeyboardInput(out PhysicalInput input)
    {
        foreach (KeyboardKey key in Enum.GetValues<KeyboardKey>())
        {
            if (key != KeyboardKey.Null && Raylib.IsKeyPressed(key))
            {
                string name = key.ToString();
                input = new PhysicalInput("Keyboard", name, FriendlyName(name));
                return true;
            }
        }

        input = default;
        return false;
    }

    private static bool TryGetMouseInput(out PhysicalInput input)
    {
        foreach (MouseButton button in Enum.GetValues<MouseButton>())
        {
            if (Raylib.IsMouseButtonPressed(button))
            {
                string name = button.ToString();
                input = new PhysicalInput("Mouse", name, $"Mouse {FriendlyName(name)}");
                return true;
            }
        }

        input = default;
        return false;
    }

    private bool TryGetMouseAxisInput(out PhysicalInput input)
    {
        // Capture the dominant component; horizontal wins exact diagonal ties.
        bool horizontal = Math.Abs(_mouseDelta.X) >= Math.Abs(_mouseDelta.Y);
        float value = horizontal ? _mouseDelta.X : _mouseDelta.Y;
        if (Math.Abs(value) >= MouseAxisCaptureThreshold)
        {
            string name = $"Mouse{(horizontal ? "X" : "Y")}{(value < 0f ? "Negative" : "Positive")}";
            input = new PhysicalInput("MouseAxis", name, GetInputDisplayName("MouseAxis", name));
            return true;
        }

        input = default;
        return false;
    }

    /// <summary>Returns a friendly physical input name for configuration and rebind UI.</summary>
    public static string GetInputDisplayName(string device, string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        if (device.Equals("MouseAxis", StringComparison.OrdinalIgnoreCase))
        {
            return input.ToUpperInvariant() switch
            {
                "MOUSEXNEGATIVE" => "Mouse Left",
                "MOUSEXPOSITIVE" => "Mouse Right",
                "MOUSEYNEGATIVE" => "Mouse Up",
                "MOUSEYPOSITIVE" => "Mouse Down",
                _ => FriendlyName(input)
            };
        }

        string prefix = device.Equals("Mouse", StringComparison.OrdinalIgnoreCase) ? "Mouse "
            : device.Equals("GamepadButton", StringComparison.OrdinalIgnoreCase)
                || device.Equals("GamepadAxis", StringComparison.OrdinalIgnoreCase) ? "Gamepad "
            : string.Empty;
        return prefix + FriendlyName(input);
    }

    private static bool TryGetGamepadButtonInput(out PhysicalInput input)
    {
        for (int gamepad = 0; gamepad < MaximumGamepads; gamepad++)
        {
            if (!Raylib.IsGamepadAvailable(gamepad))
            {
                continue;
            }

            foreach (GamepadButton button in Enum.GetValues<GamepadButton>())
            {
                if (button != GamepadButton.Unknown && Raylib.IsGamepadButtonPressed(gamepad, button))
                {
                    string name = button.ToString();
                    input = new PhysicalInput(
                        "GamepadButton",
                        name,
                        $"Gamepad {FriendlyName(name)}");
                    return true;
                }
            }
        }

        input = default;
        return false;
    }

    private bool TryGetGamepadAxisInput(out PhysicalInput input)
    {
        for (int gamepad = 0; gamepad < MaximumGamepads; gamepad++)
        {
            if (!Raylib.IsGamepadAvailable(gamepad))
            {
                continue;
            }

            foreach (GamepadAxis axis in Enum.GetValues<GamepadAxis>())
            {
                float value = Raylib.GetGamepadAxisMovement(gamepad, axis);
                bool isTrigger = axis is GamepadAxis.LeftTrigger or GamepadAxis.RightTrigger;
                bool isActive = isTrigger ? value >= TriggerThreshold : Math.Abs(value) >= StickThreshold;
                var axisKey = (gamepad, axis);
                bool wasActive = _activeAxes.GetValueOrDefault(axisKey);
                _activeAxes[axisKey] = isActive;

                if (isActive && !wasActive)
                {
                    string direction = !isTrigger && value < 0
                        ? "Negative"
                        : !isTrigger
                            ? "Positive"
                            : string.Empty;
                    string axisName = axis.ToString();
                    string displayDirection = direction.Length > 0 ? $" {direction}" : string.Empty;
                    input = new PhysicalInput(
                        "GamepadAxis",
                        $"{axisName}{direction}",
                        $"Gamepad {FriendlyName(axisName)}{displayDirection} ({value:0.00})");
                    return true;
                }
            }
        }

        input = default;
        return false;
    }

    private static string FriendlyName(string name)
    {
        var result = new StringBuilder(name.Length + 4);

        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(name[index - 1]))
            {
                result.Append(' ');
            }

            result.Append(character);
        }

        return result.ToString();
    }

    private static bool TryGetDeviceFamily(string device, out InputDeviceFamily deviceFamily)
    {
        if (device.Equals("Keyboard", StringComparison.OrdinalIgnoreCase)
            || device.Equals("Mouse", StringComparison.OrdinalIgnoreCase)
            || device.Equals("MouseAxis", StringComparison.OrdinalIgnoreCase))
        {
            deviceFamily = InputDeviceFamily.KeyboardMouse;
            return true;
        }

        if (device.Equals("GamepadButton", StringComparison.OrdinalIgnoreCase)
            || device.Equals("GamepadAxis", StringComparison.OrdinalIgnoreCase))
        {
            deviceFamily = InputDeviceFamily.Gamepad;
            return true;
        }

        deviceFamily = default;
        return false;
    }

    private static void ValidateDeviceFamily(InputDeviceFamily deviceFamily)
    {
        if (deviceFamily is not InputDeviceFamily.KeyboardMouse and not InputDeviceFamily.Gamepad)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceFamily));
        }
    }

    private readonly record struct PhysicalInput(string Device, string Input, string DisplayName);

    private readonly record struct ConfiguredBinding(string Device, string Input, string Action);

    private readonly record struct InputActionState(
        bool IsDown,
        bool WasPressed,
        bool WasReleased,
        float Value);

    private enum AxisDirection
    {
        Positive,
        Negative
    }

    private sealed class InputBindingKeyComparer : IEqualityComparer<(string Device, string Input)>
    {
        public static InputBindingKeyComparer Instance { get; } = new();

        public bool Equals((string Device, string Input) x, (string Device, string Input) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Device, y.Device)
            && StringComparer.OrdinalIgnoreCase.Equals(x.Input, y.Input);

        public int GetHashCode((string Device, string Input) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Device),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Input));
    }
}
