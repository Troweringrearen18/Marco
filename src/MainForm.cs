using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Marco;

public sealed class MainForm : Form
{
    private readonly ListaSuave _lista;
    private readonly Label _estado;
    private readonly DesplegableIdiomas _desplegableIdiomas;
    private readonly FlowLayoutPanel _botones;
    private readonly Panel _barraTitulo;
    private readonly Label _titulo;
    private readonly Button _maximizar;
    private readonly BotonRedondeado _botonAccion;
    private readonly BotonRedondeado _botonFavorito;
    private readonly BotonRedondeado _botonOpciones;
    private readonly BotonRedondeado _botonActualizar;
    private readonly BotonRedondeado _botonAjustes;
    private readonly TableLayoutPanel _panelAjustes;
    private readonly Panel _marcoLista;
    private readonly NotifyIcon _bandeja;
    private readonly System.Windows.Forms.Timer _watcher;
    private readonly HashSet<long> _watcherFallidas = new();
    // Deshechas a mano por el usuario: el watcher las respeta mientras viva la ventana;
    // al relanzar el juego (hwnd nuevo) vuelve el borderless automático
    private readonly HashSet<long> _deshechasManualmente = new();
    // Primer avistamiento de cada hwnd favorito, para el retardo configurable
    private readonly Dictionary<long, DateTime> _vistasWatcher = new();
    // Pids que Marco silenció: solo se desmutea lo que se muteó aquí
    private readonly HashSet<int> _pidsSilenciados = new();
    private IntPtr _ratonBloqueado;
    private bool _watcherPausado;
    private readonly Config _config;
    private long _huellaVentanas;
    private bool _iniciarOculto;
    private bool _globoBandeja;
    private bool _salir;

    private readonly Label _version;
    private Color _fondo, _texto, _textoSuave, _panel, _textoPanel, _seleccion, _acento;
    private Font _fuenteNegrita = null!, _fuentePequena = null!;

    public MainForm(bool arrancoBandeja = false)
    {
        _config = ConfigStore.Cargar();
        Textos.Idioma = string.IsNullOrEmpty(_config.Idioma) ? "en" : _config.Idioma;
        // Minimizado solo cuando lo lanza el autoarranque de Windows (--bandeja)
        _iniciarOculto = arrancoBandeja && _config.ArrancarMinimizado;

        Text = "Marco";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        // Apaisada como LS; si el usuario la redimensionó, se respeta su último tamaño
        MinimumSize = new Size(520, 380);
        ClientSize = _config.VentanaAncho >= 520 && _config.VentanaAlto >= 380
            ? new Size(_config.VentanaAncho, _config.VentanaAlto)
            : new Size(680, 460);
        FormClosing += (_, _) =>
        {
            var tam = WindowState == FormWindowState.Normal ? ClientSize : RestoreBounds.Size;
            // No memorizar el alto extra del panel de ajustes desplegado
            if (_panelAjustes?.Visible == true) tam = new Size(tam.Width, tam.Height - _panelAjustes.Height);
            _config.VentanaAncho = tam.Width;
            _config.VentanaAlto = tam.Height;
            ConfigStore.Guardar(_config);
        };
        // Anillo de 6px del propio Form alrededor de los hijos: zona de agarre del redimensionado
        Padding = new Padding(6);
        Font = new Font("Segoe UI", 9.75f);

        // Barra de título propia, al estilo Lossless Scaling
        _barraTitulo = new Panel { Dock = DockStyle.Top, Height = 42 };
        // Icono junto al nombre, como LS
        var iconoTitulo = new PictureBox
        {
            Size = new Size(20, 20),
            Location = new Point(13, 11),
            BackColor = Color.Transparent,
            Image = DibujarPixeles(20),
        };
        iconoTitulo.MouseDown += ArrastrarVentana;
        _barraTitulo.Controls.Add(iconoTitulo);
        _titulo = new Label
        {
            Text = "Marco",
            AutoSize = true,
            Location = new Point(39, 11),
            Font = new Font("Segoe UI Semibold", 10.5f),
        };
        _maximizar = BotonTitulo("\uE922", AlternarMaximizado); // maximizar/restaurar (Segoe MDL2)
        var cerrar = BotonTitulo("\uE8BB", Close);
        var minimizar = BotonTitulo("\uE921", OcultarABandeja); // minimizar = irse a la bandeja
        var ayuda = BotonTitulo("\uE897", AbrirAyuda);          // ? \u2014 ayuda general
        _barraTitulo.Controls.Add(_titulo);
        _barraTitulo.Controls.Add(cerrar);
        _barraTitulo.Controls.Add(_maximizar);
        _barraTitulo.Controls.Add(minimizar);
        _barraTitulo.Controls.Add(ayuda);
        // Los avisos viven en la barra de título (sin barra inferior) y se borran solos
        _estado = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent,
        };
        var borradoEstado = new System.Windows.Forms.Timer { Interval = 6000 };
        borradoEstado.Tick += (_, _) => { borradoEstado.Stop(); _estado.Text = ""; };
        _estado.TextChanged += (_, _) =>
        {
            borradoEstado.Stop();
            if (_estado.Text.Length > 0) borradoEstado.Start();
        };
        _barraTitulo.Controls.Add(_estado);

        _barraTitulo.Resize += (_, _) =>
        {
            cerrar.Location = new Point(_barraTitulo.Width - cerrar.Width - 6, 5);
            _maximizar.Location = new Point(cerrar.Left - _maximizar.Width - 2, 5);
            minimizar.Location = new Point(_maximizar.Left - minimizar.Width - 2, 5);
            ayuda.Location = new Point(minimizar.Left - ayuda.Width - 2, 5);
            _estado.SetBounds(_titulo.Right + 12, 0,
                Math.Max(0, ayuda.Left - _titulo.Right - 24), _barraTitulo.Height);
        };
        _barraTitulo.MouseDown += ArrastrarVentana;
        _titulo.MouseDown += ArrastrarVentana;
        _estado.MouseDown += ArrastrarVentana;

        _lista = new ListaSuave(this) { Dock = DockStyle.Fill };
        _lista.DobleClic += AccionPrincipal;
        _lista.SeleccionCambiada += ActualizarBotonAccion;

        _botones = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(8),
        };
        _botonAccion = Boton(Textos.T("boton.aplicar"), AccionPrincipal);
        _botonFavorito = Boton(Textos.T("boton.guardar"), AlternarFavorito);
        _botonOpciones = Boton(Textos.T("boton.opciones"), AbrirOpciones);
        _botonOpciones.Visible = false; // solo con un favorito seleccionado
        _botonActualizar = Boton(Textos.T("boton.actualizar"), Refrescar);
        _botonAjustes = Boton(Textos.T("boton.ajustes"), AlternarAjustes);
        _botones.Controls.Add(_botonAccion);
        _botones.Controls.Add(_botonFavorito);
        _botones.Controls.Add(_botonOpciones);
        _botones.Controls.Add(_botonActualizar);
        _botones.Controls.Add(_botonAjustes);

        Icon = CrearIcono();
        _bandeja = new NotifyIcon
        {
            Icon = Icon,
            Text = "Marco",
            Visible = true,
        };
        _bandeja.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) MostrarDesdeBandeja();
        };
        RehacerMenuBandeja();

        // Watcher: aplica borderless solo a los juegos de la biblioteca según arrancan
        _watcher = new System.Windows.Forms.Timer { Interval = 2000 };
        _watcher.Tick += (_, _) => TickWatcher();
        _watcher.Start();

        FormClosing += (_, e) =>
        {
            // Con "cerrar minimiza": la ✕ esconde a la bandeja; salir de verdad es el menú de bandeja
            if (_config.CerrarMinimiza && !_salir && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                OcultarABandeja();
                return;
            }
            _bandeja.Visible = false;
            _bandeja.Dispose();
            NativeMethods.UnregisterHotKey(Handle, 1);
            NativeMethods.UnregisterHotKey(Handle, 2);
            // No dejar rastro al salir: ni cursor preso ni juegos mudos huérfanos
            NativeMethods.ClipCursor(IntPtr.Zero);
            foreach (int pid in _pidsSilenciados) AudioService.Silenciar(pid, false);
        };

        // Estilo LS: sin líneas separadoras — la tarjeta redondeada la pinta ListaSuave
        _marcoLista = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4) };
        _marcoLista.Controls.Add(_lista);

        // Panel de ajustes integrado: se despliega bajo los botones, con el tema de la app
        // (el ContextMenuStrip del sistema rompía la estética)
        _panelAjustes = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(10, 4, 10, 8),
            Visible = false,
        };
        _panelAjustes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Versión en la esquina inferior derecha, flotando a la altura de los botones
        string producto = Application.ProductVersion.Split('+', '-')[0];
        var trozos = producto.Split('.');
        _version = new Label
        {
            AutoSize = true,
            Text = "v" + (trozos.Length >= 2 ? $"{trozos[0]}.{trozos[1]}" : producto),
            Font = new Font("Segoe UI", 8.25f),
            BackColor = Color.Transparent,
        };
        void ColocarVersion()
        {
            _version.Location = new Point(
                ClientSize.Width - _version.Width - 14,
                _botones.Top + (_botones.Height - _version.Height) / 2);
            _version.BringToFront();
        }
        _botones.SizeChanged += (_, _) => ColocarVersion();
        _botones.LocationChanged += (_, _) => ColocarVersion();
        _version.SizeChanged += (_, _) => ColocarVersion();
        Resize += (_, _) => ColocarVersion();

        _desplegableIdiomas = new DesplegableIdiomas(this);
        _desplegableIdiomas.Elegido += CambiarIdioma;

        // Orden de docking: panel de ajustes al fondo, botones encima del panel
        Controls.Add(_marcoLista);
        Controls.Add(_botones);
        Controls.Add(_panelAjustes);
        Controls.Add(_barraTitulo);
        Controls.Add(_desplegableIdiomas);
        Controls.Add(_version);
        _marcoLista.BringToFront();

        // Autoarranque activo: refrescar la entrada del registro por si el exe cambió
        // de nombre o de ruta (p.ej. renombrado SinBordes → Marco, o Debug → Release)
        if (_config.IniciarConWindows) ConfigurarAutoarranque(true);

        AplicarTema();
        Load += (_, _) => Refrescar();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Sin FormBorderStyle, Windows 11 no redondea solo: se pide a DWM (en W10 se ignora)
        int redondeo = NativeMethods.DWMWCP_ROUND;
        _ = NativeMethods.DwmSetWindowAttribute(Handle,
            NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref redondeo, sizeof(int));

        // Hotkeys globales configurables: id 1 borderless, id 2 bloqueo de ratón
        RegistrarHotkeys();
    }

    private void RegistrarHotkeys()
    {
        NativeMethods.UnregisterHotKey(Handle, 1);
        NativeMethods.UnregisterHotKey(Handle, 2);
        RegistrarHotkey(1, _config.Hotkey, "Ctrl+Alt+B");
        RegistrarHotkey(2, _config.HotkeyRaton, "Ctrl+Alt+L");
    }

    // Si el combo del usuario no parsea o ya lo tiene otra app, se cae al de por defecto
    private void RegistrarHotkey(int id, string combo, string defecto)
    {
        if (ParseHotkey(combo, out uint mods, out uint vk) &&
            NativeMethods.RegisterHotKey(Handle, id, mods, vk))
            return;
        if (ParseHotkey(defecto, out mods, out vk))
            NativeMethods.RegisterHotKey(Handle, id, mods, vk);
    }

    // "Ctrl+Alt+B" → (MOD_CONTROL|MOD_ALT, 0x42). Los nombres de tecla son los del
    // enum Keys (más "0".."9" como alias de D0..D9); "Strg" convive por el alemán
    private static bool ParseHotkey(string combo, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        foreach (string trozo in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (trozo.ToLowerInvariant())
            {
                case "ctrl" or "control" or "strg": mods |= NativeMethods.MOD_CONTROL; continue;
                case "alt": mods |= NativeMethods.MOD_ALT; continue;
                case "shift": mods |= NativeMethods.MOD_SHIFT; continue;
            }
            string nombre = trozo.Length == 1 && char.IsAsciiDigit(trozo[0]) ? "D" + trozo : trozo;
            if (!Enum.TryParse(nombre, ignoreCase: true, out Keys tecla)) return false;
            vk = (uint)tecla;
        }
        return vk != 0;
    }

    private static string ComboATexto(Keys modificadores, Keys tecla)
    {
        string nombre = tecla is >= Keys.D0 and <= Keys.D9 ? ((char)('0' + tecla - Keys.D0)).ToString() : tecla.ToString();
        return (modificadores.HasFlag(Keys.Control) ? "Ctrl+" : "")
             + (modificadores.HasFlag(Keys.Alt) ? "Alt+" : "")
             + (modificadores.HasFlag(Keys.Shift) ? "Shift+" : "")
             + nombre;
    }

    private void ArrastrarVentana(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN,
            new IntPtr(NativeMethods.HTCAPTION), IntPtr.Zero);
    }

    private void AlternarMaximizado()
    {
        if (WindowState == FormWindowState.Normal)
        {
            // Respetar la barra de tareas: sin esto un form sin borde maximizado la tapa
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            WindowState = FormWindowState.Maximized;
            _maximizar.Text = "\uE923"; // restaurar (Segoe MDL2)
        }
        else
        {
            WindowState = FormWindowState.Normal;
            _maximizar.Text = "\uE922";
        }
    }

    // Arrancar minimizado: se veta la primera visualización en vez de un Hide posterior
    protected override void SetVisibleCore(bool value)
    {
        if (_iniciarOculto && value)
        {
            _iniciarOculto = false;
            value = false;
            if (!IsHandleCreated) CreateHandle(); // el hotkey y el watcher necesitan handle
        }
        base.SetVisibleCore(value);
    }

    private void RehacerMenuBandeja()
    {
        var menuBandeja = new ContextMenuStrip();
        menuBandeja.Items.Add(Textos.T("bandeja.mostrar"), null, (_, _) => MostrarDesdeBandeja());
        // Pausa del automático: solo esta sesión, como en el BG clásico
        var pausa = new ToolStripMenuItem(Textos.T("bandeja.pausa"))
        {
            CheckOnClick = true,
            Checked = _watcherPausado,
        };
        pausa.CheckedChanged += (_, _) => _watcherPausado = pausa.Checked;
        menuBandeja.Items.Add(pausa);
        menuBandeja.Items.Add(new ToolStripSeparator());
        menuBandeja.Items.Add(Textos.T("bandeja.salir"), null, (_, _) => { _salir = true; Close(); });
        _bandeja.ContextMenuStrip?.Dispose();
        _bandeja.ContextMenuStrip = menuBandeja;
    }

    private void CambiarIdioma(string codigo)
    {
        _config.Idioma = codigo;
        Textos.Idioma = codigo;
        ConfigStore.Guardar(_config);
        _botonActualizar.Text = Textos.T("boton.actualizar");
        _botonAjustes.Text = Textos.T("boton.ajustes");
        RehacerMenuBandeja();
        ReconstruirAjustes();
        Refrescar(); // botones contextuales, barra de estado y filas
    }

    private void OcultarABandeja()
    {
        Hide();
        if (_globoBandeja) return;
        _globoBandeja = true;
        _bandeja.BalloonTipTitle = "Marco";
        _bandeja.BalloonTipText = Textos.T("bandeja.globo");
        _bandeja.ShowBalloonTip(1500);
    }

    private void MostrarDesdeBandeja()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        Refrescar();
    }

    private void TickWatcher()
    {
        var ventanas = WindowEnumerator.Listar();

        bool cambio = false;
        if (!_watcherPausado && _config.Favoritos.Count > 0)
        {
            foreach (var v in ventanas)
            {
                if (v.Elevada || BuscarFavorito(v.ProcessName) is not { } favorito) continue;
                long clave = v.Hwnd.ToInt64();
                if (_watcherFallidas.Contains(clave) || _deshechasManualmente.Contains(clave)) continue;
                bool aplicada = BorderlessService.TieneEstadoGuardado(v.Hwnd);
                // Retardo configurado: dejar al juego terminar de arrancar antes del primer toque
                if (!aplicada && favorito.RetardoSegundos > 0)
                {
                    if (!_vistasWatcher.TryGetValue(clave, out var vista))
                    {
                        _vistasWatcher[clave] = DateTime.UtcNow;
                        continue;
                    }
                    if ((DateTime.UtcNow - vista).TotalSeconds < favorito.RetardoSegundos) continue;
                }
                bool conBorde = (NativeMethods.GetWindowLongPtr(v.Hwnd, NativeMethods.GWL_STYLE)
                                 & NativeMethods.WS_CAPTION) != 0;
                // Ventana favorita nueva, o el juego re-impuso su borde al cambiar de escena
                if (!aplicada || conBorde)
                {
                    if (BorderlessService.Aplicar(v.Hwnd, favorito, out _)) cambio = true;
                    else _watcherFallidas.Add(clave); // no insistir cada 2 s contra un fallo persistente
                }
            }
            _watcherFallidas.RemoveWhere(h => !NativeMethods.IsWindow(new IntPtr(h)));
            _deshechasManualmente.RemoveWhere(h => !NativeMethods.IsWindow(new IntPtr(h)));
            foreach (long clave in _vistasWatcher.Keys.Where(h => !NativeMethods.IsWindow(new IntPtr(h))).ToList())
                _vistasWatcher.Remove(clave);
        }

        MantenerBloqueoRaton();
        MantenerSilencios(ventanas);

        long huella = ventanas.Count;
        foreach (var v in ventanas) huella = unchecked(huella * 31 + v.Hwnd.ToInt64());
        if (cambio || huella != _huellaVentanas)
        {
            _huellaVentanas = huella;
            if (Visible) RefrescarCon(ventanas);
        }
    }

    private bool EsFavorito(string proceso) => BuscarFavorito(proceso) is not null;

    private Favorito? BuscarFavorito(string proceso) =>
        _config.Favoritos.FirstOrDefault(f => f.Proceso.Equals(proceso, StringComparison.OrdinalIgnoreCase));

    private void AlternarFavorito()
    {
        if (_lista.Seleccion is not { } v)
        {
            _estado.Text = Textos.T("estado.selecciona");
            return;
        }
        if (EsFavorito(v.ProcessName))
        {
            _config.Favoritos.RemoveAll(f => f.Proceso.Equals(v.ProcessName, StringComparison.OrdinalIgnoreCase));
            _estado.Text = Textos.F("estado.fueraBiblio", v.ProcessName);
        }
        else
        {
            _config.Favoritos.Add(new Favorito { Proceso = v.ProcessName });
            _estado.Text = Textos.F("estado.enBiblio", v.ProcessName);
        }
        ConfigStore.Guardar(_config);
        Refrescar();
    }

    // El bloqueo de ratón es global y frágil: cualquier cambio de foco lo limpia y el juego
    // puede pisarlo. El watcher lo re-impone solo si la ventana bloqueada sigue activa;
    // re-clipar sin foco secuestraría el ratón del usuario.
    private void MantenerBloqueoRaton()
    {
        if (_ratonBloqueado == IntPtr.Zero) return;
        if (!NativeMethods.IsWindow(_ratonBloqueado))
        {
            _ratonBloqueado = IntPtr.Zero;
            NativeMethods.ClipCursor(IntPtr.Zero);
            return;
        }
        if (NativeMethods.GetForegroundWindow() == _ratonBloqueado)
            ClipRaton(_ratonBloqueado);
    }

    private static void ClipRaton(IntPtr hwnd)
    {
        // Rect cliente en coordenadas de pantalla: GetWindowRect incluiría los bordes
        if (!NativeMethods.GetClientRect(hwnd, out var rc)) return;
        var esquina = new NativeMethods.POINT();
        if (!NativeMethods.ClientToScreen(hwnd, ref esquina)) return;
        var r = new NativeMethods.RECT
        {
            Left = esquina.X,
            Top = esquina.Y,
            Right = esquina.X + rc.Right,
            Bottom = esquina.Y + rc.Bottom,
        };
        NativeMethods.ClipCursor(ref r);
    }

    private void AlternarBloqueoRaton()
    {
        _bandeja.BalloonTipTitle = "Marco";
        if (_ratonBloqueado != IntPtr.Zero)
        {
            _ratonBloqueado = IntPtr.Zero;
            NativeMethods.ClipCursor(IntPtr.Zero);
            _bandeja.BalloonTipText = Textos.T("globo.ratonOff");
        }
        else
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == Handle) return;
            _ratonBloqueado = hwnd;
            ClipRaton(hwnd);
            _bandeja.BalloonTipText = Textos.T("globo.ratonOn");
        }
        _bandeja.ShowBalloonTip(1200);
    }

    // Silenciar en segundo plano: mutea la sesión de audio del favorito cuando su ventana
    // pierde el foco y la devuelve al volver o al cerrarse. Solo se toca lo muteado aquí.
    private void MantenerSilencios(List<WindowInfo> ventanas)
    {
        if (_watcherPausado || _config.Favoritos.Count == 0)
        {
            if (_pidsSilenciados.Count == 0) return;
            foreach (int pid in _pidsSilenciados) AudioService.Silenciar(pid, false);
            _pidsSilenciados.Clear();
            return;
        }

        NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out uint pidActivo);
        var conSilencio = new HashSet<int>();
        foreach (var v in ventanas)
            if (BuscarFavorito(v.ProcessName) is { SilenciarFondo: true })
                conSilencio.Add(v.Pid);

        foreach (int pid in conSilencio)
            if (pid != pidActivo && !_pidsSilenciados.Contains(pid) && AudioService.Silenciar(pid, true))
                _pidsSilenciados.Add(pid);
        foreach (int pid in _pidsSilenciados.Where(p => p == pidActivo || !conSilencio.Contains(p)).ToList())
        {
            AudioService.Silenciar(pid, false);
            _pidsSilenciados.Remove(pid);
        }
    }

    // Hotkey global: borderless/deshacer sobre la ventana en primer plano
    private void AlternarVentanaActiva()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == Handle) return;
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == Environment.ProcessId) return;
        bool aplicada = BorderlessService.TieneEstadoGuardado(hwnd);
        string error;
        bool exito = aplicada
            ? BorderlessService.Restaurar(hwnd, out error)
            : BorderlessService.Aplicar(hwnd, out error);
        if (exito)
        {
            if (aplicada) _deshechasManualmente.Add(hwnd.ToInt64());
            else _deshechasManualmente.Remove(hwnd.ToInt64());
        }
        _bandeja.BalloonTipTitle = "Marco";
        _bandeja.BalloonTipText = exito
            ? (aplicada ? Textos.T("bandeja.restaurada") : Textos.T("bandeja.sinbordes"))
            : error;
        _bandeja.ShowBalloonTip(1200);
        if (Visible) Refrescar();
    }

    private void ConfigurarAutoarranque(bool activar)
    {
        const string clave = @"Software\Microsoft\Windows\CurrentVersion\Run";
        try
        {
            using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(clave, writable: true);
            if (activar)
                run?.SetValue("Marco", $"\"{Application.ExecutablePath}\" --bandeja");
            else
                run?.DeleteValue("Marco", throwOnMissingValue: false);
            run?.DeleteValue("SinBordes", throwOnMissingValue: false); // entrada del nombre antiguo
        }
        catch
        {
            _estado.Text = Textos.T("estado.autoarranqueError");
        }
    }

    // Icono pixel-art (mismo diseño que icono.ico): la pantalla con el marco
    // rompiéndose por la esquina — Marco, el marco que quitamos.
    // . fondo morado, D oscuro, G gris pantalla, W brillo
    private static readonly string[] PixelesIcono =
    {
        "................",
        "................",
        "................",
        "..DDDDDDDDDDDD..",
        "..DGGGGGGGGGGD..",
        "..DGWWGGGGGGGD..",
        "..DGWGGGGGGGGD..",
        "..DGGGGGGGGGGD..",
        "..DGGGGGGGGGGD..",
        "..DGGGGGGGGGGD..",
        "..DGGGGGGGGGGD..",
        "..DDDDDDDDDDDD..",
        "................",
        "................",
        "................",
        "................",
    };

    // Renderiza la matriz de píxeles a cualquier lado (escalado nearest para pixel nítido)
    private static Bitmap DibujarPixeles(int lado)
    {
        var mini = new Bitmap(16, 16);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                char c = PixelesIcono[y][x];
                // esquinas del fondo recortadas (transparentes), como en el .ico
                bool esquina = ((x == 0 || x == 15) && (y == 0 || y == 15))
                    || ((x == 1 || x == 14) && (y == 0 || y == 15))
                    || ((x == 0 || x == 15) && (y == 1 || y == 14));
                if (esquina && c == '.') continue;
                mini.SetPixel(x, y, c switch
                {
                    'D' => Color.FromArgb(30, 27, 38),
                    'G' => Color.FromArgb(201, 201, 206),
                    'W' => Color.FromArgb(240, 240, 244),
                    _ => Color.FromArgb(122, 74, 214),
                });
            }
        }
        if (lado == 16) return mini;
        var bmp = new Bitmap(lado, lado);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(mini, 0, 0, lado, lado);
        }
        mini.Dispose();
        return bmp;
    }

    private static Icon CrearIcono()
    {
        using var bmp = DibujarPixeles(32);
        return Icon.FromHandle(bmp.GetHicon());
    }

    // Sin marco del sistema no hay bordes de agarre: se reconstruyen sobre el anillo de Padding
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam == (IntPtr)1)
            AlternarVentanaActiva();
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam == (IntPtr)2)
            AlternarBloqueoRaton();
        base.WndProc(ref m);
        if (m.Msg != WM_NCHITTEST || m.Result != (IntPtr)1 || WindowState != FormWindowState.Normal)
            return;
        int x = unchecked((short)(long)m.LParam);
        int y = unchecked((short)((long)m.LParam >> 16));
        var p = PointToClient(new Point(x, y));
        const int esquina = 14;
        bool izq = p.X < esquina, der = p.X >= ClientSize.Width - esquina;
        bool arriba = p.Y < esquina, abajo = p.Y >= ClientSize.Height - esquina;
        m.Result = (IntPtr)(
            arriba && izq ? 13 : arriba && der ? 14 :          // HTTOPLEFT / HTTOPRIGHT
            abajo && izq ? 16 : abajo && der ? 17 :            // HTBOTTOMLEFT / HTBOTTOMRIGHT
            p.X < Padding.Left ? 10 :                          // HTLEFT
            p.X >= ClientSize.Width - Padding.Right ? 11 :     // HTRIGHT
            p.Y < Padding.Top ? 12 :                           // HTTOP
            p.Y >= ClientSize.Height - Padding.Bottom ? 15 :   // HTBOTTOM
            1);
    }

    private static Button BotonTitulo(string glifo, Action accion)
    {
        var boton = new BotonRedondeado
        {
            Text = glifo,
            Font = new Font("Segoe MDL2 Assets", 9f),
            Size = new Size(38, 32),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            TabStop = false,
        };
        boton.FlatAppearance.BorderSize = 0;
        boton.Click += (_, _) => accion();
        return boton;
    }

    private static BotonRedondeado Boton(string texto, Action accion)
    {
        var boton = new BotonRedondeado
        {
            Text = texto,
            AutoSize = true,
            Padding = new Padding(10, 5, 10, 5),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        boton.FlatAppearance.BorderSize = 0;
        boton.Click += (_, _) => accion();
        return boton;
    }

    private void AplicarTema()
    {
        try { _fondo = ColorTranslator.FromHtml(_config.ColorFondo); }
        catch { _fondo = ColorTranslator.FromHtml(new Config().ColorFondo); }
        try { _acento = ColorTranslator.FromHtml(_config.ColorAcento); }
        catch { _acento = ColorTranslator.FromHtml(new Config().ColorAcento); }

        bool oscuro = _fondo.GetBrightness() < 0.5f;
        try { _texto = ColorTranslator.FromHtml(_config.ColorTexto); }
        catch { _texto = ColorTranslator.FromHtml(new Config().ColorTexto); }
        _textoSuave = Mezclar(_texto, _fondo, 0.45f);
        _panel = Mezclar(_fondo, oscuro ? Color.White : Color.Black, oscuro ? 0.08f : 0.05f);
        if (!string.IsNullOrEmpty(_config.ColorPanel))
            try { _panel = ColorTranslator.FromHtml(_config.ColorPanel); } catch { }
        _textoPanel = _texto;
        _seleccion = Mezclar(_fondo, oscuro ? Color.White : Color.Black, oscuro ? 0.10f : 0.07f);

        _fuenteNegrita?.Dispose();
        _fuentePequena?.Dispose();
        _fuenteNegrita = new Font("Segoe UI Semibold", 9.75f);
        _fuentePequena = new Font("Segoe UI", 8.5f);

        BackColor = _fondo;
        // Como en LS: la barra de título es el propio fondo, no una caja de color;
        // el color de panel queda solo para botones y tarjetas
        _barraTitulo.BackColor = _fondo;
        _titulo.ForeColor = _texto;
        _marcoLista.BackColor = _fondo;
        _lista.BackColor = Mezclar(_fondo, oscuro ? Color.White : Color.Black, oscuro ? 0.06f : 0.04f);
        _botones.BackColor = _fondo;
        _estado.ForeColor = _textoSuave;
        _version.ForeColor = _textoSuave;

        int radio = _config.BordesRedondeados ? 6 : 0;
        bool panelOscuro = _panel.GetBrightness() < 0.5f;

        foreach (var boton in _barraTitulo.Controls.OfType<BotonRedondeado>())
        {
            boton.BackColor = _fondo;
            boton.ForeColor = Mezclar(_texto, _fondo, 0.25f);
            boton.FlatAppearance.MouseOverBackColor = _panel;
            boton.Radio = radio;
            boton.Borde = Color.Transparent;
        }

        foreach (var boton in _botones.Controls.OfType<BotonRedondeado>())
        {
            boton.BackColor = _panel;
            boton.ForeColor = _textoPanel;
            boton.FlatAppearance.MouseOverBackColor =
                Mezclar(_panel, panelOscuro ? Color.White : Color.Black, 0.12f);
            boton.Radio = radio;
            // Borde 1px derivado del relleno: diferencia el botón aunque comparta color con el fondo
            boton.Borde = Mezclar(_panel, panelOscuro ? Color.White : Color.Black, 0.28f);
        }

        ReconstruirAjustes();
        Invalidate(true);
        _lista.Invalidate();
    }

    private static Color Mezclar(Color origen, Color destino, float peso) => Color.FromArgb(
        origen.R + (int)((destino.R - origen.R) * peso),
        origen.G + (int)((destino.G - origen.G) * peso),
        origen.B + (int)((destino.B - origen.B) * peso));

    // Fondo real tras un control: Clear(Transparent) pinta negro, así que hay que subir
    // hasta el primer ancestro con color opaco (los contenedores de ajustes son transparentes)
    private static Color FondoEfectivo(Control control)
    {
        for (Control? padre = control.Parent; padre != null; padre = padre.Parent)
            if (padre.BackColor.A == 255) return padre.BackColor;
        return control.BackColor;
    }

    private void DibujarFila(Graphics g, WindowInfo v, Rectangle limites, bool seleccionada)
    {
        var zona = new Rectangle(limites.X + 8, limites.Y + 3, limites.Width - 16, limites.Height - 6);
        if (seleccionada)
        {
            using var fondoSel = new SolidBrush(_seleccion);
            if (_config.BordesRedondeados)
            {
                using var camino = CaminoRedondeado(zona, 6);
                g.FillPath(fondoSel, camino);
            }
            else
            {
                g.FillRectangle(fondoSel, zona);
            }
            using var franja = new SolidBrush(_acento);
            g.FillRectangle(franja, zona.X, zona.Y + 6, 3, zona.Height - 12);
        }

        bool apagada = v.Hwnd == IntPtr.Zero;
        bool aplicada = !apagada && BorderlessService.TieneEstadoGuardado(v.Hwnd);
        int derecha = zona.Right - 10;
        if (aplicada)
            derecha = Pildora(g, zona, Textos.T("pill.sinbordes"), _acento);
        else if (v.Elevada)
            derecha = Pildora(g, zona, Textos.T("pill.admin"), Color.FromArgb(230, 126, 34)); // UIPI bloqueará: avisar

        var zonaTexto = new Rectangle(zona.X + 14, zona.Y + 4, derecha - zona.X - 14, zona.Height - 8);
        var lineaNombre = new Rectangle(zonaTexto.X, zonaTexto.Y, zonaTexto.Width, zonaTexto.Height / 2);
        int xNombre = zonaTexto.X;
        if (EsFavorito(v.ProcessName))
        {
            TextRenderer.DrawText(g, "★", _fuenteNegrita, lineaNombre, _acento,
                TextFormatFlags.Left | TextFormatFlags.Bottom);
            xNombre += 20;
        }
        TextRenderer.DrawText(g, v.ProcessName, _fuenteNegrita,
            new Rectangle(xNombre, zonaTexto.Y, zonaTexto.Width - (xNombre - zonaTexto.X), zonaTexto.Height / 2),
            apagada ? _textoSuave : _texto,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, v.Title, _fuentePequena,
            new Rectangle(zonaTexto.X, zonaTexto.Y + zonaTexto.Height / 2, zonaTexto.Width, zonaTexto.Height / 2),
            _textoSuave, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }

    /// <returns>Borde izquierdo de la píldora, para truncar el texto de la fila ahí.</returns>
    private int Pildora(Graphics g, Rectangle zona, string texto, Color color)
    {
        var medida = TextRenderer.MeasureText(texto, _fuentePequena);
        var pildora = new Rectangle(zona.Right - medida.Width - 22, zona.Y + (zona.Height - medida.Height - 8) / 2,
            medida.Width + 14, medida.Height + 8);
        using var fondoPildora = new SolidBrush(Color.FromArgb(45, color));
        using var caminoPildora = CaminoRedondeado(pildora, pildora.Height / 2);
        g.FillPath(fondoPildora, caminoPildora);
        TextRenderer.DrawText(g, texto, _fuentePequena, pildora,
            Mezclar(color, _texto, 0.35f),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return pildora.Left - 8;
    }

    private static GraphicsPath CaminoRedondeado(Rectangle r, int radio)
    {
        var camino = new GraphicsPath();
        if (radio <= 0)
        {
            camino.AddRectangle(r);
            return camino;
        }
        int d = radio * 2;
        camino.AddArc(r.X, r.Y, d, d, 180, 90);
        camino.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        camino.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        camino.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        camino.CloseFigure();
        return camino;
    }

    // Diálogo de ayuda temático: chrome propio, texto corto e informativo, Esc cierra
    private void AbrirAyuda()
    {
        using var dialogo = new Form
        {
            Text = Textos.T("ayuda.titulo"),
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false,
            ClientSize = new Size(620, 560),
            BackColor = _fondo,
            KeyPreview = true,
        };
        dialogo.HandleCreated += (_, _) =>
        {
            int redondeo = NativeMethods.DWMWCP_ROUND;
            _ = NativeMethods.DwmSetWindowAttribute(dialogo.Handle,
                NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref redondeo, sizeof(int));
        };
        dialogo.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) dialogo.Close(); };

        void Arrastrar(object? s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(dialogo.Handle, NativeMethods.WM_NCLBUTTONDOWN,
                new IntPtr(NativeMethods.HTCAPTION), IntPtr.Zero);
        }
        dialogo.MouseDown += Arrastrar;

        var tituloAyuda = new Label
        {
            Text = Textos.T("ayuda.titulo"),
            AutoSize = true,
            Location = new Point(18, 14),
            Font = new Font("Segoe UI Semibold", 10.5f),
            ForeColor = _texto,
            BackColor = Color.Transparent,
        };
        tituloAyuda.MouseDown += Arrastrar;

        var cerrarAyuda = BotonTitulo("\uE8BB", () => dialogo.Close());
        cerrarAyuda.BackColor = _fondo;
        cerrarAyuda.ForeColor = _textoSuave;
        cerrarAyuda.FlatAppearance.MouseOverBackColor = _panel;
        cerrarAyuda.Location = new Point(dialogo.ClientSize.Width - cerrarAyuda.Width - 8, 8);

        var cuerpo = new Label
        {
            Text = Textos.T("ayuda.texto") + "\n\n" + Textos.T("ayuda.raton"),
            Location = new Point(18, 52),
            Size = new Size(dialogo.ClientSize.Width - 36, dialogo.ClientSize.Height - 70),
            ForeColor = _texto,
            BackColor = Color.Transparent,
        };
        cuerpo.MouseDown += Arrastrar;

        dialogo.Controls.Add(tituloAyuda);
        dialogo.Controls.Add(cerrarAyuda);
        dialogo.Controls.Add(cuerpo);
        dialogo.ShowDialog(this);
    }

    // Opciones por favorito: modo de tamaño, monitor, siempre encima, silenciar de fondo
    // y retardo. Mismo chrome temático que la Ayuda; Esc cierra sin guardar.
    private void AbrirOpciones()
    {
        if (_lista.Seleccion is not { } v || BuscarFavorito(v.ProcessName) is not { } favorito) return;

        using var dialogo = new Form
        {
            Text = Textos.F("opciones.titulo", favorito.Proceso),
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false,
            ClientSize = new Size(470, 452),
            BackColor = _fondo,
            KeyPreview = true,
        };
        dialogo.HandleCreated += (_, _) =>
        {
            int redondeo = NativeMethods.DWMWCP_ROUND;
            _ = NativeMethods.DwmSetWindowAttribute(dialogo.Handle,
                NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref redondeo, sizeof(int));
        };
        dialogo.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) dialogo.Close(); };
        void Arrastrar(object? s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(dialogo.Handle, NativeMethods.WM_NCLBUTTONDOWN,
                new IntPtr(NativeMethods.HTCAPTION), IntPtr.Zero);
        }
        dialogo.MouseDown += Arrastrar;

        var tituloOpciones = new Label
        {
            Text = Textos.F("opciones.titulo", favorito.Proceso),
            AutoSize = true,
            Location = new Point(18, 14),
            Font = new Font("Segoe UI Semibold", 10.5f),
            ForeColor = _texto,
            BackColor = Color.Transparent,
        };
        tituloOpciones.MouseDown += Arrastrar;
        var cerrarOpciones = BotonTitulo("\uE8BB", () => dialogo.Close());
        cerrarOpciones.BackColor = _fondo;
        cerrarOpciones.ForeColor = _textoSuave;
        cerrarOpciones.FlatAppearance.MouseOverBackColor = _panel;
        cerrarOpciones.Location = new Point(dialogo.ClientSize.Width - cerrarOpciones.Width - 8, 8);

        RadioButton Radio(string clave, int y, bool marcado) => new()
        {
            Text = Textos.T(clave),
            AutoSize = true,
            Location = new Point(22, y),
            ForeColor = _texto,
            BackColor = Color.Transparent,
            Checked = marcado,
        };
        var modoMonitor = Radio("opciones.modoMonitor", 54, favorito.Modo == ModoTamano.Monitor);
        var modoSoloBordes = Radio("opciones.modoSoloBordes", 82, favorito.Modo == ModoTamano.SoloBordes);
        var modoPersonalizado = Radio("opciones.modoPersonalizado", 110, favorito.Modo == ModoTamano.Personalizado);

        TextBox Caja(string valor, int x, int y) => new()
        {
            Text = valor,
            Location = new Point(x, y),
            Width = 62,
            BackColor = _panel,
            ForeColor = _texto,
            BorderStyle = BorderStyle.FixedSingle,
        };
        Label Etiqueta(string texto, int x, int y, bool suave = false) => new()
        {
            Text = texto,
            AutoSize = true,
            Location = new Point(x, y),
            ForeColor = suave ? _textoSuave : _texto,
            BackColor = Color.Transparent,
        };

        var etAncho = Etiqueta(Textos.T("opciones.ancho"), 44, 141);
        var cajaAncho = Caja(favorito.Ancho > 0 ? favorito.Ancho.ToString() : "", 44, 162);
        var etAlto = Etiqueta(Textos.T("opciones.alto"), 118, 141);
        var cajaAlto = Caja(favorito.Alto > 0 ? favorito.Alto.ToString() : "", 118, 162);
        var etPosicion = Etiqueta(Textos.T("opciones.posicion"), 200, 141, suave: true);
        var cajaX = Caja(favorito.PosX?.ToString() ?? "", 200, 162);
        var cajaY = Caja(favorito.PosY?.ToString() ?? "", 272, 162);
        void RefrescarHabilitado()
        {
            foreach (Control c in new Control[] { etAncho, cajaAncho, etAlto, cajaAlto, etPosicion, cajaX, cajaY })
                c.Enabled = modoPersonalizado.Checked;
        }
        modoPersonalizado.CheckedChanged += (_, _) => RefrescarHabilitado();
        RefrescarHabilitado();

        // Monitor: botón que rota entre "automático" y cada pantalla conectada. Se persiste
        // el DeviceName (estable entre reinicios); si esa pantalla ya no está, cae al cercano
        var etMonitor = Etiqueta(Textos.T("opciones.monitor"), 22, 204);
        var pantallas = Screen.AllScreens;
        var monitores = new List<(string Dispositivo, string Texto)>
        {
            ("", Textos.T("opciones.monitorAuto")),
        };
        for (int i = 0; i < pantallas.Length; i++)
            monitores.Add((pantallas[i].DeviceName,
                Textos.F("opciones.monitorN", i + 1, pantallas[i].Bounds.Width, pantallas[i].Bounds.Height)));
        int monitorActual = Math.Max(0, monitores.FindIndex(m => m.Dispositivo == favorito.MonitorDispositivo));
        var botonMonitor = BotonChico(monitores[monitorActual].Texto);
        botonMonitor.Location = new Point(44, 226);
        botonMonitor.Click += (_, _) =>
        {
            monitorActual = (monitorActual + 1) % monitores.Count;
            botonMonitor.Text = monitores[monitorActual].Texto;
        };

        CheckBox Casilla(string clave, int y, bool marcado) => new()
        {
            Text = Textos.T(clave),
            AutoSize = true,
            Location = new Point(22, y),
            ForeColor = _texto,
            BackColor = Color.Transparent,
            Checked = marcado,
        };
        var siempreEncima = Casilla("opciones.siempreEncima", 268, favorito.SiempreEncima);
        var silenciar = Casilla("opciones.silenciar", 296, favorito.SilenciarFondo);

        var etRetardo = Etiqueta(Textos.T("opciones.retardo"), 22, 330);
        var cajaRetardo = Caja(favorito.RetardoSegundos > 0 ? favorito.RetardoSegundos.ToString() : "", 24, 352);

        var pie = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(10),
            BackColor = Color.Transparent,
        };
        var cancelar = BotonChico(Textos.T("opciones.cancelar"));
        cancelar.Click += (_, _) => dialogo.Close();
        var guardar = BotonChico(Textos.T("opciones.guardar"));
        guardar.Click += (_, _) =>
        {
            favorito.Modo = modoSoloBordes.Checked ? ModoTamano.SoloBordes
                : modoPersonalizado.Checked ? ModoTamano.Personalizado
                : ModoTamano.Monitor;
            favorito.Ancho = int.TryParse(cajaAncho.Text, out int ancho) && ancho > 0 ? ancho : 0;
            favorito.Alto = int.TryParse(cajaAlto.Text, out int alto) && alto > 0 ? alto : 0;
            favorito.PosX = int.TryParse(cajaX.Text, out int x) ? x : null;
            favorito.PosY = int.TryParse(cajaY.Text, out int y) ? y : null;
            favorito.MonitorDispositivo = monitores[monitorActual].Dispositivo;
            favorito.SiempreEncima = siempreEncima.Checked;
            favorito.SilenciarFondo = silenciar.Checked;
            favorito.RetardoSegundos = int.TryParse(cajaRetardo.Text, out int retardo) && retardo > 0
                ? Math.Min(retardo, 600) : 0;
            ConfigStore.Guardar(_config);
            // Ventana ya sin bordes: re-aplicar al momento con las opciones nuevas
            if (v.Hwnd != IntPtr.Zero && BorderlessService.TieneEstadoGuardado(v.Hwnd))
                BorderlessService.Aplicar(v.Hwnd, favorito, out _);
            _estado.Text = Textos.F("estado.opciones", favorito.Proceso);
            dialogo.Close();
            Refrescar();
        };
        pie.Controls.Add(cancelar);
        pie.Controls.Add(guardar);

        dialogo.Controls.Add(tituloOpciones);
        dialogo.Controls.Add(cerrarOpciones);
        dialogo.Controls.Add(modoMonitor);
        dialogo.Controls.Add(modoSoloBordes);
        dialogo.Controls.Add(modoPersonalizado);
        dialogo.Controls.Add(etAncho);
        dialogo.Controls.Add(cajaAncho);
        dialogo.Controls.Add(etAlto);
        dialogo.Controls.Add(cajaAlto);
        dialogo.Controls.Add(etPosicion);
        dialogo.Controls.Add(cajaX);
        dialogo.Controls.Add(cajaY);
        dialogo.Controls.Add(etMonitor);
        dialogo.Controls.Add(botonMonitor);
        dialogo.Controls.Add(siempreEncima);
        dialogo.Controls.Add(silenciar);
        dialogo.Controls.Add(etRetardo);
        dialogo.Controls.Add(cajaRetardo);
        dialogo.Controls.Add(pie);
        dialogo.ShowDialog(this);
    }

    private void OcultarSeleccionada()
    {
        if (_lista.Seleccion is not { } v)
        {
            _estado.Text = Textos.T("estado.selecciona");
            return;
        }
        if (!EstaOculta(v.ProcessName)) _config.Ocultas.Add(v.ProcessName);
        ConfigStore.Guardar(_config);
        _estado.Text = Textos.F("estado.oculta", v.ProcessName);
        ReconstruirAjustes(); // el contador de «mostrar ocultas» acaba de cambiar
        Refrescar();
    }

    private void MostrarOcultas()
    {
        if (_config.Ocultas.Count == 0) return;
        _config.Ocultas.Clear();
        ConfigStore.Guardar(_config);
        _estado.Text = Textos.T("estado.ocultasVaciadas");
        ReconstruirAjustes();
        Refrescar();
    }

    private void AlternarAjustes()
    {
        if (!_panelAjustes.Visible)
        {
            _panelAjustes.Visible = true;
            Height += _panelAjustes.Height; // que los ajustes no se coman la lista
        }
        else
        {
            Height -= _panelAjustes.Height;
            _panelAjustes.Visible = false;
        }
    }

    private void ReconstruirAjustes()
    {
        _panelAjustes.SuspendLayout();
        _panelAjustes.Controls.Clear();
        _panelAjustes.BackColor = _fondo;

        // Colores: muestras clicables con el color actual + restablecer
        var muestras = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, // sin esto el alto se clava en 100px
            WrapContents = false,
            Margin = new Padding(0),
            BackColor = Color.Transparent,
        };
        muestras.Controls.Add(Muestra(_fondo, c => _config.ColorFondo = c));
        muestras.Controls.Add(Muestra(_panel, c => _config.ColorPanel = c));
        muestras.Controls.Add(Muestra(_acento, c => _config.ColorAcento = c));
        muestras.Controls.Add(Muestra(_texto, c => _config.ColorTexto = c));
        var restablecer = BotonChico(Textos.T("menu.restablecer"));
        restablecer.Click += (_, _) =>
        {
            var defecto = new Config();
            _config.ColorFondo = defecto.ColorFondo;
            _config.ColorPanel = defecto.ColorPanel;
            _config.ColorAcento = defecto.ColorAcento;
            _config.ColorTexto = defecto.ColorTexto;
            ConfigStore.Guardar(_config);
            AplicarTema();
            _estado.Text = Textos.T("estado.coloresDefecto");
        };
        muestras.Controls.Add(restablecer);
        _panelAjustes.Controls.Add(FilaAjuste(Textos.T("menu.colores"), muestras));

        // Idioma: una sola caja; al clicar se despliega la lista vertical (10 filas, rueda)
        string nombreActual = "English";
        foreach (var (codigo, nombre) in Textos.Disponibles)
            if (codigo == Textos.Idioma) { nombreActual = nombre; break; }
        var idiomaBoton = BotonChico(nombreActual + "  ▾");
        idiomaBoton.Click += (_, _) => MostrarIdiomas(idiomaBoton);
        _panelAjustes.Controls.Add(FilaAjuste(Textos.T("menu.idioma"), idiomaBoton));

        _panelAjustes.Controls.Add(FilaAjuste(Textos.T("menu.esquinas"),
            Palanca(_config.BordesRedondeados, v =>
            {
                _config.BordesRedondeados = v;
                ConfigStore.Guardar(_config);
                AplicarTema();
            })));
        _panelAjustes.Controls.Add(FilaAjuste(Textos.T("menu.autoarranque"),
            Palanca(_config.IniciarConWindows, v =>
            {
                _config.IniciarConWindows = v;
                ConfigurarAutoarranque(v);
                ConfigStore.Guardar(_config);
            })));
        _panelAjustes.Controls.Add(FilaAjuste(Textos.T("menu.arrancarMin"),
            Palanca(_config.ArrancarMinimizado, v =>
            {
                _config.ArrancarMinimizado = v;
                ConfigStore.Guardar(_config);
            })));
        _panelAjustes.Controls.Add(FilaAjuste(Textos.T("menu.cerrarMin"),
            Palanca(_config.CerrarMinimiza, v =>
            {
                _config.CerrarMinimiza = v;
                ConfigStore.Guardar(_config);
            })));

        // Exclusiones: esconder la fila seleccionada / recuperar todas las escondidas
        var botonesLista = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0),
            BackColor = Color.Transparent,
        };
        var ocultar = BotonChico(Textos.T("menu.ocultar"));
        ocultar.Click += (_, _) => OcultarSeleccionada();
        botonesLista.Controls.Add(ocultar);
        var mostrarOcultas = BotonChico(Textos.F("menu.mostrarOcultas", _config.Ocultas.Count));
        mostrarOcultas.Click += (_, _) => MostrarOcultas();
        botonesLista.Controls.Add(mostrarOcultas);
        _panelAjustes.Controls.Add(FilaAjuste(Textos.T("menu.lista"), botonesLista));

        _panelAjustes.Controls.Add(FilaHotkey(Textos.T("hotkey.accion"),
            () => _config.Hotkey, v => _config.Hotkey = v, 1));
        _panelAjustes.Controls.Add(FilaHotkey(Textos.T("hotkey.raton"),
            () => _config.HotkeyRaton, v => _config.HotkeyRaton = v, 2));

        _panelAjustes.ResumeLayout();
    }

    // Fila de ajuste estilo LS: etiqueta a la izquierda, control alineado a la derecha
    private Panel FilaAjuste(string etiqueta, Control control, bool suave = false)
    {
        var fila = new Panel
        {
            Height = 32,
            Margin = new Padding(0, 1, 0, 1),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.Transparent,
        };
        var texto = new Label
        {
            Text = etiqueta,
            AutoSize = true,
            ForeColor = suave ? _textoSuave : _texto,
            BackColor = Color.Transparent,
        };
        fila.Controls.Add(texto);
        fila.Controls.Add(control);
        void Colocar()
        {
            texto.Location = new Point(0, (fila.Height - texto.Height) / 2);
            control.Location = new Point(Math.Max(texto.Right + 8, fila.Width - control.Width),
                (fila.Height - control.Height) / 2);
        }
        fila.Resize += (_, _) => Colocar();
        control.Resize += (_, _) => Colocar();
        Colocar();
        return fila;
    }

    // Fila de hotkey configurable: el botón muestra el combo actual; al clicar entra en
    // modo captura y la siguiente pulsación se convierte en el nuevo hotkey. El registro
    // se prueba en vivo: si otra app ya tiene ese combo, se repone el anterior y se avisa.
    private Panel FilaHotkey(string etiqueta, Func<string> leer, Action<string> escribir, int id)
    {
        var boton = BotonChico(leer());
        bool capturando = false;
        void Terminar()
        {
            capturando = false;
            boton.Text = leer();
        }
        boton.Click += (_, _) =>
        {
            capturando = true;
            boton.Text = Textos.T("hotkey.captura");
        };
        boton.LostFocus += (_, _) => { if (capturando) Terminar(); };
        boton.PreviewKeyDown += (_, e) => { if (capturando) e.IsInputKey = true; };
        boton.KeyDown += (_, e) =>
        {
            if (!capturando) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            // Un modificador suelto no es un combo todavía: seguir esperando
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
            if (e.KeyCode == Keys.Escape)
            {
                Terminar();
                return;
            }
            // Sin modificador solo se admiten teclas "seguras" (F1-F24, BloqDespl, Pausa):
            // registrar una letra pelada secuestraría la escritura de todo el sistema
            bool sola = e.KeyCode is (>= Keys.F1 and <= Keys.F24) or Keys.Scroll or Keys.Pause;
            if (e.Modifiers == Keys.None && !sola)
            {
                _estado.Text = Textos.T("estado.hotkeyModificador");
                return;
            }
            string combo = ComboATexto(e.Modifiers, e.KeyCode);
            NativeMethods.UnregisterHotKey(Handle, id);
            if (ParseHotkey(combo, out uint mods, out uint vk) &&
                NativeMethods.RegisterHotKey(Handle, id, mods, vk))
            {
                escribir(combo);
                ConfigStore.Guardar(_config);
                _estado.Text = Textos.F("estado.hotkeyGuardado", combo);
            }
            else
            {
                _estado.Text = Textos.F("estado.hotkeyError", combo);
                if (ParseHotkey(leer(), out uint m0, out uint v0))
                    NativeMethods.RegisterHotKey(Handle, id, m0, v0); // reponer el anterior
            }
            Terminar();
        };
        return FilaAjuste(etiqueta, boton);
    }

    private void MostrarIdiomas(Control ancla)
    {
        // Reabrir con el mismo clic que lo cerró (perdió el foco un instante antes): ignorar
        if ((DateTime.Now - _desplegableIdiomas.UltimoCierre).TotalMilliseconds < 250) return;
        var esquina = PointToClient(ancla.PointToScreen(Point.Empty));
        int ancho = Math.Max(250, ancla.Width);
        int x = Math.Clamp(esquina.X + ancla.Width - ancho, Padding.Left,
            Math.Max(Padding.Left, ClientSize.Width - ancho - Padding.Right));
        // Hacia arriba: la fila de idioma vive al fondo de la ventana
        int y = esquina.Y - _desplegableIdiomas.Height - 4;
        if (y < _barraTitulo.Bottom) y = esquina.Y + ancla.Height + 4;
        _desplegableIdiomas.Width = ancho;
        _desplegableIdiomas.Mostrar(new Point(x, y));
    }

    private BotonRedondeado BotonChico(string texto)
    {
        bool panelOscuro = _panel.GetBrightness() < 0.5f;
        var boton = new BotonRedondeado
        {
            Text = texto,
            AutoSize = true,
            Padding = new Padding(6, 2, 6, 2),
            Margin = new Padding(3, 0, 3, 0),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = _fuentePequena,
            BackColor = _panel,
            ForeColor = _textoPanel,
            Radio = _config.BordesRedondeados ? 6 : 0,
            Borde = Mezclar(_panel, panelOscuro ? Color.White : Color.Black, 0.28f),
        };
        boton.FlatAppearance.BorderSize = 0;
        boton.FlatAppearance.MouseOverBackColor =
            Mezclar(_panel, panelOscuro ? Color.White : Color.Black, 0.12f);
        return boton;
    }

    private BotonRedondeado Muestra(Color color, Action<string> asignar)
    {
        var boton = new BotonRedondeado
        {
            Size = new Size(34, 24),
            Margin = new Padding(3, 0, 3, 0),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            BackColor = color,
            Radio = _config.BordesRedondeados ? 6 : 0,
            Borde = Mezclar(color, color.GetBrightness() < 0.5f ? Color.White : Color.Black, 0.35f),
        };
        boton.FlatAppearance.BorderSize = 0;
        boton.FlatAppearance.MouseOverBackColor = color;
        boton.Click += (_, _) => ElegirColor(asignar, color);
        return boton;
    }

    private Interruptor Palanca(bool valor, Action<bool> asignar)
    {
        var palanca = new Interruptor
        {
            Encendido = valor,
            ColorEncendido = _acento,
            ColorApagado = Mezclar(_panel, _texto, 0.18f),
            Margin = new Padding(0),
        };
        palanca.Cambiado += asignar;
        return palanca;
    }

    private void ElegirColor(Action<string> asignar, Color actual)
    {
        using var dialogo = new ColorDialog { Color = actual, FullOpen = true };
        if (dialogo.ShowDialog(this) != DialogResult.OK) return;
        asignar(ColorTranslator.ToHtml(dialogo.Color));
        ConfigStore.Guardar(_config);
        AplicarTema();
        _estado.Text = Textos.T("estado.coloresGuardados");
    }

    private void Refrescar() => RefrescarCon(WindowEnumerator.Listar());

    private void RefrescarCon(List<WindowInfo> ventanas)
    {
        // La biblioteca: los favoritos sin ejecutar aparecen al final, apagados
        var visibles = new List<WindowInfo>(ventanas);
        foreach (var favorito in _config.Favoritos)
            if (!ventanas.Any(v => v.ProcessName.Equals(favorito.Proceso, StringComparison.OrdinalIgnoreCase)))
                visibles.Add(new WindowInfo(IntPtr.Zero, Textos.T("fila.apagada"), favorito.Proceso, 0));
        // Las ocultas desaparecen de la lista; el watcher no se entera (usa la enumeración cruda)
        visibles.RemoveAll(v => EstaOculta(v.ProcessName));
        _lista.Cargar(visibles);
        ActualizarBotonAccion();
    }

    private bool EstaOculta(string proceso) =>
        _config.Ocultas.Any(o => o.Equals(proceso, StringComparison.OrdinalIgnoreCase));

    // Los botones son contextuales según la fila seleccionada
    private void ActualizarBotonAccion()
    {
        var v = _lista.Seleccion;
        _botonAccion.Text = v is not null && v.Hwnd != IntPtr.Zero && BorderlessService.TieneEstadoGuardado(v.Hwnd)
            ? Textos.T("boton.deshacer")
            : Textos.T("boton.aplicar");
        bool favorita = v is not null && EsFavorito(v.ProcessName);
        _botonFavorito.Text = favorita ? Textos.T("boton.quitar") : Textos.T("boton.guardar");
        _botonOpciones.Text = Textos.T("boton.opciones");
        _botonOpciones.Visible = favorita;
    }

    private void AccionPrincipal()
    {
        if (_lista.Seleccion is not { } v)
        {
            _estado.Text = Textos.T("estado.selecciona");
            return;
        }
        if (v.Hwnd == IntPtr.Zero)
        {
            _estado.Text = Textos.T("estado.noEjecucion");
            return;
        }
        bool aplicada = BorderlessService.TieneEstadoGuardado(v.Hwnd);
        string error;
        bool exito = aplicada
            ? BorderlessService.Restaurar(v.Hwnd, out error)
            : BorderlessService.Aplicar(v.Hwnd, out error);
        if (exito)
        {
            if (aplicada) _deshechasManualmente.Add(v.Hwnd.ToInt64());
            else _deshechasManualmente.Remove(v.Hwnd.ToInt64());
        }
        _estado.Text = exito
            ? (aplicada
                ? Textos.F("estado.restaurada", v.Title, v.ProcessName)
                : Textos.F("estado.aplicada", v.Title, v.ProcessName))
            : error;
        Refrescar();
    }

    // Botón pintado a mano: Region recorta sin antialiasing (esquinas dentadas); aquí el
    // redondeo se dibuja suavizado y con borde de 1px opcional
    private sealed class BotonRedondeado : Button
    {
        public int Radio = 6;
        public Color Borde = Color.Transparent;
        private bool _sobre;

        public BotonRedondeado()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _sobre = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _sobre = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(FondoEfectivo(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var zona = new Rectangle(0, 0, Width - 1, Height - 1);
            using var camino = CaminoRedondeado(zona, Radio);
            using var brocha = new SolidBrush(_sobre ? FlatAppearance.MouseOverBackColor : BackColor);
            g.FillPath(brocha, camino);
            if (Borde.A > 0)
            {
                using var pluma = new Pen(Borde);
                g.DrawPath(pluma, camino);
            }
            TextRenderer.DrawText(g, Text, Font, zona, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // Desplegable de idiomas propio: caja temática que abre una lista vertical de
    // 10 filas visibles con scroll de rueda; se cierra al elegir o al perder el foco
    private sealed class DesplegableIdiomas : Control
    {
        private const int AltoFila = 28;
        private const int Visibles = 10;

        private readonly MainForm _dueno;
        private float _scroll;
        private int _hover = -1;

        public DateTime UltimoCierre;
        public event Action<string>? Elegido;

        public DesplegableIdiomas(MainForm dueno)
        {
            _dueno = dueno;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            Size = new Size(250, AltoFila * Visibles + 8);
            Visible = false;
        }

        public void Mostrar(Point posicion)
        {
            Location = posicion;
            int actual = 0;
            for (int i = 0; i < Textos.Disponibles.Length; i++)
                if (Textos.Disponibles[i].Codigo == Textos.Idioma) { actual = i; break; }
            _scroll = Math.Clamp(actual * AltoFila - Height / 2f + AltoFila / 2f, 0, MaxScroll);
            _hover = -1;
            Visible = true;
            BringToFront();
            Focus();
            Invalidate();
        }

        private float MaxScroll => Math.Max(0, Textos.Disponibles.Length * AltoFila - (Height - 8));

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Visible = false;
            UltimoCierre = DateTime.Now;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _scroll = Math.Clamp(_scroll - e.Delta / 120f * AltoFila * 2, 0, MaxScroll);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int fila = (int)((e.Y - 4 + _scroll) / AltoFila);
            if (fila != _hover)
            {
                _hover = fila;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int fila = (int)((e.Y - 4 + _scroll) / AltoFila);
            if (fila >= 0 && fila < Textos.Disponibles.Length)
            {
                Visible = false;
                UltimoCierre = DateTime.Now;
                Elegido?.Invoke(Textos.Disponibles[fila].Codigo);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(FondoEfectivo(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var panel = _dueno._panel;
            bool panelOscuro = panel.GetBrightness() < 0.5f;
            var caja = new Rectangle(0, 0, Width - 1, Height - 1);
            using var camino = CaminoRedondeado(caja, _dueno._config.BordesRedondeados ? 8 : 0);
            using var fondo = new SolidBrush(panel);
            g.FillPath(fondo, camino);
            using var borde = new Pen(Mezclar(panel, panelOscuro ? Color.White : Color.Black, 0.28f));
            g.DrawPath(borde, camino);
            g.SetClip(camino);
            var idiomas = Textos.Disponibles;
            int primera = Math.Max(0, (int)(_scroll / AltoFila));
            int ultima = Math.Min(idiomas.Length - 1, (int)((_scroll + Height) / AltoFila) + 1);
            for (int i = primera; i <= ultima; i++)
            {
                int y = (int)(i * AltoFila - _scroll) + 4;
                var zona = new Rectangle(4, y, Width - 8, AltoFila);
                if (i == _hover)
                {
                    using var resaltado = new SolidBrush(
                        Mezclar(_dueno._panel, panelOscuro ? Color.White : Color.Black, 0.10f));
                    using var caminoFila = CaminoRedondeado(zona, 5);
                    g.FillPath(resaltado, caminoFila);
                }
                TextRenderer.DrawText(g, idiomas[i].Nombre, Font,
                    new Rectangle(zona.X + 10, zona.Y, zona.Width - 20, zona.Height),
                    idiomas[i].Codigo == Textos.Idioma ? _dueno._acento : _dueno._textoPanel,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            g.ResetClip();
        }
    }

    // Interruptor de palanca estilo LS: pista redondeada + bola, pintado con antialias
    private sealed class Interruptor : Control
    {
        public bool Encendido;
        public Color ColorEncendido = Color.MediumSlateBlue;
        public Color ColorApagado = Color.Gray;
        public event Action<bool>? Cambiado;

        public Interruptor()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            Size = new Size(44, 22);
            Cursor = Cursors.Hand;
        }

        protected override void OnClick(EventArgs e)
        {
            Encendido = !Encendido;
            Invalidate();
            Cambiado?.Invoke(Encendido);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(FondoEfectivo(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var pista = new Rectangle(0, 0, Width - 1, Height - 1);
            using var camino = CaminoRedondeado(pista, Height / 2);
            using var fondo = new SolidBrush(Encendido ? ColorEncendido : ColorApagado);
            g.FillPath(fondo, camino);
            int d = Height - 7;
            int x = Encendido ? Width - d - 4 : 3;
            using var bola = new SolidBrush(Color.White);
            g.FillEllipse(bola, x, 3, d, d);
        }
    }

    // Lista propia dibujada a mano: el ListView en vista informe solo sabe saltar de fila
    // en fila, así que el scroll suave por píxeles exige pintar y desplazar manualmente
    private sealed class ListaSuave : Control
    {
        private const int AltoFila = 46;

        private readonly MainForm _dueno;
        private readonly List<WindowInfo> _filas = new();
        private readonly System.Windows.Forms.Timer _animador = new() { Interval = 15 };
        private float _desplazamiento; // actual, en píxeles
        private float _objetivo;       // destino de la animación

        private int _seleccionada = -1;

        public event Action? SeleccionCambiada;
        public event Action? DobleClic;

        public ListaSuave(MainForm dueno)
        {
            _dueno = dueno;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            _animador.Tick += (_, _) => Animar();
        }

        public int Cuenta => _filas.Count;

        public WindowInfo? Seleccion =>
            _seleccionada >= 0 && _seleccionada < _filas.Count ? _filas[_seleccionada] : null;

        public void Cargar(IReadOnlyList<WindowInfo> ventanas)
        {
            IntPtr? previa = Seleccion?.Hwnd;
            _filas.Clear();
            _filas.AddRange(ventanas);
            _seleccionada = previa is { } h ? _filas.FindIndex(f => f.Hwnd == h) : -1;
            AjustarLimites();
            Invalidate();
        }

        private float MaximoScroll => Math.Max(0, _filas.Count * AltoFila - (Height - 8));

        private void AjustarLimites()
        {
            _objetivo = Math.Clamp(_objetivo, 0, MaximoScroll);
            _desplazamiento = Math.Clamp(_desplazamiento, 0, MaximoScroll);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            AjustarLimites();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _objetivo = Math.Clamp(_objetivo - e.Delta * 92f / 120f, 0, MaximoScroll);
            _animador.Start();
        }

        // Interpolación exponencial hacia el objetivo: rápida al inicio, se posa con suavidad
        private void Animar()
        {
            float distancia = _objetivo - _desplazamiento;
            if (Math.Abs(distancia) < 0.5f)
            {
                _desplazamiento = _objetivo;
                _animador.Stop();
            }
            else
            {
                _desplazamiento += distancia * 0.25f;
            }
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int fila = (int)((e.Y - 4 + _desplazamiento) / AltoFila);
            _seleccionada = fila >= 0 && fila < _filas.Count ? fila : -1;
            Invalidate();
            SeleccionCambiada?.Invoke();
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            DobleClic?.Invoke();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(FondoEfectivo(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // La tarjeta flotante estilo LS: redondeada, sin borde, un punto más clara que el fondo
            var tarjeta = new Rectangle(0, 0, Width - 1, Height - 1);
            using var camino = CaminoRedondeado(tarjeta, _dueno._config.BordesRedondeados ? 8 : 0);
            using var brocha = new SolidBrush(BackColor);
            g.FillPath(brocha, camino);
            if (_filas.Count == 0) return;
            g.SetClip(camino);
            int primera = Math.Max(0, (int)(_desplazamiento / AltoFila));
            int ultima = Math.Min(_filas.Count - 1, (int)((_desplazamiento + Height) / AltoFila) + 1);
            for (int i = primera; i <= ultima; i++)
            {
                int y = (int)(i * AltoFila - _desplazamiento) + 4;
                _dueno.DibujarFila(g, _filas[i], new Rectangle(0, y, Width, AltoFila), i == _seleccionada);
            }
            g.ResetClip();
        }
    }
}
