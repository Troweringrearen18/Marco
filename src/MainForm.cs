namespace SinBordes;

public sealed class MainForm : Form
{
    private readonly ListView _lista;
    private readonly ToolStripStatusLabel _estado;
    private readonly StatusStrip _barra;
    private readonly FlowLayoutPanel _botones;
    private readonly Config _config;

    private Color _fondo, _texto, _panel, _acento;

    public MainForm()
    {
        _config = ConfigStore.Cargar();

        Text = "SinBordes";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 540);
        MinimumSize = new Size(400, 420);
        Font = new Font("Segoe UI", 9.75f);

        _lista = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            BorderStyle = BorderStyle.None,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            OwnerDraw = true,
        };
        _lista.Columns.Add("Proceso", 120);
        _lista.Columns.Add("Ventana", 230);
        _lista.Columns.Add("Estado", 80);
        _lista.DoubleClick += (_, _) => AplicarSeleccion();
        _lista.DrawColumnHeader += DibujarCabecera;
        _lista.DrawItem += (_, e) => e.DrawDefault = true;
        _lista.DrawSubItem += (_, e) => e.DrawDefault = true;

        _botones = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(8),
        };
        _botones.Controls.Add(Boton("Hacer borderless", AplicarSeleccion, destacado: true));
        _botones.Controls.Add(Boton("Restaurar", RestaurarSeleccion));
        _botones.Controls.Add(Boton("Actualizar", Refrescar));
        _botones.Controls.Add(Boton("Fondo…", ElegirFondo));
        _botones.Controls.Add(Boton("Acento…", ElegirAcento));

        _barra = new StatusStrip { SizingGrip = false };
        _estado = new ToolStripStatusLabel("Listo.");
        _barra.Items.Add(_estado);

        Controls.Add(_lista);
        Controls.Add(_botones);
        Controls.Add(_barra);
        _lista.BringToFront();

        AplicarTema();
        Load += (_, _) => Refrescar();
    }

    private static Button Boton(string texto, Action accion, bool destacado = false)
    {
        var boton = new Button
        {
            Text = texto,
            AutoSize = true,
            Padding = new Padding(10, 5, 10, 5),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Tag = destacado,
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
        _texto = oscuro ? Color.FromArgb(232, 236, 242) : Color.FromArgb(28, 31, 36);
        _panel = Mezclar(_fondo, oscuro ? Color.White : Color.Black, oscuro ? 0.08f : 0.05f);

        BackColor = _fondo;
        _lista.BackColor = Mezclar(_fondo, oscuro ? Color.White : Color.Black, 0.04f);
        _lista.ForeColor = _texto;
        _botones.BackColor = _fondo;
        _barra.BackColor = _panel;
        _estado.ForeColor = _texto;

        foreach (var boton in _botones.Controls.OfType<Button>())
        {
            bool destacado = boton.Tag is true;
            boton.BackColor = destacado ? _acento : _panel;
            boton.ForeColor = destacado
                ? (_acento.GetBrightness() < 0.55f ? Color.White : Color.FromArgb(28, 31, 36))
                : _texto;
            boton.FlatAppearance.MouseOverBackColor =
                Mezclar(boton.BackColor, oscuro ? Color.White : Color.Black, 0.12f);
        }

        if (IsHandleCreated) PintarBarraTitulo();
        _lista.Invalidate();
    }

    private static Color Mezclar(Color origen, Color destino, float peso) => Color.FromArgb(
        origen.R + (int)((destino.R - origen.R) * peso),
        origen.G + (int)((destino.G - origen.G) * peso),
        origen.B + (int)((destino.B - origen.B) * peso));

    private void DibujarCabecera(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var fondo = new SolidBrush(_panel);
        e.Graphics.FillRectangle(fondo, e.Bounds);
        var zona = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.Header!.Text, Font, zona, _texto,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    // La barra de título no obedece a BackColor: hay que pedirle a DWM el modo oscuro
    private void PintarBarraTitulo()
    {
        int inmersivo = _fondo.GetBrightness() < 0.5f ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(Handle,
            NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref inmersivo, sizeof(int));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        PintarBarraTitulo();
    }

    private void ElegirFondo()
    {
        using var dialogo = new ColorDialog { Color = _fondo, FullOpen = true };
        if (dialogo.ShowDialog(this) != DialogResult.OK) return;
        _config.ColorFondo = ColorTranslator.ToHtml(dialogo.Color);
        ConfigStore.Guardar(_config);
        AplicarTema();
        _estado.Text = "Color de fondo guardado.";
    }

    private void ElegirAcento()
    {
        using var dialogo = new ColorDialog { Color = _acento, FullOpen = true };
        if (dialogo.ShowDialog(this) != DialogResult.OK) return;
        _config.ColorAcento = ColorTranslator.ToHtml(dialogo.Color);
        ConfigStore.Guardar(_config);
        AplicarTema();
        _estado.Text = "Color de acento guardado.";
    }

    private WindowInfo? Seleccion =>
        _lista.SelectedItems.Count > 0 ? (WindowInfo)_lista.SelectedItems[0].Tag! : null;

    private void Refrescar()
    {
        IntPtr? previa = Seleccion?.Hwnd;
        _lista.BeginUpdate();
        _lista.Items.Clear();
        foreach (var v in WindowEnumerator.Listar())
        {
            var item = new ListViewItem(new[]
            {
                v.ProcessName,
                v.Title,
                BorderlessService.TieneEstadoGuardado(v.Hwnd) ? "sin bordes" : "",
            })
            { Tag = v };
            if (v.Hwnd == previa) item.Selected = true;
            _lista.Items.Add(item);
        }
        _lista.EndUpdate();
        _estado.Text = $"{_lista.Items.Count} ventanas.";
    }

    private void AplicarSeleccion()
    {
        if (Seleccion is not { } v)
        {
            _estado.Text = "Selecciona una ventana de la lista.";
            return;
        }
        _estado.Text = BorderlessService.Aplicar(v.Hwnd, out string error)
            ? $"«{v.Title}» ({v.ProcessName}) ahora está sin bordes."
            : error;
        Refrescar();
    }

    private void RestaurarSeleccion()
    {
        if (Seleccion is not { } v)
        {
            _estado.Text = "Selecciona una ventana de la lista.";
            return;
        }
        _estado.Text = BorderlessService.Restaurar(v.Hwnd, out string error)
            ? $"«{v.Title}» ({v.ProcessName}) restaurada."
            : error;
        Refrescar();
    }
}
