using System.Drawing.Drawing2D;

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
    private readonly Panel _marcoLista;
    private readonly Config _config;

    private Color _fondo, _texto, _textoSuave, _panel, _textoPanel, _seleccion, _acento;
    private Font _fuenteNegrita = null!, _fuentePequena = null!;

    public MainForm()
    {
        _config = ConfigStore.Cargar();

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
        var minimizar = BotonTitulo("\uE921", () => WindowState = FormWindowState.Minimized);
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
        _botonAccion = Boton("Hacer borderless", AccionPrincipal);
        _botones.Controls.Add(_botonAccion);
        _botones.Controls.Add(Boton("Actualizar", Refrescar));
        _botones.Controls.Add(Boton("Ajustes…", MenuAjustes));

        _barra = new StatusStrip { SizingGrip = false };
        _estado = new ToolStripStatusLabel("Listo.");
        _barra.Items.Add(_estado);

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

    // Sin marco del sistema no hay bordes de agarre: se reconstruyen sobre el anillo de Padding
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
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

        bool aplicada = BorderlessService.TieneEstadoGuardado(v.Hwnd);
        int derecha = zona.Right - 10;
        if (aplicada)
        {
            var medida = TextRenderer.MeasureText("sin bordes", _fuentePequena);
            var pildora = new Rectangle(zona.Right - medida.Width - 22, zona.Y + (zona.Height - medida.Height - 8) / 2,
                medida.Width + 14, medida.Height + 8);
            using var fondoPildora = new SolidBrush(Color.FromArgb(45, _acento));
            using var caminoPildora = CaminoRedondeado(pildora, pildora.Height / 2);
            g.FillPath(fondoPildora, caminoPildora);
            TextRenderer.DrawText(g, "sin bordes", _fuentePequena, pildora,
                Mezclar(_acento, _texto, 0.35f),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            derecha = pildora.Left - 8;
        }

        var zonaTexto = new Rectangle(zona.X + 14, zona.Y + 4, derecha - zona.X - 14, zona.Height - 8);
        TextRenderer.DrawText(g, v.ProcessName, _fuenteNegrita,
            new Rectangle(zonaTexto.X, zonaTexto.Y, zonaTexto.Width, zonaTexto.Height / 2),
            _texto, TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, v.Title, _fuentePequena,
            new Rectangle(zonaTexto.X, zonaTexto.Y + zonaTexto.Height / 2, zonaTexto.Width, zonaTexto.Height / 2),
            _textoSuave, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
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

        var colores = new ToolStripMenuItem("Colores");
        colores.DropDownItems.Add("Fondo…", null, (_, _) => ElegirColor(c => _config.ColorFondo = c, _fondo));
        colores.DropDownItems.Add("Botones y barras…", null, (_, _) => ElegirColor(c => _config.ColorPanel = c, _panel));
        colores.DropDownItems.Add("Acento…", null, (_, _) => ElegirColor(c => _config.ColorAcento = c, _acento));
        colores.DropDownItems.Add("Texto…", null, (_, _) => ElegirColor(c => _config.ColorTexto = c, _texto));
        colores.DropDownItems.Add(new ToolStripSeparator());
        colores.DropDownItems.Add("Restablecer", null, (_, _) =>
        {
            var defecto = new Config();
            _config.ColorFondo = defecto.ColorFondo;
            _config.ColorPanel = defecto.ColorPanel;
            _config.ColorAcento = defecto.ColorAcento;
            _config.ColorTexto = defecto.ColorTexto;
            ConfigStore.Guardar(_config);
            AplicarTema();
            _estado.Text = "Colores por defecto.";
        });
        menu.Items.Add(colores);

        var redondeo = new ToolStripMenuItem("Esquinas redondeadas")
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

        menu.Show(Cursor.Position);
    }

    private void ElegirColor(Action<string> asignar, Color actual)
    {
        using var dialogo = new ColorDialog { Color = actual, FullOpen = true };
        if (dialogo.ShowDialog(this) != DialogResult.OK) return;
        asignar(ColorTranslator.ToHtml(dialogo.Color));
        ConfigStore.Guardar(_config);
        AplicarTema();
        _estado.Text = "Colores guardados.";
    }

    private void Refrescar()
    {
        _lista.Cargar(WindowEnumerator.Listar());
        _estado.Text = $"{_lista.Cuenta} ventanas.";
        ActualizarBotonAccion();
    }

    // El botón principal es contextual: aplica sobre ventanas normales, deshace sobre aplicadas
    private void ActualizarBotonAccion()
    {
        _botonAccion.Text = _lista.Seleccion is { } v && BorderlessService.TieneEstadoGuardado(v.Hwnd)
            ? "Deshacer"
            : "Hacer borderless";
    }

    private void AccionPrincipal()
    {
        if (_lista.Seleccion is not { } v)
        {
            _estado.Text = "Selecciona una ventana de la lista.";
            return;
        }
        bool aplicada = BorderlessService.TieneEstadoGuardado(v.Hwnd);
        string error;
        bool exito = aplicada
            ? BorderlessService.Restaurar(v.Hwnd, out error)
            : BorderlessService.Aplicar(v.Hwnd, out error);
        _estado.Text = exito
            ? (aplicada
                ? $"«{v.Title}» ({v.ProcessName}) restaurada."
                : $"«{v.Title}» ({v.ProcessName}) ahora está sin bordes.")
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
