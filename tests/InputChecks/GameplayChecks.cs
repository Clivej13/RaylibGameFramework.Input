using System.Numerics;
using Raylib_cs;
using RaylibGameFramework.Input;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class GameplayChecks
{
    private static InputController Create(InputControllerOptions? options = null)
    {
        Raylib.Reset();
        return new(new InputConfig { Bindings = [
            new() { Device = "Keyboard", Input = "A", Action = "Back" },
            new() { Device = "Mouse", Input = "Left", Action = "Click" },
            new() { Device = "MouseAxis", Input = "MouseXNegative", Action = "Look" },
            new() { Device = "GamepadButton", Input = "RightFaceDown", Action = "Back" },
            new() { Device = "GamepadAxis", Input = "LeftXPositive", Action = "Right" },
            new() { Device = "GamepadAxis", Input = "LeftYNegative", Action = "Up" },
            new() { Device = "GamepadAxis", Input = "LeftTrigger", Action = "Trigger" }
        ] }, options ?? new());
    }

    [Fact]
    public void OnlySelectedPadContributesAndDisconnectProducesEdges()
    {
        var c = Create(new() { DeviceFamily = InputDeviceFamily.Gamepad, GamepadIndex = 2 });
        Raylib.Connected.UnionWith([0, 2]);
        Raylib.ButtonsDown.Add(0);
        Raylib.PadAxes[(0, GamepadAxis.LeftX)] = 1;
        c.Update();
        Assert.False(c.IsDown("Back"));
        Assert.Equal(0, c.GetRawValue("Right"));
        Raylib.ButtonsDown.Add(2);
        Raylib.PadAxes[(2, GamepadAxis.LeftX)] = .3f;
        c.Update();
        Assert.True(c.WasPressed("Back"));
        Assert.Equal(.3f, c.GetRawValue("Right"));
        Assert.Equal(0, c.GetValue("Right"));
        c.Update();
        Assert.False(c.WasPressed("Back"));
        Raylib.Connected.Remove(2);
        c.Update();
        Assert.True(c.WasReleased("Back"));
        Assert.Equal(0, c.GetRawValue("Right"));
        c.Update();
        Assert.False(c.WasReleased("Back"));
        Raylib.Connected.Add(2);
        c.Update();
        Assert.True(c.WasPressed("Back"));
        Assert.Equal(.3f, c.GetRawValue("Right"));
    }

    [Fact]
    public void FilterAndSelectionChangesPreserveAggregateHeldState()
    {
        var c = Create(new() { DeviceFamily = InputDeviceFamily.Gamepad, GamepadIndex = 0 });
        Raylib.Connected.UnionWith([0, 2]);
        Raylib.ButtonsDown.UnionWith([0, 2]);
        Raylib.KeyDown = true;
        Raylib.MouseDown = true;
        Raylib.Delta = new(-20, 0);
        c.Update();
        Assert.True(c.WasPressed("Back"));
        Assert.Equal(0, c.GetRawValue("Look"));
        Assert.False(c.IsDown("Click"));
        c.Configure(c.Options with { GamepadIndex = 2 });
        c.Update();
        Assert.True(c.IsDown("Back"));
        Assert.False(c.WasPressed("Back"));
        c.Configure(new() { DeviceFamily = InputDeviceFamily.KeyboardMouse });
        c.Update();
        Assert.True(c.IsDown("Back"));
        Assert.False(c.WasPressed("Back"));
        Assert.Equal(-20, c.GetRawValue("Look"));
        Assert.True(c.IsDown("Click"));
        Raylib.KeyDown = false;
        c.Update();
        Assert.True(c.WasReleased("Back"));
        c.Configure(new() { DeviceFamily = InputDeviceFamily.Gamepad, GamepadIndex = 1 });
        c.Update();
        Assert.False(c.IsDown("Back"));
        c.Configure(new());
        c.Update();
        Assert.True(c.WasPressed("Back"));
    }

    [Fact]
    public void RawSticksAndRadialDeadzonePreserveAnalogRange()
    {
        var c = Create(new() { GamepadIndex = 2 });
        Raylib.Connected.Add(2);
        foreach (float x in new[] { .05f, .3f, 1f })
        {
            Raylib.PadAxes[(2, GamepadAxis.LeftX)] = x;
            Raylib.PadAxes[(2, GamepadAxis.LeftY)] = -x;
            c.Update();
            Assert.Equal(x, c.GetRawValue("Right"));
            Assert.Equal(-x, c.GetRawValue("Up"));
        }
        Assert.Equal(Vector2.Zero, InputMath.ApplyRadialDeadzone(new(.05f, .05f), .18f));
        Assert.InRange(InputMath.ApplyRadialDeadzone(new(.3f, 0), .18f).X, .146f, .147f);
        Assert.Equal(Vector2.UnitX, InputMath.ApplyRadialDeadzone(Vector2.UnitX, .18f));
        var diagonal = InputMath.ApplyRadialDeadzone(Vector2.One, .18f);
        Assert.InRange(diagonal.Length(), .99999f, 1.00001f);
        Assert.Equal(diagonal.X, diagonal.Y);
        Assert.Equal(Vector2.Zero, InputMath.ApplyRadialDeadzone(Vector2.Zero, 0));
        Raylib.PadAxes[(2, GamepadAxis.LeftX)] = float.NaN;
        c.Update();
        Assert.Equal(0, c.GetRawValue("Right"));
        Assert.Equal(0, c.GetRawValue("Trigger"));
        Raylib.PadAxes[(2, GamepadAxis.LeftTrigger)] = 0;
        c.Update();
        Assert.Equal(.5f, c.GetRawValue("Trigger"));
        Assert.Equal(0, c.GetValue("Trigger"));
    }

    [Fact]
    public void RebindingHonorsSelectionAndResetsDisconnectedAxisLatch()
    {
        var c = Create(new() { DeviceFamily = InputDeviceFamily.Gamepad, GamepadIndex = 2 });
        Raylib.Connected.UnionWith([0, 2]);
        Raylib.KeyPressed = Raylib.MousePressed = true;
        Raylib.ButtonsPressed.Add(0);
        Raylib.PadAxes[(2, GamepadAxis.LeftX)] = .3f;
        c.BeginRebind("New");
        c.Update();
        Assert.True(c.IsRebinding);
        Raylib.PadAxes[(2, GamepadAxis.LeftX)] = .8f;
        c.Update();
        Assert.Equal("LeftXPositive", c.CompletedRebind?.Input);
        c.BeginRebind("Again");
        Raylib.Connected.Remove(2);
        c.Update();
        Assert.True(c.IsRebinding);
        Raylib.Connected.Add(2);
        c.Update();
        Assert.Equal("LeftXPositive", c.CompletedRebind?.Input);
        c.Configure(new() { DeviceFamily = InputDeviceFamily.KeyboardMouse });
        Raylib.KeyPressed = Raylib.MousePressed = false;
        c.BeginRebind("KeyboardOnly");
        c.Update();
        Assert.True(c.IsRebinding);
        Raylib.Delta = new(-8, 0);
        c.Update();
        Assert.Equal("MouseXNegative", c.CompletedRebind?.Input);
    }

    [Theory]
    [InlineData("UnknownDevice", "A")]
    [InlineData("Keyboard", "999")]
    [InlineData("GamepadButton", "Unknown")]
    [InlineData("GamepadAxis", "99Positive")]
    [InlineData("GamepadAxis", "LeftTriggerNegative")]
    [InlineData("GamepadAxis", "LeftX")]
    [InlineData("MouseAxis", "MouseZPositive")]
    public void InvalidBindingsFailBeforeMutation(string device, string input)
    {
        var c = Create();
        Assert.Throws<InvalidDataException>(() => c.ApplyRebind(new("Back", device, input)));
        Assert.Equal("A", c.GetBinding("Back", InputDeviceFamily.KeyboardMouse)?.Input);
        Assert.Throws<InvalidDataException>(() => new InputController(new() {
            Bindings = [new() { Device = device, Input = input, Action = "Action" }] }));
    }

    [Fact]
    public void InvalidOptionsAndDeadzonesFail()
    {
        var c = Create();
        foreach (int index in new[] { -1, 4 })
            Assert.Throws<ArgumentOutOfRangeException>(() => c.Configure(new() { GamepadIndex = index }));
        Assert.Throws<ArgumentOutOfRangeException>(() => c.Configure(new() { DeviceFamily = (InputDeviceFamily)99 }));
        foreach (float value in new[] { -1f, 1f, float.NaN, float.PositiveInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => InputMath.ApplyRadialDeadzone(Vector2.One, value));
    }
}
