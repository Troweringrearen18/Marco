using System.Runtime.InteropServices;
using System.Text.Json;

namespace Marco;

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

    // Persistido en disco para poder restaurar aunque Marco se haya cerrado entre medias.
    // Vía ConfigStore.Carpeta para que la migración de carpeta corra antes que este path
    private static readonly string RutaEstado = Path.Combine(ConfigStore.Carpeta, "estado.json");

    private static readonly Dictionary<long, EstadoVentana> Guardadas;

    static BorderlessService()
    {
        Guardadas = Cargar();
        // Los hwnd se reciclan: el estado de una ventana ya muerta no se puede restaurar,
        // y si otro proceso hereda ese hwnd se le aplicarían estilos ajenos
        var muertas = Guardadas.Keys.Where(k => !NativeMethods.IsWindow(new IntPtr(k))).ToList();
        if (muertas.Count > 0)
        {
            foreach (var k in muertas) Guardadas.Remove(k);
            Persistir();
        }
    }

    public static bool TieneEstadoGuardado(IntPtr hwnd) => Guardadas.ContainsKey(hwnd.ToInt64());

    public static bool Aplicar(IntPtr hwnd, out string error) => Aplicar(hwnd, null, out error);

    public static bool Aplicar(IntPtr hwnd, Favorito? opciones, out string error)
    {
        error = "";
        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

        long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);
        long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        if (style == 0 || !NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            error = Textos.T("err.leer");
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
            error = Textos.T("err.uipi");
            return false;
        }

        const uint flags = NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW |
                           NativeMethods.SWP_NOOWNERZORDER;
        IntPtr encima = opciones?.SiempreEncima == true ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_TOP;

        // Solo quitar bordes: ni mover ni estirar, pero SWP_FRAMECHANGED siempre
        // (sin él, el borde se queda pintado aunque el estilo ya no esté)
        if (opciones?.Modo == ModoTamano.SoloBordes)
        {
            if (!NativeMethods.SetWindowPos(hwnd, encima, 0, 0, 0, 0,
                    flags | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE))
            {
                error = Textos.T("err.colocar");
                return false;
            }
            return true;
        }

        // Monitor objetivo: el elegido por DeviceName (estable entre reinicios) o el más cercano.
        // Si el monitor elegido ya no existe (desenchufado), caer al más cercano sin fallar.
        NativeMethods.RECT r;
        var elegido = string.IsNullOrEmpty(opciones?.MonitorDispositivo)
            ? null
            : Screen.AllScreens.FirstOrDefault(s => s.DeviceName == opciones.MonitorDispositivo);
        if (elegido is not null)
        {
            // Screen.Bounds equivale a rcMonitor: tapa también la barra de tareas
            r = new NativeMethods.RECT
            {
                Left = elegido.Bounds.Left,
                Top = elegido.Bounds.Top,
                Right = elegido.Bounds.Right,
                Bottom = elegido.Bounds.Bottom,
            };
        }
        else
        {
            var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (!NativeMethods.GetMonitorInfo(monitor, ref mi))
            {
                error = Textos.T("err.monitor");
                return false;
            }
            // rcMonitor y no rcWork: queremos tapar también la barra de tareas
            r = mi.rcMonitor;
        }

        int ancho = r.Right - r.Left, alto = r.Bottom - r.Top;
        int x = r.Left, y = r.Top;
        if (opciones?.Modo == ModoTamano.Personalizado)
        {
            if (opciones.Ancho > 0) ancho = opciones.Ancho;
            if (opciones.Alto > 0) alto = opciones.Alto;
            x = opciones.PosX ?? r.Left + (r.Right - r.Left - ancho) / 2;
            y = opciones.PosY ?? r.Top + (r.Bottom - r.Top - alto) / 2;
        }

        if (!NativeMethods.SetWindowPos(hwnd, encima, x, y, ancho, alto, flags))
        {
            error = Textos.T("err.colocar");
            return false;
        }

        return true;
    }

    public static bool Restaurar(IntPtr hwnd, out string error)
    {
        error = "";
        if (!Guardadas.TryGetValue(hwnd.ToInt64(), out var estado))
        {
            error = Textos.T("err.sinEstado");
            return false;
        }

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, estado.Style);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, estado.ExStyle);

        // Reponer el exstyle no basta para quitar el "siempre encima": el estado topmost
        // real solo cambia vía SetWindowPos con HWND_(NO)TOPMOST según el original
        IntPtr encima = (estado.ExStyle & NativeMethods.WS_EX_TOPMOST) != 0
            ? NativeMethods.HWND_TOPMOST
            : NativeMethods.HWND_NOTOPMOST;
        if (!NativeMethods.SetWindowPos(hwnd, encima,
                estado.Left, estado.Top, estado.Width, estado.Height,
                NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOOWNERZORDER))
        {
            error = Textos.T("err.restaurar");
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
