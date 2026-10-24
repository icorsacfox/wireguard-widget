using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace WireGuardWidget.UI
{
    /// <summary>
    /// Приём файлов, перетащенных из Проводника. Обычный AllowDrop (OLE) не работает в окнах с правами
    /// администратора — Windows блокирует перетаскивание из процессов с меньшими правами. Поэтому используется
    /// старый механизм WM_DROPFILES, которому явно разрешается проходить через фильтр сообщений (UIPI).
    /// </summary>
    internal sealed class FileDropHook : NativeWindow
    {
        const int WM_DROPFILES = 0x0233, WM_COPYDATA = 0x004A, WM_COPYGLOBALDATA = 0x0049;
        const uint MSGFLT_ALLOW = 1;

        readonly Action<string[]> onDrop;

        public FileDropHook(Control control, Action<string[]> onDrop)
        {
            this.onDrop = onDrop;
            if (control.IsHandleCreated)
                Attach(control);
            control.HandleCreated += (s, e) => Attach(control);
            control.HandleDestroyed += (s, e) => ReleaseHandle();
        }

        void Attach(Control control)
        {
            AssignHandle(control.Handle);
            foreach (int message in new[] { WM_DROPFILES, WM_COPYDATA, WM_COPYGLOBALDATA })
                ChangeWindowMessageFilterEx(control.Handle, message, MSGFLT_ALLOW, IntPtr.Zero);
            DragAcceptFiles(control.Handle, true);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg != WM_DROPFILES)
            {
                base.WndProc(ref m);
                return;
            }
            IntPtr drop = m.WParam;
            try
            {
                uint count = DragQueryFile(drop, uint.MaxValue, null, 0);
                var files = new string[count];
                for (uint i = 0; i < count; i++)
                {
                    var buffer = new StringBuilder((int)DragQueryFile(drop, i, null, 0) + 1);
                    DragQueryFile(drop, i, buffer, (uint)buffer.Capacity);
                    files[i] = buffer.ToString();
                }
                onDrop(files);
            }
            finally
            {
                DragFinish(drop);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, int message, uint action, IntPtr changeFilterStruct);

        [DllImport("shell32.dll")]
        static extern void DragAcceptFiles(IntPtr hwnd, bool accept);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder file, uint size);

        [DllImport("shell32.dll")]
        static extern void DragFinish(IntPtr drop);
    }
}
