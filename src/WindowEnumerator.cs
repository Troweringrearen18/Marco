using System.Diagnostics;
using System.Text;

namespace SinBordes;

public sealed record WindowInfo(IntPtr Hwnd, string Title, string ProcessName, int Pid);

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

    public static List<WindowInfo> Listar()
    {
        var resultado = new List<WindowInfo>();
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

            resultado.Add(new WindowInfo(hwnd, titulo, proceso, (int)pid));
            return true;
        }, IntPtr.Zero);

        return resultado.OrderBy(v => v.ProcessName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(v => v.Title, StringComparer.OrdinalIgnoreCase)
                        .ToList();
    }
}
