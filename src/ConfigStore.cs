using System.Text.Json;

namespace SinBordes;

public sealed class Config
{
    public string ColorFondo { get; set; } = "#1B1B1B";
    public string ColorAcento { get; set; } = "#E91E63";
    public string ColorPanel { get; set; } = "";   // vacío = derivar automáticamente del fondo
    public string ColorTexto { get; set; } = "#FFFFFF";
    public bool BordesRedondeados { get; set; } = true;
    public int VentanaAncho { get; set; }   // 0 = tamaño por defecto
    public int VentanaAlto { get; set; }
    public List<string> Favoritos { get; set; } = new();
    public bool ArrancarMinimizado { get; set; }
    public bool IniciarConWindows { get; set; }
    public bool CerrarMinimiza { get; set; }   // ✕ esconde a la bandeja en vez de salir
}

public static class ConfigStore
{
    private static readonly string Ruta = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SinBordes", "config.json");

    public static Config Cargar()
    {
        try
        {
            if (File.Exists(Ruta))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(Ruta)) ?? new();
        }
        catch
        {
            // config corrupta: valores por defecto antes que impedir arrancar
        }
        return new();
    }

    public static void Guardar(Config config)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
            File.WriteAllText(Ruta, JsonSerializer.Serialize(config,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // sin disco los colores duran solo esta sesión
        }
    }
}
