using System.Numerics;

namespace RaylibGameFramework.Input;

/// <summary>Null filters retain unrestricted keyboard/mouse and all four Raylib gamepad slots.</summary>
public sealed record InputControllerOptions
{
    public InputDeviceFamily? DeviceFamily { get; init; }
    public int? GamepadIndex { get; init; }

    public void Validate()
    {
        if (DeviceFamily is not null and not InputDeviceFamily.KeyboardMouse and not InputDeviceFamily.Gamepad)
            throw new ArgumentOutOfRangeException(nameof(DeviceFamily));
        if (GamepadIndex is < 0 or >= 4)
            throw new ArgumentOutOfRangeException(nameof(GamepadIndex));
    }
}

public static class InputMath
{
    /// <summary>Removes radial drift, rescales remaining magnitude to [0,1], and preserves direction.</summary>
    public static Vector2 ApplyRadialDeadzone(Vector2 value, float deadzone)
    {
        if (!float.IsFinite(deadzone) || deadzone is < 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(deadzone));
        float length = value.Length();
        if (!float.IsFinite(length) || length <= deadzone || length == 0) return Vector2.Zero;
        return value / length * ((Math.Min(length, 1) - deadzone) / (1 - deadzone));
    }
}
