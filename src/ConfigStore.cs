using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marco;

public enum ModoTamano
{
    Monitor,        // estirar al monitor (comportamiento clásico)
    SoloBordes,     // quitar bordes sin mover ni estirar
    Personalizado,  // tamaño/posición propios
}

/// <summary>Entrada de la biblioteca con sus opciones de aplicación.</summary>
[JsonConverter(typeof(FavoritoConverter))]
public sealed class Favorito
{
    public string Proceso { get; set; } = "";
    public ModoTamano Modo { get; set; } = ModoTamano.Monitor;
    public int Ancho { get; set; }      // 0 = el ancho del monitor objetivo
    public int Alto { get; set; }
    public int? PosX { get; set; }      // null = centrado en el monitor objetivo
    public int? PosY { get; set; }
    // DeviceName de Screen (\\.\DISPLAY1): estable entre reinicios, los índices no
    public string MonitorDispositivo { get; set; } = "";
    public bool SiempreEncima { get; set; }
    public int RetardoSegundos { get; set; }
    public bool SilenciarFondo { get; set; }
}

/// <summary>
/// Acepta el formato v1.0 ("notepad") además del objeto v1.1: nadie pierde su biblioteca
/// al actualizar. Lectura/escritura a mano para no recursar en el propio converter.
/// </summary>
public sealed class FavoritoConverter : JsonConverter<Favorito>
{
    public override Favorito Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var favorito = new Favorito();
        if (reader.TokenType == JsonTokenType.String)
        {
            favorito.Proceso = reader.GetString() ?? "";
            return favorito;
        }
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return favorito;
        }
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            string propiedad = reader.GetString() ?? "";
            reader.Read();
            switch (propiedad)
            {
                case nameof(Favorito.Proceso): favorito.Proceso = reader.GetString() ?? ""; break;
                case nameof(Favorito.Modo):
                    if (Enum.TryParse(reader.GetString(), out ModoTamano modo)) favorito.Modo = modo;
                    break;
                case nameof(Favorito.Ancho): favorito.Ancho = reader.GetInt32(); break;
                case nameof(Favorito.Alto): favorito.Alto = reader.GetInt32(); break;
                case nameof(Favorito.PosX):
                    favorito.PosX = reader.TokenType == JsonTokenType.Null ? null : reader.GetInt32();
                    break;
                case nameof(Favorito.PosY):
                    favorito.PosY = reader.TokenType == JsonTokenType.Null ? null : reader.GetInt32();
                    break;
                case nameof(Favorito.MonitorDispositivo): favorito.MonitorDispositivo = reader.GetString() ?? ""; break;
                case nameof(Favorito.SiempreEncima): favorito.SiempreEncima = reader.GetBoolean(); break;
                case nameof(Favorito.RetardoSegundos): favorito.RetardoSegundos = reader.GetInt32(); break;
                case nameof(Favorito.SilenciarFondo): favorito.SilenciarFondo = reader.GetBoolean(); break;
                default: reader.Skip(); break;
            }
        }
        return favorito;
    }

    // Solo se escriben las opciones que se apartan del defecto: config.json legible
    public override void Write(Utf8JsonWriter writer, Favorito favorito, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(nameof(Favorito.Proceso), favorito.Proceso);
        if (favorito.Modo != ModoTamano.Monitor) writer.WriteString(nameof(Favorito.Modo), favorito.Modo.ToString());
        if (favorito.Ancho > 0) writer.WriteNumber(nameof(Favorito.Ancho), favorito.Ancho);
        if (favorito.Alto > 0) writer.WriteNumber(nameof(Favorito.Alto), favorito.Alto);
        if (favorito.PosX is { } x) writer.WriteNumber(nameof(Favorito.PosX), x);
        if (favorito.PosY is { } y) writer.WriteNumber(nameof(Favorito.PosY), y);
        if (favorito.MonitorDispositivo.Length > 0)
            writer.WriteString(nameof(Favorito.MonitorDispositivo), favorito.MonitorDispositivo);
        if (favorito.SiempreEncima) writer.WriteBoolean(nameof(Favorito.SiempreEncima), true);
        if (favorito.RetardoSegundos > 0) writer.WriteNumber(nameof(Favorito.RetardoSegundos), favorito.RetardoSegundos);
        if (favorito.SilenciarFondo) writer.WriteBoolean(nameof(Favorito.SilenciarFondo), true);
        writer.WriteEndObject();
    }
}

public sealed class Config
{
    public string ColorFondo { get; set; } = "#1B1B1B";
    public string ColorAcento { get; set; } = "#E91E63";
    public string ColorPanel { get; set; } = "";   // vacío = derivar automáticamente del fondo
    public string ColorTexto { get; set; } = "#FFFFFF";
    public bool BordesRedondeados { get; set; } = true;
    public int VentanaAncho { get; set; }   // 0 = tamaño por defecto
    public int VentanaAlto { get; set; }
    public List<Favorito> Favoritos { get; set; } = new();
    public List<string> Ocultas { get; set; } = new();   // procesos escondidos de la lista
    // Hotkeys globales como texto legible ("Ctrl+Alt+B"); si el parse o el registro
    // fallan se cae al valor por defecto sin romper nada
    public string Hotkey { get; set; } = "Ctrl+Alt+B";
    public string HotkeyRaton { get; set; } = "Ctrl+Alt+L";
    public bool ArrancarMinimizado { get; set; }
    public string Idioma { get; set; } = "en";
    public bool IniciarConWindows { get; set; }
    public bool CerrarMinimiza { get; set; }   // ✕ esconde a la bandeja en vez de salir
}

public static class ConfigStore
{
    /// <summary>Carpeta de datos; migra la del nombre antiguo (SinBordes) si existe.</summary>
    internal static readonly string Carpeta = PrepararCarpeta();

    private static readonly string Ruta = Path.Combine(Carpeta, "config.json");

    private static string PrepararCarpeta()
    {
        string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string nueva = Path.Combine(appdata, "Marco");
        try
        {
            string vieja = Path.Combine(appdata, "SinBordes");
            if (!Directory.Exists(nueva) && Directory.Exists(vieja))
                Directory.Move(vieja, nueva);
        }
        catch
        {
            // Si la migración falla, se arranca con carpeta nueva vacía
        }
        return nueva;
    }

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
