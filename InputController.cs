using System.Text;
using Raylib_cs;

namespace RaylibGameFramework.Input;

public sealed class InputController
{
    private const int MaximumGamepads = 4;
    private const float StickThreshold = 0.5f;
    private const float TriggerThreshold = 0.5f;

    private readonly Dictionary<(int Gamepad, GamepadAxis Axis), bool> _activeAxes = [];
    private readonly Dictionary<(string Device, string Input), string> _bindings;
    private readonly List<ConfiguredBinding> _configuredBindings = [];
    private readonly Dictionary<string, InputActionState> _actionStates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly InputConfig _config;

    public InputController(InputConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _config = config;
        _bindings = new Dictionary<(string Device, string Input), string>(InputBindingKeyComparer.Instance);
        RebuildBindings();
    }

    public string LastPhysicalInputName { get; private set; } = "Press an input";

    public string LastActionName { get; private set; } = "Unmapped";

    public bool IsRebinding => RebindingAction is not null;

    public string? RebindingAction { get; private set; }

    public InputRebindResult? CompletedRebind { get; private set; }

    public bool IsDown(string action) => GetActionState(action).IsDown;

    public bool WasPressed(string action) => GetActionState(action).WasPressed;

    public bool WasReleased(string action) => GetActionState(action).WasReleased;

    public float GetValue(string action) => GetActionState(action).Value;

    public void BeginRebind(string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        RebindingAction = action;
        CompletedRebind = null;
    }

    public void CancelRebind()
    {
        RebindingAction = null;
        CompletedRebind = null;
    }

    /// <summary>
    /// Replaces the first binding for the result's action, or adds one when the action is unbound.
    /// </summary>
    public void ApplyRebind(InputRebindResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.Action);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.Device);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.Input);

        int bindingIndex = _config.Bindings.FindIndex(binding =>
            string.Equals(binding.Action, result.Action, StringComparison.OrdinalIgnoreCase));
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
        if (IsRebinding)
        {
            UpdateRebindCapture();
            ResetActionStates();
            return;
        }

        UpdateActionStates();

        if (TryGetKeyboardInput(out PhysicalInput input)
            || TryGetMouseInput(out input)
            || TryGetGamepadButtonInput(out input)
            || TryGetGamepadAxisInput(out input))
        {
            LastPhysicalInputName = input.DisplayName;
            LastActionName = _bindings.GetValueOrDefault((input.Device, input.Input), "Unmapped");
        }
    }

    private void UpdateRebindCapture()
    {
        if (!(TryGetKeyboardInput(out PhysicalInput input)
            || TryGetMouseInput(out input)
            || TryGetGamepadButtonInput(out input)
            || TryGetGamepadAxisInput(out input)))
        {
            return;
        }

        CompletedRebind = new InputRebindResult(RebindingAction!, input.Device, input.Input);
        RebindingAction = null;
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

    private static float GetBindingValue(ConfiguredBinding binding)
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
