using System;
using System.Windows.Forms;

namespace ClickForge
{
    // Registers system-wide hotkeys against a window handle. The owning form
    // forwards WM_HOTKEY messages from its WndProc into HandleMessage, which
    // raises Triggered with the logical hotkey id.
    internal class HotkeyManager
    {
        public const int ID_TOGGLE = 9001;
        public const int ID_STOP = 9002;

        private readonly IntPtr _handle;
        private int _toggleVk;
        private int _stopVk;

        // Fires with ID_TOGGLE or ID_STOP.
        public event Action<int> Triggered;

        public HotkeyManager(IntPtr handle)
        {
            _handle = handle;
        }

        // (Re)register both hotkeys. Silently tolerates keys already grabbed by
        // another app so the rest of the program keeps working.
        public void Register(int toggleVk, int stopVk)
        {
            Unregister();
            _toggleVk = toggleVk;
            _stopVk = stopVk;

            if (_toggleVk > 0)
                NativeMethods.RegisterHotKey(_handle, ID_TOGGLE,
                    NativeMethods.MOD_NOREPEAT, (uint)_toggleVk);

            if (_stopVk > 0 && _stopVk != _toggleVk)
                NativeMethods.RegisterHotKey(_handle, ID_STOP,
                    NativeMethods.MOD_NOREPEAT, (uint)_stopVk);
        }

        public void Unregister()
        {
            NativeMethods.UnregisterHotKey(_handle, ID_TOGGLE);
            NativeMethods.UnregisterHotKey(_handle, ID_STOP);
        }

        // Returns true if the message was a hotkey we handled.
        public bool HandleMessage(ref Message m)
        {
            if (m.Msg != NativeMethods.WM_HOTKEY)
                return false;

            int id = m.WParam.ToInt32();
            if (id == ID_TOGGLE || id == ID_STOP)
            {
                if (Triggered != null)
                    Triggered(id);
                return true;
            }
            return false;
        }

        // Friendly name for a virtual-key code, for the UI.
        public static string KeyName(int vk)
        {
            try
            {
                Keys k = (Keys)vk;
                return k.ToString();
            }
            catch
            {
                return "0x" + vk.ToString("X2");
            }
        }
    }
}
