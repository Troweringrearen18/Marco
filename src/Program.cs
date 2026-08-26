namespace Marco;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Antes de la rama CLI: aquí se activa PerMonitorV2, y sin él GetMonitorInfo
        // devuelve coordenadas virtualizadas con escalado ≠ 100%
        ApplicationConfiguration.Initialize();
        Textos.Idioma = ConfigStore.Cargar().Idioma;

        // Modo CLI para scripts y pruebas: Marco --apply <proceso> | --restore <proceso>
        // (el CLI queda fuera del candado de instancia única a propósito)
        if (args.Length >= 2 && args[0] is "--apply" or "--restore")
            return Cli(args[0], args[1]);

        // Una sola instancia de la GUI: la segunda despierta a la primera y se retira
        using var unica = new Mutex(initiallyOwned: true, @"Local\Marco.InstanciaUnica", out bool primera);
        if (!primera)
        {
            IntPtr existente = NativeMethods.FindWindow(null, "Marco");
            if (existente != IntPtr.Zero)
            {
                NativeMethods.ShowWindow(existente, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(existente);
            }
            else
            {
                MessageBox.Show(Textos.T("app.yaAbierto"), "Marco",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 0;
        }

        // --bandeja lo pasa solo la entrada de autoarranque: un doble clic manual
        // siempre abre la ventana aunque "arrancar minimizado" esté activo
        Application.Run(new MainForm(args.Contains("--bandeja")));
        return 0;
    }

    /// <returns>0 ok, 1 falló la operación, 2 no hay ventana de ese proceso</returns>
    private static int Cli(string accion, string proceso)
    {
        var ventanas = WindowEnumerator.Listar()
            .Where(v => v.ProcessName.Equals(proceso, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (ventanas.Count == 0)
        {
            Console.WriteLine(Textos.F("cli.sinVentanas", proceso));
            return 2;
        }

        // Si el proceso está en la biblioteca, el CLI aplica sus mismas opciones
        var favorito = ConfigStore.Cargar().Favoritos
            .FirstOrDefault(f => f.Proceso.Equals(proceso, StringComparison.OrdinalIgnoreCase));

        bool ok = true;
        foreach (var v in ventanas)
        {
            string error;
            bool exito = accion == "--apply"
                ? BorderlessService.Aplicar(v.Hwnd, favorito, out error)
                : BorderlessService.Restaurar(v.Hwnd, out error);
            Console.WriteLine($"{v.ProcessName} «{v.Title}»: {(exito ? "ok" : error)}");
            ok &= exito;
        }
        return ok ? 0 : 1;
    }
}
