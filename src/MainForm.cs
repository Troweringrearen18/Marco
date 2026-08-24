using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SinBordes;

public sealed class MainForm : Form
{
    private readonly ListaSuave _lista;
    private readonly ToolStripStatusLabel _estado;
    private readonly StatusStrip _barra;
    private readonly FlowLayoutPanel _botones;
    private readonly Panel _barraTitulo;
    private readonly Label _titulo;
    private readonly Button _maximizar;
    private readonly BotonRedondeado _botonAccion;
    private readonly BotonRedondeado _botonFavorito;
    private readonly BotonRedondeado _botonActualizar;
    private readonly BotonRedondeado _botonAjustes;
    private readonly Panel _marcoLista;
    private readonly NotifyIcon _bandeja;
    private readonly System.Windows.Forms.Timer _watcher;
    private readonly HashSet<long> _watcherFallidas = new();
    // Deshechas a mano por el usuario: el watcher las respeta mientras viva la ventana;
    // al relanzar el juego (hwnd nuevo) vuelve el borderless automático
    private readonly HashSet<long> _deshechasManualmente = new();
    private readonly Config _config;
    private long _huellaVentanas;
    private bool _iniciarOculto;
    private bool _globoBandeja;
    private bool _salir;

    private Color _fondo, _texto, _textoSuave, _panel, _textoPanel, _seleccion, _acento;
    private Font _fuenteNegrita = null!, _fuentePequena = null!;

    public MainForm()
    {
        _config = ConfigStore.Cargar();
        Textos.Idioma = string.IsNullOrEmpty(_config.Idioma) ? "en" : _config.Idioma;
        _iniciarOculto = _config.ArrancarMinimizado;

        Text = "SinBordes";
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
            _config.VentanaAncho = tam.Width;
            _config.VentanaAlto = tam.Height;
            ConfigStore.Guardar(_config);
        };
        // Anillo de 6px del propio Form alrededor de los hijos: zona de agarre del redimensionado
        Padding = new Padding(6);
        Font = new Font("Segoe UI", 9.75f);

        // Barra de título propia, al estilo Lossless Scaling
        _barraTitulo = new Panel { Dock = DockStyle.Top, Height = 42 };
        _titulo = new Label
        {
            Text = "SinBordes",
            AutoSize = true,
            Location = new Point(14, 11),
            Font = new Font("Segoe UI Semibold", 10.5f),
        };
        _maximizar = BotonTitulo("\uE922", AlternarMaximizado); // maximizar/restaurar (Segoe MDL2)
        var cerrar = BotonTitulo("\uE8BB", Close);
        var minimizar = BotonTitulo("\uE921", OcultarABandeja); // minimizar = irse a la bandeja
        _barraTitulo.Controls.Add(_titulo);
        _barraTitulo.Controls.Add(cerrar);
        _barraTitulo.Controls.Add(_maximizar);
        _barraTitulo.Controls.Add(minimizar);
        _barraTitulo.Resize += (_, _) =>
        {
            cerrar.Location = new Point(_barraTitulo.Width - cerrar.Width - 6, 5);
            _maximizar.Location = new Point(cerrar.Left - _maximizar.Width - 2, 5);
            minimizar.Location = new Point(_maximizar.Left - minimizar.Width - 2, 5);
        };
        _barraTitulo.MouseDown += ArrastrarVentana;
        _titulo.MouseDown += ArrastrarVentana;

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
        _botonActualizar = Boton(Textos.T("boton.actualizar"), Refrescar);
        _botonAjustes = Boton(Textos.T("boton.ajustes"), MenuAjustes);
        _botones.Controls.Add(_botonAccion);
        _botones.Controls.Add(_botonFavorito);
        _botones.Controls.Add(_botonActualizar);
        _botones.Controls.Add(_botonAjustes);

        _barra = new StatusStrip { SizingGrip = false };
        _estado = new ToolStripStatusLabel(Textos.T("estado.listo"));
        _barra.Items.Add(_estado);

        Icon = CrearIcono();
        _bandeja = new NotifyIcon
        {
            Icon = Icon,
            Text = "SinBordes",
            Visible = true,
        };
        _bandeja.DoubleClick += (_, _) => MostrarDesdeBandeja();
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
        };

        // Estilo LS: sin líneas separadoras — la tarjeta redondeada la pinta ListaSuave
        _marcoLista = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4) };
        _marcoLista.Controls.Add(_lista);

        Controls.Add(_marcoLista);
        Controls.Add(_botones);
        Controls.Add(_barra);
        Controls.Add(_barraTitulo);
        _marcoLista.BringToFront();

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

        // Hotkey global Ctrl+Alt+B: borderless/deshacer sobre la ventana activa
        NativeMethods.RegisterHotKey(Handle, 1,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, 0x42 /* B */);
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
        Refrescar(); // botones contextuales, barra de estado y filas
    }

    private void OcultarABandeja()
    {
        Hide();
        if (_globoBandeja) return;
        _globoBandeja = true;
        _bandeja.BalloonTipTitle = "SinBordes";
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
        if (_config.Favoritos.Count > 0)
        {
            foreach (var v in ventanas)
            {
                if (!EsFavorito(v.ProcessName) || v.Elevada) continue;
                long clave = v.Hwnd.ToInt64();
                if (_watcherFallidas.Contains(clave) || _deshechasManualmente.Contains(clave)) continue;
                bool aplicada = BorderlessService.TieneEstadoGuardado(v.Hwnd);
                bool conBorde = (NativeMethods.GetWindowLongPtr(v.Hwnd, NativeMethods.GWL_STYLE)
                                 & NativeMethods.WS_CAPTION) != 0;
                // Ventana favorita nueva, o el juego re-impuso su borde al cambiar de escena
                if (!aplicada || conBorde)
                {
                    if (BorderlessService.Aplicar(v.Hwnd, out _)) cambio = true;
                    else _watcherFallidas.Add(clave); // no insistir cada 2 s contra un fallo persistente
                }
            }
            _watcherFallidas.RemoveWhere(h => !NativeMethods.IsWindow(new IntPtr(h)));
            _deshechasManualmente.RemoveWhere(h => !NativeMethods.IsWindow(new IntPtr(h)));
        }

        long huella = ventanas.Count;
        foreach (var v in ventanas) huella = unchecked(huella * 31 + v.Hwnd.ToInt64());
        if (cambio || huella != _huellaVentanas)
        {
            _huellaVentanas = huella;
            if (Visible) RefrescarCon(ventanas);
        }
    }

    private bool EsFavorito(string proceso) =>
        _config.Favoritos.Any(f => f.Equals(proceso, StringComparison.OrdinalIgnoreCase));

    private void AlternarFavorito()
    {
        if (_lista.Seleccion is not { } v)
        {
            _estado.Text = Textos.T("estado.selecciona");
            return;
        }
        if (EsFavorito(v.ProcessName))
        {
            _config.Favoritos.RemoveAll(f => f.Equals(v.ProcessName, StringComparison.OrdinalIgnoreCase));
            _estado.Text = Textos.F("estado.fueraBiblio", v.ProcessName);
        }
        else
        {
            _config.Favoritos.Add(v.ProcessName);
            _estado.Text = Textos.F("estado.enBiblio", v.ProcessName);
        }
        ConfigStore.Guardar(_config);
        Refrescar();
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
        _bandeja.BalloonTipTitle = "SinBordes";
        _bandeja.BalloonTipText = exito
            ? (aplicada ? Textos.T("bandeja.restaurada") : Textos.T("bandeja.sinbordes"))
            : error;
        _bandeja.ShowBalloonTip(1200);
        if (Visible) Refrescar();
    }

    private void EnviarAltIntro()
    {
        if (_lista.Seleccion is not { } v || v.Hwnd == IntPtr.Zero)
        {
            _estado.Text = Textos.T("estado.seleccionaViva");
            return;
        }
        NativeMethods.SetForegroundWindow(v.Hwnd);
        Thread.Sleep(250); // dar tiempo al cambio de foco antes de teclear
        var pulsos = new[]
        {
            Tecla(NativeMethods.VK_MENU, soltar: false),
            Tecla(NativeMethods.VK_RETURN, soltar: false),
            Tecla(NativeMethods.VK_RETURN, soltar: true),
            Tecla(NativeMethods.VK_MENU, soltar: true),
        };
        NativeMethods.SendInput((uint)pulsos.Length, pulsos, Marshal.SizeOf<NativeMethods.INPUT>());
        _estado.Text = Textos.F("estado.altintro", v.Title);
    }

    private static NativeMethods.INPUT Tecla(ushort vk, bool soltar) => new()
    {
        Type = NativeMethods.INPUT_KEYBOARD,
        Ki = new NativeMethods.KEYBDINPUT { Vk = vk, Flags = soltar ? NativeMethods.KEYEVENTF_KEYUP : 0 },
    };

    private void ConfigurarAutoarranque(bool activar)
    {
        const string clave = @"Software\Microsoft\Windows\CurrentVersion\Run";
        try
        {
            using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(clave, writable: true);
            if (activar)
                run?.SetValue("SinBordes", $"\"{Application.ExecutablePath}\"");
            else
                run?.DeleteValue("SinBordes", throwOnMissingValue: false);
        }
        catch
        {
            _estado.Text = Textos.T("estado.autoarranqueError");
        }
    }

    private static Icon CrearIcono()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var fondo = new SolidBrush(Color.FromArgb(233, 30, 99));
            using var camino = CaminoRedondeado(new Rectangle(1, 1, 29, 29), 8);
            g.FillPath(fondo, camino);
            using var pluma = new Pen(Color.White, 3f);
            g.DrawRectangle(pluma, 9, 9, 13, 13);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    // Sin marco del sistema no hay bordes de agarre: se reconstruyen sobre el anillo de Padding
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam == (IntPtr)1)
            AlternarVentanaActiva();
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
        _barra.BackColor = _fondo;
        _estado.ForeColor = _textoSuave;

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

        Invalidate(true);
        _lista.Invalidate();
    }

    private static Color Mezclar(Color origen, Color destino, float peso) => Color.FromArgb(
        origen.R + (int)((destino.R - origen.R) * peso),
        origen.G + (int)((destino.G - origen.G) * peso),
        origen.B + (int)((destino.B - origen.B) * peso));

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

    private void MenuAjustes()
    {
        var menu = new ContextMenuStrip();

        var colores = new ToolStripMenuItem(Textos.T("menu.colores"));
        colores.DropDownItems.Add(Textos.T("menu.fondo"), null, (_, _) => ElegirColor(c => _config.ColorFondo = c, _fondo));
        colores.DropDownItems.Add(Textos.T("menu.panel"), null, (_, _) => ElegirColor(c => _config.ColorPanel = c, _panel));
        colores.DropDownItems.Add(Textos.T("menu.acento"), null, (_, _) => ElegirColor(c => _config.ColorAcento = c, _acento));
        colores.DropDownItems.Add(Textos.T("menu.texto"), null, (_, _) => ElegirColor(c => _config.ColorTexto = c, _texto));
        colores.DropDownItems.Add(new ToolStripSeparator());
        colores.DropDownItems.Add(Textos.T("menu.restablecer"), null, (_, _) =>
        {
            var defecto = new Config();
            _config.ColorFondo = defecto.ColorFondo;
            _config.ColorPanel = defecto.ColorPanel;
            _config.ColorAcento = defecto.ColorAcento;
            _config.ColorTexto = defecto.ColorTexto;
            ConfigStore.Guardar(_config);
            AplicarTema();
            _estado.Text = Textos.T("estado.coloresDefecto");
        });
        menu.Items.Add(colores);

        var idioma = new ToolStripMenuItem(Textos.T("menu.idioma"));
        foreach (var (codigo, nombre) in Textos.Disponibles)
        {
            var opcion = new ToolStripMenuItem(nombre) { Checked = Textos.Idioma == codigo };
            string elegido = codigo;
            opcion.Click += (_, _) => CambiarIdioma(elegido);
            idioma.DropDownItems.Add(opcion);
        }
        menu.Items.Add(idioma);

        var redondeo = new ToolStripMenuItem(Textos.T("menu.esquinas"))
        {
            Checked = _config.BordesRedondeados,
            CheckOnClick = true,
        };
        redondeo.CheckedChanged += (_, _) =>
        {
            _config.BordesRedondeados = redondeo.Checked;
            ConfigStore.Guardar(_config);
            AplicarTema();
        };
        menu.Items.Add(redondeo);

        menu.Items.Add(Alternador(Textos.T("menu.autoarranque"), _config.IniciarConWindows, valor =>
        {
            _config.IniciarConWindows = valor;
            ConfigurarAutoarranque(valor);
        }));
        menu.Items.Add(Alternador(Textos.T("menu.arrancarMin"), _config.ArrancarMinimizado,
            valor => _config.ArrancarMinimizado = valor));
        menu.Items.Add(Alternador(Textos.T("menu.cerrarMin"), _config.CerrarMinimiza,
            valor => _config.CerrarMinimiza = valor));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Textos.T("menu.altintro"), null, (_, _) => EnviarAltIntro());
        menu.Items.Add(new ToolStripMenuItem(Textos.T("menu.hotkey")) { Enabled = false });

        menu.Show(Cursor.Position);
    }

    private ToolStripMenuItem Alternador(string texto, bool valor, Action<bool> asignar)
    {
        var opcion = new ToolStripMenuItem(texto) { Checked = valor, CheckOnClick = true };
        opcion.CheckedChanged += (_, _) =>
        {
            asignar(opcion.Checked);
            ConfigStore.Guardar(_config);
        };
        return opcion;
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
        foreach (string favorito in _config.Favoritos)
            if (!ventanas.Any(v => v.ProcessName.Equals(favorito, StringComparison.OrdinalIgnoreCase)))
                visibles.Add(new WindowInfo(IntPtr.Zero, Textos.T("fila.apagada"), favorito, 0));
        _lista.Cargar(visibles);
        _estado.Text = _config.Favoritos.Count > 0
            ? Textos.F("estado.ventanasBiblio", ventanas.Count, _config.Favoritos.Count)
            : Textos.F("estado.ventanas", ventanas.Count);
        ActualizarBotonAccion();
    }

    // Los botones son contextuales según la fila seleccionada
    private void ActualizarBotonAccion()
    {
        var v = _lista.Seleccion;
        _botonAccion.Text = v is not null && v.Hwnd != IntPtr.Zero && BorderlessService.TieneEstadoGuardado(v.Hwnd)
            ? Textos.T("boton.deshacer")
            : Textos.T("boton.aplicar");
        _botonFavorito.Text = v is not null && EsFavorito(v.ProcessName)
            ? Textos.T("boton.quitar")
            : Textos.T("boton.guardar");
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
            g.Clear(Parent?.BackColor ?? BackColor);
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
            g.Clear(Parent?.BackColor ?? BackColor);
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
