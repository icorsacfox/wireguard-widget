using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WireGuardWidget
{
    /// <summary>Глобальные хоткеи «модификатор + цифра» (верхний ряд и цифровой блок) через RegisterHotKey.</summary>
    internal sealed class HotkeyManager : NativeWindow, IDisposable
    {
        const int WM_HOTKEY = 0x0312;
        const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
        const int TopRowBase = 100, NumPadBase = 200;

        readonly List<int> registered = new List<int>();

        public HotkeyManager()
        {
            // окно только для сообщений (HWND_MESSAGE)
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
        }

        /// <summary>Нажата цифра 0…9.</summary>
        public event Action<int> DigitPressed;

        public static uint ParseModifiers(string text)
        {
            uint result = 0;
            if (string.IsNullOrWhiteSpace(text))
                return result;
            foreach (string part in text.Split('+'))
            {
                switch (part.Trim().ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        result |= MOD_CONTROL;
                        break;
                    case "alt":
                        result |= MOD_ALT;
                        break;
                    case "shift":
                        result |= MOD_SHIFT;
                        break;
                    case "win":
                        result |= MOD_WIN;
                        break;
                }
            }
            return result;
        }

        /// <summary>Перерегистрирует хоткеи. Возвращает цифры, которые занял кто-то другой.</summary>
        public List<int> Register(uint modifiers)
        {
            UnregisterAll();
            var failed = new List<int>();
            if (modifiers == 0)
                return failed;
            for (int digit = 0; digit <= 9; digit++)
            {
                if (RegisterHotKey(Handle, TopRowBase + digit, modifiers | MOD_NOREPEAT, (uint)(Keys.D0 + digit)))
                    registered.Add(TopRowBase + digit);
                else
                    failed.Add(digit);
                if (RegisterHotKey(Handle, NumPadBase + digit, modifiers | MOD_NOREPEAT, (uint)(Keys.NumPad0 + digit)))
                    registered.Add(NumPadBase + digit);
            }
            return failed;
        }

        void UnregisterAll()
        {
            foreach (int id in registered)
                UnregisterHotKey(Handle, id);
            registered.Clear();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                var handler = DigitPressed;
                if (handler != null)
                    handler(m.WParam.ToInt32() % 100);
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle == IntPtr.Zero)
                return;
            UnregisterAll();
            DestroyHandle();
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
