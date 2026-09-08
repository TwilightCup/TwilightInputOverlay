using System;
using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// Reads the current keyboard/mouse input state for each HUD key. Prefers
    /// the game's own <see cref="Options.keyboardBindings"/> actions so the HUD
    /// lights up with the actual bound action (movement, jump, play dead, and
    /// the left/right hand actions — mouse buttons by default), and falls back
    /// to the fixed default keys when the bindings are not ready yet.
    /// </summary>
    internal static class InputState
    {
        public static bool Forward => Read(a => a.Forward.IsPressed, () => Input.GetKey(KeyCode.W));
        public static bool Back => Read(a => a.Back.IsPressed, () => Input.GetKey(KeyCode.S));
        public static bool Left => Read(a => a.Left.IsPressed, () => Input.GetKey(KeyCode.A));
        public static bool Right => Read(a => a.Right.IsPressed, () => Input.GetKey(KeyCode.D));
        public static bool Jump => Read(a => a.Jump.IsPressed, () => Input.GetKey(KeyCode.Space));
        public static bool PlayDead => Read(a => a.Unconscious.IsPressed, () => Input.GetKey(KeyCode.Y));
        public static bool LeftHand => Read(a => a.LeftHand.IsPressed, () => Input.GetMouseButton(0));
        public static bool RightHand => Read(a => a.RightHand.IsPressed, () => Input.GetMouseButton(1));

        private static bool Read(Func<PlayerActions, bool> fromBindings, Func<bool> fallback)
        {
            try
            {
                if (Options.keyboardBindings != null)
                    return fromBindings(Options.keyboardBindings);
            }
            catch
            {
                // Fall through to the fixed defaults.
            }
            return fallback();
        }
    }
}
