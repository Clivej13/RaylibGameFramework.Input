using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using RaylibGameFramework.Input;

public sealed class InputChecks
{
    [Xunit.Fact]
    public void PhysicalInputAndRebindingRemainCompatible()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception(message);
        }
        InputController Controller(params (string Device, string Input, string Action)[] bindings) =>
            new(new InputConfig { Bindings = bindings.Select(b => new InputBinding
                { Device = b.Device, Input = b.Input, Action = b.Action }).ToList() });

        string[] names = ["MouseXNegative", "MouseXPositive", "MouseYNegative", "MouseYPositive"];
        string[] labels = ["Mouse Left", "Mouse Right", "Mouse Up", "Mouse Down"];
        Vector2[] deltas = [new(-12, 0), new(12, 0), new(0, -12), new(0, 12)];
        for (int i = 0; i < names.Length; i++)
        {
            Raylib.Reset();
            var config = JsonSerializer.Deserialize<InputConfig>(JsonSerializer.Serialize(new
            {
                Bindings = names.Select(n => new { Device = "MouseAxis", Input = n, Action = n })
            }))!;
            var controller = new InputController(config);
            Raylib.Delta = deltas[i];
            controller.Update();
            Check(Raylib.DeltaReads == 1, "Delta sampled once");
            Check(controller.GetValue(names[i]) == (i % 2 == 0 ? -12f : 12f), "Signed magnitude");
            Check(controller.IsDown(names[i]) && controller.WasPressed(names[i]), "Movement press");
            Check(controller.GetValue(names[i ^ 1]) == 0f, "Opposite direction inactive");
            Check(controller.ActiveDeviceFamily == InputDeviceFamily.KeyboardMouse, "Mouse family");
            Check(controller.LastPhysicalInputName == labels[i], "Live label");
            Check(InputController.GetInputDisplayName("mouseaxis", names[i].ToLowerInvariant()) == labels[i], "Friendly label");
            Check(controller.GetBinding(names[i], InputDeviceFamily.KeyboardMouse) is not null, "Family lookup");
            controller.Update();
            Check(!controller.WasPressed(names[i]) && controller.IsDown(names[i]), "Continuous movement");
            Raylib.Delta = Vector2.Zero;
            controller.Update();
            Check(controller.WasReleased(names[i]) && controller.GetValue(names[i]) == 0, "Movement release");
            controller.Update();
            Check(!controller.WasReleased(names[i]), "Release lasts one update");

            foreach (bool filtered in new[] { false, true })
            {
                if (filtered) controller.BeginRebind("Target", InputDeviceFamily.KeyboardMouse);
                else controller.BeginRebind("Target");
                Raylib.Delta = deltas[i] / 4;
                controller.Update();
                Check(controller.IsRebinding && controller.CompletedRebind is null, "Noise rejected");
                Raylib.Delta = deltas[i] * (8f / 12f);
                controller.Update();
                Check(controller.CompletedRebind == new InputRebindResult("Target", "MouseAxis", names[i]), "Axis capture");
                Check(!controller.IsDown(names[i]), "Actions suppressed during capture");
                controller.ApplyRebind(controller.CompletedRebind!);
                controller.Update();
                Check(controller.GetValue("Target") == (i % 2 == 0 ? -8f : 8f), "Applied rebind");
            }
        }

        Raylib.Reset();
        var axes = Controller(("mouseaxis", "mousexnegative", "Left"), ("MouseAxis", "MouseYPositive", "Down"));
        Raylib.Delta = new(-0.25f, 2.5f);
        axes.Update();
        Check(axes.GetValue("Left") == -0.25f && axes.GetValue("Down") == 2.5f, "Both components and fractional magnitude");
        axes.BeginRebind("Diagonal");
        Raylib.Delta = new(-9, 12);
        axes.Update();
        Check(axes.CompletedRebind?.Input == "MouseYPositive", "Dominant component");
        axes.BeginRebind("Tie");
        Raylib.Delta = new(-8, 8);
        axes.Update();
        Check(axes.CompletedRebind?.Input == "MouseXNegative", "Horizontal tie");

        foreach (string device in new[] { "Keyboard", "Mouse", "GamepadButton" })
        {
            Raylib.Reset();
            string input = device == "Keyboard" ? "A" : device == "Mouse" ? "Left" : "RightFaceDown";
            var controller = Controller((device, input, "Action"));
            Raylib.PadAvailable = true;
            Raylib.KeyDown = Raylib.KeyPressed = device == "Keyboard";
            Raylib.MouseDown = Raylib.MousePressed = device == "Mouse";
            Raylib.PadDown = Raylib.PadPressed = device == "GamepadButton";
            controller.Update();
            Check(controller.GetValue("Action") == 1 && controller.WasPressed("Action"), device + " press");
            controller.Update();
            Check(controller.IsDown("Action") && !controller.WasPressed("Action"), device + " held");
            controller.BeginRebind("New");
            Raylib.Delta = new(20, 0);
            // Mouse clicks and keyboard presses take precedence over movement.
            if (device == "GamepadButton") controller.BeginRebind("New", InputDeviceFamily.Gamepad);
            controller.Update();
            Check(controller.CompletedRebind == new InputRebindResult("New", device, input), device + " capture");
            Raylib.Delta = Vector2.Zero;
            controller.Update();
            Raylib.KeyDown = Raylib.MouseDown = Raylib.PadDown = false;
            controller.Update();
            Check(controller.WasReleased("Action") && !controller.IsDown("Action"), device + " release");
        }

        foreach (var (input, axis, value) in new[]
        {
            ("RightXNegative", GamepadAxis.RightX, -0.75f),
            ("RightXPositive", GamepadAxis.RightX, 0.75f),
            ("RightYNegative", GamepadAxis.RightY, -0.75f),
            ("RightYPositive", GamepadAxis.RightY, 0.75f),
            ("LeftTrigger", GamepadAxis.LeftTrigger, 0.75f),
            ("RightTrigger", GamepadAxis.RightTrigger, 0.75f)
        })
        {
            Raylib.Reset();
            Raylib.PadAvailable = true;
            var controller = Controller(("GamepadAxis", input, "Action"));
            controller.Update();
            Check(controller.GetValue("Action") == 0, "Resting axis/trigger");
            Raylib.Axes[axis] = value;
            controller.Update();
            Check(controller.GetValue("Action") == value && controller.WasPressed("Action"), "Controller magnitude");
            Check(controller.ActiveDeviceFamily == InputDeviceFamily.Gamepad, "Controller family");
            Raylib.Delta = new(0.1f, 0);
            controller.Update();
            Check(controller.ActiveDeviceFamily == InputDeviceFamily.KeyboardMouse, "Small movement changes family");
            controller.BeginRebind("New", InputDeviceFamily.Gamepad);
            Raylib.Axes.Clear();
            controller.Update();
            Raylib.Axes[axis] = value;
            controller.Update();
            Check(controller.CompletedRebind == new InputRebindResult("New", "GamepadAxis", input), "Controller rebind");
            controller.Update();
            Raylib.Axes.Clear();
            controller.Update();
            Check(controller.WasReleased("Action"), "Controller release");
        }

        Raylib.Reset();
        var replacement = Controller(("Keyboard", "A", "Shared"), ("GamepadAxis", "RightXPositive", "Shared"));
        replacement.ApplyRebind(new("Shared", "MouseAxis", "MouseXPositive"));
        Check(replacement.GetBinding("Shared", InputDeviceFamily.KeyboardMouse)?.Device == "MouseAxis", "Replace family");
        Check(replacement.GetBinding("Shared", InputDeviceFamily.Gamepad)?.Device == "GamepadAxis", "Preserve other family");
        replacement.BeginRebind("Cancelled");
        replacement.CancelRebind();
        Raylib.Delta = new(20, 0);
        replacement.Update();
        Check(replacement.CompletedRebind is null && !replacement.IsRebinding, "Cancellation");
        Check(replacement.GetValue("Unknown") == 0, "Unknown action");
        Console.WriteLine($"Passed {checks} input checks.");
    }
}
