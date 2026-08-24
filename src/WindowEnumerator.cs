using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Marco;

public sealed record WindowInfo(IntPtr Hwnd, string Title, string ProcessName, int Pid)
{
    /// <summary>El proceso corre con más privilegios que Marco: UIPI bloqueará los cambios.</summary>
    public bool Elevada { get; init; }
}

public static class WindowEnumerator
{
    private static readonly HashSet<string> ClasesExcluidas = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",                     // escritorio
        "Shell_TrayWnd",               // barra de tareas
        "WorkerW",
        "Windows.UI.Core.CoreWindow",  // UWP: no se les puede cambiar el estilo
        "ApplicationFrameWindow",
    };

    private static readonly long IlPropio = NivelIntegridad(Environment.ProcessId);

    // Nivel de integridad por token (0x2000 Medium, 0x3000 High/admin). Process.Modules
    // como heurística da falsos negativos: esto es lo fiable (visto con Space Marine)
    private static long NivelIntegridad(int pid)
    {
        IntPtr proceso = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (proceso == IntPtr.Zero) return -1;
        long nivel = -1;
        if (NativeMethods.OpenProcessToken(proceso, NativeMethods.TOKEN_QUERY, out IntPtr token))
        {
            NativeMethods.GetTokenInformation(token, NativeMethods.TokenIntegrityLevel, IntPtr.Zero, 0, out int tam);
            IntPtr buffer = Marshal.AllocHGlobal(tam);
            if (NativeMethods.GetTokenInformation(token, NativeMethods.TokenIntegrityLevel, buffer, tam, out _))
            {
                IntPtr sid = Marshal.ReadIntPtr(buffer);
                int cuenta = Marshal.ReadByte(NativeMethods.GetSidSubAuthorityCount(sid));
                nivel = Marshal.ReadInt32(NativeMethods.GetSidSubAuthority(sid, cuenta - 1));
            }
            Marshal.FreeHGlobal(buffer);
            NativeMethods.CloseHandle(token);
        }
        NativeMethods.CloseHandle(proceso);
        return nivel;
    }

    public static List<WindowInfo> Listar()
    {
        var resultado = new List<WindowInfo>();
        var elevadas = new Dictionary<int, bool>();
        int pidPropio = Environment.ProcessId;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;

            var sb = new StringBuilder(512);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            string titulo = sb.ToString();
            if (string.IsNullOrWhiteSpace(titulo)) return true;

            var clase = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, clase, clase.Capacity);
            if (ClasesExcluidas.Contains(clase.ToString())) return true;

            // Ventanas UWP "cloaked": visibles para EnumWindows pero no en pantalla
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED,
                    out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == pidPropio) return true;

            string proceso;
            try
            {
                proceso = Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
                return true; // el proceso murió entre la enumeración y aquí
            }

            if (!elevadas.TryGetValue((int)pid, out bool elevada))
            {
                elevada = NivelIntegridad((int)pid) > IlPropio;
                elevadas[(int)pid] = elevada;
            }

            resultado.Add(new WindowInfo(hwnd, titulo, proceso, (int)pid) { Elevada = elevada });
            return true;
        }, IntPtr.Zero);

        return resultado.OrderBy(v => v.ProcessName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(v => v.Title, StringComparer.OrdinalIgnoreCase)
                        .ToList();
    }
}
