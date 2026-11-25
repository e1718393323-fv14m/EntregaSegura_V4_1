using EntregaSegura.Models;
using EntregaSegura.Services;

namespace EntregaSegura.Views;
public partial class vSupervisor : ContentPage
{
    public vSupervisor()
    {
        InitializeComponent();
        lblSaludo.Text = "Hola, " + Sesion.Nombre;
        string fecha = DateTime.Now.ToString("dddd, dd 'de' MMMM", new System.Globalization.CultureInfo("es-EC"));
        lblFecha.Text = char.ToUpper(fecha[0]) + fecha[1..];
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var ordenes = await SupabaseService.ObtenerOrdenes("TODAS");
            var novedades = await SupabaseService.ObtenerNovedades();

            int total = ordenes.Count;
            int pendientes = ordenes.Count(o => o.estado == "PENDIENTE");
            int entregadas = ordenes.Count(o => o.estado == "ENTREGADA");
            int conNovedad = ordenes.Count(o => o.estado == "NOVEDAD");
            int porRevisar = novedades.Count(n => !n.revisada);

            lblTotal.Text = total.ToString();
            lblPendientes.Text = pendientes.ToString();
            lblEntregadas.Text = entregadas.ToString();
            lblNovedades.Text = conNovedad.ToString();
            lblPorRevisar.Text = porRevisar > 0
                ? $"Tiene {porRevisar} novedad(es) por revisar"
                : "Todas las novedades están revisadas";
            lblPorRevisar.TextColor = porRevisar > 0 ? Color.FromArgb("#B3261E") : Color.FromArgb("#1E7B34");
            brdPorRevisar.BackgroundColor = porRevisar > 0 ? Color.FromArgb("#FCE8E6") : Color.FromArgb("#E3F4E7");

            // Avance = órdenes atendidas / total
            double avance = total == 0 ? 0 : (double)(entregadas + conNovedad) / total;
            await barAvance.ProgressTo(avance, 600, Easing.CubicOut);
            lblAvance.Text = $"{avance * 100:F0} %";
        }
        catch (Exception ex)
        {
            await DisplayAlert("ERROR", "No se pudo cargar el resumen: " + ex.Message, "Cerrar");
        }
    }

    private async void btnOrdenes_Clicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new vTodasOrdenes());
    }

    private async void btnNovedades_Clicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new vNovedades());
    }

    private async void btnPerfil_Clicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new vPerfil());
    }

    private async void btnSalir_Clicked(object sender, EventArgs e)
    {
        bool salir = await DisplayAlert("Cerrar sesión", "¿Desea salir de la aplicación?", "Sí", "No");
        if (salir)
        {
            Sesion.Cerrar();
            await Navigation.PopToRootAsync();
        }
    }
}
