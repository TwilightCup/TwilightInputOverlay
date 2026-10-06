using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// Reads the held state of one HUD key's bound <see cref="KeyCode"/>. The
    /// customizable layout binds each key unit to an explicit key (or mouse
    /// button), so the HUD reads the raw input directly instead of the game's
    /// action bindings. Mouse buttons arrive as the Mouse0–Mouse6 KeyCode range;
    /// route those through <see cref="Input.GetMouseButton"/> because
    /// <see cref="Input.GetKey"/> is not documented to cover them.
    /// </summary>
    internal static class InputState
    {
        /// <summary>Whether the given key or mouse button is currently held.</summary>
        public static bool IsHeld(KeyCode key)
        {
            if (key == KeyCode.None) return false;
            int v = (int)key;
            if (v >= (int)KeyCode.Mouse0 && v <= (int)KeyCode.Mouse6)
                return Input.GetMouseButton(v - (int)KeyCode.Mouse0);
            return Input.GetKey(key);
        }
    }
}
