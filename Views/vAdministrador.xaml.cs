using System.Security.Cryptography;
using EntregaSegura.Models;
using EntregaSegura.Services;

namespace EntregaSegura.Views;

// ADMINISTRADOR: atiende las solicitudes de "Olvidé mi contraseña" y asigna contraseñas temporales
public partial class vAdministrador : ContentPage
{
    public vAdministrador()
    {
        InitializeComponent();
        lblSaludo.Text = "Hola, " + Sesion.Nombre;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Cargar();
    }

    private async Task Cargar()
    {
        try
        {
            refrescar.IsRefreshing = true;
            if (!OfflineService.HayInternet)
            {
                await DisplayAlert("Sin conexión", "La administración de usuarios requiere internet.", "Cerrar");
                return;
            }

            lstUsuarios.ItemsSource = await SupabaseService.ObtenerUsuarios();
            MostrarSolicitudes(await SupabaseService.ObtenerSolicitudes());
        }
        catch (Exception ex)
        {
            await DisplayAlert("ERROR", "No se pudo cargar la información: " + ex.Message, "Cerrar");
        }
        finally
        {
            refrescar.IsRefreshing = false;
        }
    }

    // Dibuja un botón por cada solicitud pendiente
    private void MostrarSolicitudes(List<SolicitudClave> solicitudes)
    {
        lstSolicitudes.Children.Clear();
        bool hay = solicitudes.Count > 0;
        lblSolicitudes.Text = hay ? $"Solicitudes de contraseña pendientes: {solicitudes.Count}" : "No hay solicitudes de contraseña pendientes";
        lblSolicitudes.TextColor = hay ? Color.FromArgb("#B3261E") : Color.FromArgb("#1E7B34");
        brdSolicitudes.BackgroundColor = hay ? Color.FromArgb("#FCE8E6") : Color.FromArgb("#E3F4E7");

        foreach (var s in solicitudes)
        {
            var boton = new Button
            {
                Text = $"{s.email}  ·  {s.FechaTexto}",
                Style = (Style)Application.Current!.Resources["BotonSecundario"],
                MinimumHeightRequest = 44,
                FontSize = 13
            };
            boton.Clicked += async (o, e) => await Restablecer(s.email);
            lstSolicitudes.Children.Add(boton);
        }
    }

    private async void refrescar_Refreshing(object sender, EventArgs e) => await Cargar();

    private async void lstUsuarios_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Conductor usuario) return;
        lstUsuarios.SelectedItem = null;

        if (usuario.email == Sesion.Correo)
        {
            await DisplayAlert("Aviso", "Para cambiar su propia contraseña use la opción «¿Olvidó su contraseña?» o pida a otro administrador.", "Aceptar");
            return;
        }
        await Restablecer(usuario.email);
    }

    // Asigna una contraseña temporal; el usuario deberá cambiarla al ingresar
    private async Task Restablecer(string correo)
    {
        try
        {
            string sugerida = GenerarClaveTemporal();
            string? clave = await DisplayPromptAsync("Contraseña temporal",
                $"Usuario: {correo}\n\nSe sugiere esta contraseña temporal (puede cambiarla). Al ingresar, el usuario deberá crear una nueva.",
                "Asignar", "Cancelar", "Mínimo 8 caracteres", maxLength: 30, initialValue: sugerida);

            if (string.IsNullOrWhiteSpace(clave)) return;
            if (clave.Length < 8)
            {
                await DisplayAlert("Aviso", "La contraseña temporal debe tener al menos 8 caracteres.", "Aceptar");
                return;
            }

            await SupabaseService.RestablecerClave(correo, clave.Trim());

            await DisplayAlert("Contraseña asignada",
                $"Entregue al usuario esta contraseña temporal por un medio seguro (en persona o por teléfono):\n\n{clave.Trim()}\n\n" +
                "Sus sesiones abiertas se cerraron y deberá crear una contraseña nueva al ingresar.",
                "Aceptar");
            await Cargar();
        }
        catch (Exception ex)
        {
            await DisplayAlert("ERROR", "No se pudo restablecer la contraseña: " + ex.Message, "Cerrar");
        }
    }

    // Ejemplo: "Temp-4821-kq" (letras y números, fácil de dictar)
    private static string GenerarClaveTemporal()
    {
        const string letras = "abcdefghjkmnpqrstuvwxyz";
        int numero = RandomNumberGenerator.GetInt32(1000, 10000);
        char a = letras[RandomNumberGenerator.GetInt32(letras.Length)];
        char b = letras[RandomNumberGenerator.GetInt32(letras.Length)];
        return $"Temp-{numero}-{a}{b}";
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
