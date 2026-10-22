using System.Numerics;

// Deterministic polling substitute: exercise the production controller without a window or hardware.
namespace Raylib_cs;

public enum KeyboardKey { Null, A }
public enum MouseButton { Left, Right }
public enum GamepadButton { Unknown, RightFaceDown }
public enum GamepadAxis { LeftX, LeftY, RightX, RightY, LeftTrigger, RightTrigger }

public static class Raylib
{
    public static Vector2 Delta, Position, Wheel;
    public static bool KeyDown, KeyPressed, MouseDown, MousePressed, PadAvailable, PadDown, PadPressed;
    public static readonly Dictionary<GamepadAxis, float> Axes = [];
    public static readonly HashSet<int> Connected = [];
    public static readonly HashSet<int> ButtonsDown = [], ButtonsPressed = [];
    public static readonly Dictionary<(int, GamepadAxis), float> PadAxes = [];
    public static int DeltaReads;
    public static Vector2 GetMouseDelta() { DeltaReads++; return Delta; }
    public static Vector2 GetMousePosition() => Position;
    public static Vector2 GetMouseWheelMoveV() => Wheel;
    public static bool IsKeyDown(KeyboardKey key) => KeyDown;
    public static bool IsKeyPressed(KeyboardKey key) => KeyPressed;
    public static bool IsMouseButtonDown(MouseButton button) => button == MouseButton.Left && MouseDown;
    public static bool IsMouseButtonPressed(MouseButton button) => button == MouseButton.Left && MousePressed;
    public static bool IsGamepadAvailable(int gamepad) => (gamepad == 0 && PadAvailable) || Connected.Contains(gamepad);
    public static bool IsGamepadButtonDown(int gamepad, GamepadButton button) => (gamepad == 0 && PadDown) || ButtonsDown.Contains(gamepad);
    public static bool IsGamepadButtonPressed(int gamepad, GamepadButton button) => (gamepad == 0 && PadPressed) || ButtonsPressed.Contains(gamepad);
    public static float GetGamepadAxisMovement(int gamepad, GamepadAxis axis) =>
        PadAxes.GetValueOrDefault((gamepad, axis), gamepad == 0 ? Axes.GetValueOrDefault(axis, axis is GamepadAxis.LeftTrigger or GamepadAxis.RightTrigger ? -1f : 0f) : axis is GamepadAxis.LeftTrigger or GamepadAxis.RightTrigger ? -1f : 0f);
    public static void Reset()
    {
        Delta = Position = Wheel = Vector2.Zero;
        KeyDown = KeyPressed = MouseDown = MousePressed = PadAvailable = PadDown = PadPressed = false;
        Axes.Clear();
        Connected.Clear(); ButtonsDown.Clear(); ButtonsPressed.Clear(); PadAxes.Clear();
        DeltaReads = 0;
    }
}
