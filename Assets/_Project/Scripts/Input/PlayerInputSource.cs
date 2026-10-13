using UnityEngine;
using UnityEngine.InputSystem;

namespace PointOfYou
{
    /// <summary>
    /// Fuente de input de un jugador local. Se lee directo de los dispositivos (en vez de usar
    /// PlayerInput) para poder tener dos jugadores en el mismo teclado sin pelear con el pairing.
    /// </summary>
    public abstract class PlayerInputSource
    {
        public abstract string Label { get; }
        public abstract bool IsConnected { get; }
        public abstract Vector2 Move { get; }
        /// <summary>True solo en el frame en que se apretó.</summary>
        public abstract bool JumpPressed { get; }
        public abstract bool GrabHeld { get; }
        public abstract bool ResetPressed { get; }
        public abstract bool SameDevice(PlayerInputSource other);
    }

    public sealed class KeyboardInputSource : PlayerInputSource
    {
        public enum Side { Left, Right }

        public Side KeyboardSide { get; }

        readonly Key up, down, left, right, reset;
        readonly Key[] jump, grab;

        public KeyboardInputSource(Side side)
        {
            KeyboardSide = side;
            if (side == Side.Left)
            {
                up = Key.W; down = Key.S; left = Key.A; right = Key.D; reset = Key.R;
                jump = new[] { Key.Space };
                grab = new[] { Key.LeftShift, Key.E };
            }
            else
            {
                up = Key.UpArrow; down = Key.DownArrow; left = Key.LeftArrow; right = Key.RightArrow; reset = Key.Backspace;
                jump = new[] { Key.Enter, Key.NumpadEnter, Key.Numpad0 };
                grab = new[] { Key.RightShift, Key.RightCtrl };
            }
        }

        /// <summary>Tecla para sumarse a la partida con esta mitad del teclado.</summary>
        public static bool JoinPressed(Side side)
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            return side == Side.Left
                ? kb.spaceKey.wasPressedThisFrame
                : kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
        }

        public override string Label => KeyboardSide == Side.Left ? "Teclado (WASD)" : "Teclado (flechas)";
        public override bool IsConnected => Keyboard.current != null;

        public override Vector2 Move
        {
            get
            {
                var kb = Keyboard.current;
                if (kb == null) return Vector2.zero;
                var v = new Vector2(
                    (kb[right].isPressed ? 1f : 0f) - (kb[left].isPressed ? 1f : 0f),
                    (kb[up].isPressed ? 1f : 0f) - (kb[down].isPressed ? 1f : 0f));
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        public override bool JumpPressed => AnyPressedThisFrame(jump);
        public override bool GrabHeld => AnyHeld(grab);
        public override bool ResetPressed => Keyboard.current != null && Keyboard.current[reset].wasPressedThisFrame;

        public override bool SameDevice(PlayerInputSource other) =>
            other is KeyboardInputSource k && k.KeyboardSide == KeyboardSide;

        static bool AnyPressedThisFrame(Key[] keys)
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            foreach (var k in keys)
                if (kb[k].wasPressedThisFrame) return true;
            return false;
        }

        static bool AnyHeld(Key[] keys)
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            foreach (var k in keys)
                if (kb[k].isPressed) return true;
            return false;
        }
    }

    public sealed class GamepadInputSource : PlayerInputSource
    {
        const float Deadzone = 0.15f;

        public Gamepad Pad { get; }

        public GamepadInputSource(Gamepad pad) => Pad = pad;

        public override string Label => Pad != null ? Pad.displayName : "Joystick";
        public override bool IsConnected => Pad != null && Pad.added;

        public override Vector2 Move
        {
            get
            {
                if (!IsConnected) return Vector2.zero;
                var v = Pad.leftStick.ReadValue() + Pad.dpad.ReadValue();
                return v.magnitude < Deadzone ? Vector2.zero : Vector2.ClampMagnitude(v, 1f);
            }
        }

        public override bool JumpPressed => IsConnected && Pad.buttonSouth.wasPressedThisFrame;

        public override bool GrabHeld => IsConnected &&
            (Pad.rightTrigger.isPressed || Pad.rightShoulder.isPressed || Pad.buttonWest.isPressed || Pad.leftTrigger.isPressed);

        public override bool ResetPressed => IsConnected && Pad.selectButton.wasPressedThisFrame;

        public override bool SameDevice(PlayerInputSource other) =>
            other is GamepadInputSource g && g.Pad == Pad;
    }

    /// <summary>Input manejado por código: para tests automáticos y, más adelante, bots.</summary>
    public sealed class ScriptedInputSource : PlayerInputSource
    {
        readonly string label;
        bool jumpQueued, resetQueued;

        public ScriptedInputSource(string label) => this.label = label;

        public Vector2 MoveValue { get; set; }
        public bool Grab { get; set; }
        public void PressJump() => jumpQueued = true;
        public void PressReset() => resetQueued = true;

        public override string Label => label;
        public override bool IsConnected => true;
        public override Vector2 Move => Vector2.ClampMagnitude(MoveValue, 1f);
        public override bool GrabHeld => Grab;

        public override bool JumpPressed
        {
            get { bool j = jumpQueued; jumpQueued = false; return j; }
        }

        public override bool ResetPressed
        {
            get { bool r = resetQueued; resetQueued = false; return r; }
        }

        public override bool SameDevice(PlayerInputSource other) => ReferenceEquals(this, other);
    }
}
