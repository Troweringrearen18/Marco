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
        if (args.Length >= 2 && args[0] is "--apply" or "--restore")
            return Cli(args[0], args[1]);

        Application.Run(new MainForm());
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

        bool ok = true;
        foreach (var v in ventanas)
        {
            string error;
            bool exito = accion == "--apply"
                ? BorderlessService.Aplicar(v.Hwnd, out error)
                : BorderlessService.Restaurar(v.Hwnd, out error);
            Console.WriteLine($"{v.ProcessName} «{v.Title}»: {(exito ? "ok" : error)}");
            ok &= exito;
        }
        return ok ? 0 : 1;
    }
}
