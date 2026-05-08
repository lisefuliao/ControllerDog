using DouDouDeDou.Models;
using Vortice.XInput;

namespace DouDouDeDou.Services;

public sealed class XInputControllerService
{
    private const byte TriggerThreshold = 30;
    private const uint MaxUserIndex = 4;

    public bool TryGetFirstConnectedState(out ControllerState state)
    {
        for (uint index = 0; index < MaxUserIndex; index++)
        {
            if (TryGetState(index, out state))
            {
                return true;
            }
        }

        state = new ControllerState();
        return false;
    }

    public bool TryGetFirstConnectedIndex(out uint userIndex)
    {
        for (uint index = 0; index < MaxUserIndex; index++)
        {
            if (XInput.GetState(index, out _))
            {
                userIndex = index;
                return true;
            }
        }

        userIndex = 0;
        return false;
    }

    public bool TryGetState(uint userIndex, out ControllerState state)
    {
        state = new ControllerState();

        if (!XInput.GetState(userIndex, out var xState))
        {
            return false;
        }

        var gamepad = xState.Gamepad;
        var buttons = gamepad.Buttons;
        var pressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddIf(buttons, GamepadButtons.A, "A", pressed);
        AddIf(buttons, GamepadButtons.B, "B", pressed);
        AddIf(buttons, GamepadButtons.X, "X", pressed);
        AddIf(buttons, GamepadButtons.Y, "Y", pressed);
        AddIf(buttons, GamepadButtons.LeftShoulder, "LB", pressed);
        AddIf(buttons, GamepadButtons.RightShoulder, "RB", pressed);
        AddIf(buttons, GamepadButtons.Back, "Back", pressed);
        AddIf(buttons, GamepadButtons.Start, "Start", pressed);
        AddIf(buttons, GamepadButtons.LeftThumb, "LeftStick", pressed);
        AddIf(buttons, GamepadButtons.RightThumb, "RightStick", pressed);
        AddIf(buttons, GamepadButtons.DPadUp, "DPadUp", pressed);
        AddIf(buttons, GamepadButtons.DPadDown, "DPadDown", pressed);
        AddIf(buttons, GamepadButtons.DPadLeft, "DPadLeft", pressed);
        AddIf(buttons, GamepadButtons.DPadRight, "DPadRight", pressed);

        if (gamepad.LeftTrigger >= TriggerThreshold)
        {
            pressed.Add("LT");
        }

        if (gamepad.RightTrigger >= TriggerThreshold)
        {
            pressed.Add("RT");
        }

        state = new ControllerState
        {
            IsConnected = true,
            DeviceName = $"XInput 手柄 #{userIndex + 1}",
            ControllerType = ControllerType.XInput,
            InputMode = "XInput",
            XInputUserIndex = (int)userIndex,
            PressedButtons = pressed,
            Timestamp = DateTimeOffset.Now,
            RawSummary = $"LT={gamepad.LeftTrigger}, RT={gamepad.RightTrigger}, Buttons={buttons}"
        };

        return true;
    }

    private static void AddIf(GamepadButtons buttons, GamepadButtons flag, string name, ISet<string> pressed)
    {
        if ((buttons & flag) != 0)
        {
            pressed.Add(name);
        }
    }
}
