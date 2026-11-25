using EntregaSegura.Models;
using EntregaSegura.Services;

namespace EntregaSegura.Views;

public partial class vPrincipal : ContentPage
{
    public vPrincipal()
    {
        InitializeComponent();
        lblSaludo.Text = "Hola, " + Sesion.Nombre;
        string fecha = DateTime.Now.ToString("dddd, dd 'de' MMMM", new System.Globalization.CultureInfo("es-EC"));
        lblFecha.Text = char.ToUpper(fecha[0]) + fecha[1..];

        var vehiculo = Sesion.Conductor?.vehiculo;
        lblVehiculo.Text = vehiculo == null ? "Sin vehículo asignado" : $"{vehiculo.modelo} · {vehiculo.placa}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        OfflineService.EstadoCambiado += AlCambiarEstado;
        ActualizarConexion();
        await CargarResumen();
    }

    private async Task CargarResumen()
    {
        try
        {
            var pendientes = await SupabaseService.ObtenerOrdenes("PENDIENTE");
            var realizadas = await SupabaseService.ObtenerOrdenes("REALIZADAS");
            lblPendientes.Text = pendientes.Count.ToString();
            lblRealizadas.Text = realizadas.Count.ToString();

            int total = pendientes.Count + realizadas.Count;
            double avance = total == 0 ? 0 : (double)realizadas.Count / total;
            await barAvance.ProgressTo(avance, 600, Easing.CubicOut);
            lblAvance.Text = $"{avance * 100:F0} %";
        }
        catch (Exception ex)
        {
            await DisplayAlert("ERROR", "No se pudo cargar el resumen: " + ex.Message, "Cerrar");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        OfflineService.EstadoCambiado -= AlCambiarEstado;
    }

    private void AlCambiarEstado() => MainThread.BeginInvokeOnMainThread(ActualizarConexion);

    // ===== MODO OFFLINE: muestra si hay internet y cuántas operaciones esperan envío =====
    private void ActualizarConexion()
    {
        bool internet = OfflineService.HayInternet;
        int pendientes = OfflineService.CantidadPendientes;

        brdConexion.IsVisible = !internet || pendientes > 0 || Sesion.ModoOffline;
        lblConexion.Text = internet ? "Conectado" : "Sin conexión · trabajando offline";
        lblConexion.TextColor = internet ? Color.FromArgb("#1E7B34") : Color.FromArgb("#8A4B00");
        brdConexion.BackgroundColor = internet ? Color.FromArgb("#E8EEF8") : Color.FromArgb("#FFF1DF");
        lblPendientesSync.Text = pendientes == 0
            ? "Todo está enviado"
            : $"{pendientes} operación(es) guardada(s) en el teléfono por enviar";
        btnSincronizar.IsVisible = internet && pendientes > 0;
    }

    private async void btnSincronizar_Clicked(object sender, EventArgs e)
    {
        try
        {
            btnSincronizar.IsEnabled = false;
            var (enviadas, restantes) = await OfflineService.SincronizarAsync();
            string detalle = restantes == 0
                ? $"Se enviaron {enviadas} operación(es). Todo está sincronizado."
                : $"Se enviaron {enviadas}. Quedan {restantes}: {OfflineService.Pendientes().FirstOrDefault()?.UltimoError}";
            await DisplayAlert("Sincronización", detalle, "Aceptar");
            ActualizarConexion();
            await CargarResumen();
        }
        catch (Exception ex)
        {
            await DisplayAlert("ERROR", ex.Message, "Cerrar");
        }
        finally
        {
            btnSincronizar.IsEnabled = true;
        }
    }

    private async void btnEntregas_Clicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new vEntregas());
    }

    private async void btnHistorial_Clicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new vHistorial());
    }

    private async void btnPerfil_Clicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new vPerfil());
    }

    private async void btnSalir_Clicked(object sender, EventArgs e)
    {
        if (OfflineService.CantidadPendientes > 0)
        {
            await DisplayAlert("Operaciones pendientes",
                $"Tiene {OfflineService.CantidadPendientes} operación(es) sin enviar. Quedarán guardadas en este teléfono y se enviarán cuando vuelva a ingresar con internet.",
                "Entendido");
        }
        bool salir = await DisplayAlert("Cerrar sesión", "¿Desea salir de la aplicación?", "Sí", "No");
        if (salir)
        {
            Sesion.Cerrar();
            await Navigation.PopToRootAsync();  
        }
    }
}
