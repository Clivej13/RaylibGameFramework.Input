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

    public InputController(InputConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _bindings = new Dictionary<(string Device, string Input), string>(InputBindingKeyComparer.Instance);
        foreach (InputBinding binding in config.Bindings)
        {
            if (!string.IsNullOrWhiteSpace(binding.Device)
                && !string.IsNullOrWhiteSpace(binding.Input)
                && !string.IsNullOrWhiteSpace(binding.Action))
            {
                _bindings[(binding.Device, binding.Input)] = binding.Action;
            }
        }
    }

    public string LastPhysicalInputName { get; private set; } = "Press an input";

    public string LastActionName { get; private set; } = "Unmapped";

    public void Update()
    {
        if (TryGetKeyboardInput(out PhysicalInput input)
            || TryGetMouseInput(out input)
            || TryGetGamepadButtonInput(out input)
            || TryGetGamepadAxisInput(out input))
        {
            LastPhysicalInputName = input.DisplayName;
            LastActionName = _bindings.GetValueOrDefault((input.Device, input.Input), "Unmapped");
        }
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
