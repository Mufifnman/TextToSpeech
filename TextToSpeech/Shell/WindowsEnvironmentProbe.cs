using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.CoreAudioApi;
using TextToSpeech.Api.Arbitration;

namespace TextToSpeech.Shell
{
    /// <summary>
    /// Windows answers for the reading rules: is the default output headphones, which window is the user in, which
    /// window hosts an agent's process. Read only, except <see cref="BringToFront"/> which only runs when the
    /// "bring the reading window to the front" setting is on. Every call is defensive: a failure means "unknown".
    /// </summary>
    internal sealed class WindowsEnvironmentProbe : IEnvironmentProbe
    {
        // PKEY_AudioEndpoint_FormFactor: 1 speakers, 3 headphones, 5 headset, 9 HDMI, ...
        private static readonly Guid FormFactorFmtId = new("1da5d803-d492-4edd-8c23-e0c0ffee7f0e");

        private Dictionary<int, int>? parents;
        private DateTime parentsAt;

        public DateTimeOffset Now => DateTimeOffset.UtcNow;

        public bool? HeadphonesConnected()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var key = new PropertyKey(FormFactorFmtId, 0);
                if (!device.Properties.Contains(key))
                {
                    return null;
                }
                uint form = Convert.ToUInt32(device.Properties[key].Value);
                if (form == 3 || form == 5)
                {
                    return true;
                }
                return form == 10 ? null : false; // 10 = unknown form factor
            }
            catch (Exception)
            {
                return null;
            }
        }

        public (int Pid, string Title)? ForegroundWindow()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero || GetWindowThreadProcessId(hwnd, out uint pid) == 0)
                {
                    return null;
                }
                var title = new StringBuilder(512);
                GetWindowText(hwnd, title, title.Capacity);
                return ((int)pid, title.ToString());
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool IsAncestor(int ancestorPid, int pid)
        {
            var map = ParentMap();
            for (int hops = 0; hops < 32; hops++)
            {
                if (pid == ancestorPid) return true;
                if (!map.TryGetValue(pid, out int parent) || parent == 0 || parent == pid) return false;
                pid = parent;
            }
            return false;
        }

        public bool BringToFront(int pid)
        {
            try
            {
                var map = ParentMap();
                for (int hops = 0; hops < 32 && pid > 0; hops++)
                {
                    IntPtr hwnd = IntPtr.Zero;
                    try { using var p = Process.GetProcessById(pid); hwnd = p.MainWindowHandle; } catch (Exception) { /* gone */ }
                    if (hwnd != IntPtr.Zero)
                    {
                        if (IsIconic(hwnd)) ShowWindow(hwnd, 9); // SW_RESTORE
                        return SetForegroundWindow(hwnd);
                    }
                    if (!map.TryGetValue(pid, out int parent) || parent == pid) break;
                    pid = parent;
                }
            }
            catch (Exception) { /* nothing to raise */ }
            return false;
        }

        /// <summary>Process id to parent id, from a snapshot that is reused for a second (the poll asks once per agent).</summary>
        private Dictionary<int, int> ParentMap()
        {
            if (parents != null && DateTime.UtcNow - parentsAt < TimeSpan.FromSeconds(1))
            {
                return parents;
            }
            var map = new Dictionary<int, int>();
            IntPtr snap = CreateToolhelp32Snapshot(0x2, 0); // TH32CS_SNAPPROCESS
            if (snap != IntPtr.Zero && snap != new IntPtr(-1))
            {
                try
                {
                    var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
                    for (bool ok = Process32First(snap, ref entry); ok; ok = Process32Next(snap, ref entry))
                    {
                        map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
                    }
                }
                finally
                {
                    CloseHandle(snap);
                }
            }
            parents = map;
            parentsAt = DateTime.UtcNow;
            return map;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
