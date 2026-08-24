namespace SinBordes;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Modo CLI para scripts y pruebas: SinBordes --apply <proceso> | --restore <proceso>
        if (args.Length >= 2 && args[0] is "--apply" or "--restore")
            return Cli(args[0], args[1]);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    /// <returns>0 ok, 1 falló la operación, 2 no hay ventana de ese proceso</returns>
    private static int Cli(string accion, string proceso)
    {
        var ventanas = WindowEnumerator.Listar()
            .Where(v => v.ProcessName.Equals(proceso, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (ventanas.Count == 0) return 2;

        bool ok = true;
        foreach (var v in ventanas)
            ok &= accion == "--apply"
                ? BorderlessService.Aplicar(v.Hwnd, out _)
                : BorderlessService.Restaurar(v.Hwnd, out _);
        return ok ? 0 : 1;
    }
}
