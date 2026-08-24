using System.Runtime.InteropServices;
using System.Text.Json;

namespace SinBordes;

public sealed class EstadoVentana
{
    public long Style { get; set; }
    public long ExStyle { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public static class BorderlessService
{
    private const long EstilosBorde =
        NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME | NativeMethods.WS_MINIMIZEBOX |
        NativeMethods.WS_MAXIMIZEBOX | NativeMethods.WS_SYSMENU;

    private const long EstilosBordeEx =
        NativeMethods.WS_EX_DLGMODALFRAME | NativeMethods.WS_EX_CLIENTEDGE | NativeMethods.WS_EX_STATICEDGE;

    // Persistido en disco para poder restaurar aunque SinBordes se haya cerrado entre medias
    private static readonly string RutaEstado = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SinBordes", "estado.json");

    private static readonly Dictionary<long, EstadoVentana> Guardadas = Cargar();

    public static bool TieneEstadoGuardado(IntPtr hwnd) => Guardadas.ContainsKey(hwnd.ToInt64());

    public static bool Aplicar(IntPtr hwnd, out string error)
    {
        error = "";
        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

        long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);
        long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        if (style == 0 || !NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            error = "No se pudo leer la ventana (¿el proceso corre como administrador?).";
            return false;
        }

        // Si se re-aplica, conservar el primer estado guardado: es el original de verdad
        if (!Guardadas.ContainsKey(hwnd.ToInt64()))
        {
            Guardadas[hwnd.ToInt64()] = new EstadoVentana
            {
                Style = style,
                ExStyle = exStyle,
                Left = rect.Left,
                Top = rect.Top,
                Width = rect.Right - rect.Left,
                Height = rect.Bottom - rect.Top,
            };
            Persistir();
        }

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, style & ~EstilosBorde);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, exStyle & ~EstilosBordeEx);

        // UIPI falla en silencio: la única comprobación fiable es releer el estilo
        if ((NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE) & NativeMethods.WS_CAPTION) != 0)
        {
            error = "Windows bloqueó el cambio (el proceso tiene más privilegios). Ejecuta SinBordes como administrador.";
            return false;
        }

        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (!NativeMethods.GetMonitorInfo(monitor, ref mi))
        {
            error = "No se pudo obtener el monitor de la ventana.";
            return false;
        }

        // rcMonitor y no rcWork: queremos tapar también la barra de tareas
        var r = mi.rcMonitor;
        if (!NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, r.Left, r.Top,
                r.Right - r.Left, r.Bottom - r.Top,
                NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOOWNERZORDER))
        {
            error = "SetWindowPos falló al colocar la ventana.";
            return false;
        }

        return true;
    }

    public static bool Restaurar(IntPtr hwnd, out string error)
    {
        error = "";
        if (!Guardadas.TryGetValue(hwnd.ToInt64(), out var estado))
        {
            error = "No hay estado guardado de esa ventana.";
            return false;
        }

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, estado.Style);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, estado.ExStyle);

        if (!NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP,
                estado.Left, estado.Top, estado.Width, estado.Height,
                NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOOWNERZORDER))
        {
            error = "SetWindowPos falló al restaurar.";
            return false;
        }

        Guardadas.Remove(hwnd.ToInt64());
        Persistir();
        return true;
    }

    private static Dictionary<long, EstadoVentana> Cargar()
    {
        try
        {
            if (File.Exists(RutaEstado))
                return JsonSerializer.Deserialize<Dictionary<long, EstadoVentana>>(
                    File.ReadAllText(RutaEstado)) ?? new();
        }
        catch
        {
            // JSON corrupto o ilegible: empezar de cero antes que impedir arrancar
        }
        return new();
    }

    private static void Persistir()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RutaEstado)!);
            File.WriteAllText(RutaEstado, JsonSerializer.Serialize(Guardadas));
        }
        catch
        {
            // Sin disco no hay restauración tras reinicio, pero la sesión sigue funcionando
        }
    }
}
