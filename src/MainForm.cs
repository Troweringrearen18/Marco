namespace SinBordes;

public sealed class MainForm : Form
{
    private readonly ListView _lista;
    private readonly ToolStripStatusLabel _estado;

    public MainForm()
    {
        Text = "SinBordes";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(780, 500);
        MinimumSize = new Size(620, 400);

        _lista = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
        };
        _lista.Columns.Add("Proceso", 150);
        _lista.Columns.Add("Ventana", 440);
        _lista.Columns.Add("Estado", 110);
        _lista.DoubleClick += (_, _) => AplicarSeleccion();

        var botones = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(6),
        };
        botones.Controls.Add(Boton("Hacer borderless", AplicarSeleccion));
        botones.Controls.Add(Boton("Restaurar", RestaurarSeleccion));
        botones.Controls.Add(Boton("Actualizar lista", Refrescar));

        var barra = new StatusStrip();
        _estado = new ToolStripStatusLabel("Listo.");
        barra.Items.Add(_estado);

        Controls.Add(_lista);
        Controls.Add(botones);
        Controls.Add(barra);
        _lista.BringToFront();

        Load += (_, _) => Refrescar();
    }

    private static Button Boton(string texto, Action accion)
    {
        var boton = new Button { Text = texto, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        boton.Click += (_, _) => accion();
        return boton;
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
