using EntregaSegura.Models;
using EntregaSegura.Services;

namespace EntregaSegura.Views;

public partial class vLogin : ContentPage
{
    public vLogin()
    {
        InitializeComponent();

        txtCorreo.Text = Preferences.Get("correo", "");
        chkRecordar.IsChecked = txtCorreo.Text != "";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ActualizarAvisoConexion();
        OfflineService.EstadoCambiado += ActualizarAvisoConexion;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        OfflineService.EstadoCambiado -= ActualizarAvisoConexion;
    }

    private void ActualizarAvisoConexion() =>
        MainThread.BeginInvokeOnMainThread(() => brdSinConexion.IsVisible = !OfflineService.HayInternet);

    private void chkMostrar_CheckedChanged(object sender, CheckedChangedEventArgs e)
    {
        txtContrasena.IsPassword = !e.Value;
    }

    // Presionar "Enter" en la contraseña también inicia sesión
    private void txtContrasena_Completed(object sender, EventArgs e) => btnIngresar_Clicked(sender, e);

    private async void btnIngresar_Clicked(object sender, EventArgs e)
    {
        try
        {
            string correo = txtCorreo.Text?.Trim().ToLower() ?? "";
            string contrasena = txtContrasena.Text ?? "";

            if (correo == "" || contrasena == "")
            {
                await DisplayAlert("Aviso", "Ingrese su correo y su contraseña", "Aceptar");
                return;
            }
            if (!correo.Contains('@'))
            {
                await DisplayAlert("Aviso", "Ingrese un correo válido", "Aceptar");
                return;
            }

            btnIngresar.IsEnabled = false;
            indicador.IsRunning = true;

            // ===== SIN INTERNET: entrar con los datos guardados en el teléfono =====
            if (!OfflineService.HayInternet)
            {
                if (await OfflineService.IniciarSesionOfflineAsync(correo, contrasena))
                {
                    await DisplayAlert("Modo sin conexión",
                        "Está trabajando sin internet. Verá las órdenes descargadas la última vez y sus entregas se enviarán solas cuando vuelva la señal.",
                        "Entendido");
                    await AbrirMenu();
                }
                else
                {
                    await DisplayAlert("Sin conexión",
                        "No hay internet y este usuario no ha iniciado sesión antes en este teléfono (o la contraseña no coincide). Conéctese para ingresar.",
                        "Cerrar");
                }
                return;
            }

            // ===== CON INTERNET =====
            bool correcto = await SupabaseService.IniciarSesion(correo, contrasena);
            if (!correcto)
            {
                await DisplayAlert("ERROR", "Usuario/Contraseña incorrectos", "Cerrar");
                return;
            }

            Sesion.Conductor = await SupabaseService.ObtenerPerfil();
            if (Sesion.Conductor == null)
            {
                await DisplayAlert("Aviso", "Su usuario no está registrado en la tabla conductores", "Cerrar");
                Sesion.Cerrar();
                return;
            }

            Preferences.Set("correo", chkRecordar.IsChecked ? correo : "");
            txtContrasena.Text = "";

            // Si el administrador le asignó una clave temporal, debe cambiarla antes de continuar
            if (Sesion.Conductor.debe_cambiar_clave)
            {
                OfflineService.OlvidarSesion(correo);   // la clave temporal no sirve para entrar sin internet
                await Navigation.PushAsync(new vCambiarClave());
                return;
            }

            // Guardamos lo necesario para poder entrar sin internet la próxima vez
            await OfflineService.RecordarSesionAsync(correo, contrasena);

            // Envía lo que haya quedado pendiente de una jornada sin conexión
            var (enviadas, _) = await OfflineService.SincronizarAsync();
            if (enviadas > 0)
                await DisplayAlert("Sincronización", $"Se enviaron {enviadas} operación(es) guardadas sin conexión.", "Aceptar");

            await AbrirMenu();
        }
        catch (Exception ex)
        {
            await DisplayAlert("ERROR", "No se pudo conectar: " + ex.Message, "Cerrar");
        }
        finally
        {
            btnIngresar.IsEnabled = true;
            indicador.IsRunning = false;
        }
    }

    // Abre el menú según el rol
    private async Task AbrirMenu()
    {
        txtContrasena.Text = "";
        if (Sesion.Conductor!.EsAdministrador)
            await Navigation.PushAsync(new vAdministrador());
        else if (Sesion.Conductor.EsSupervisor)
            await Navigation.PushAsync(new vSupervisor());
        else
            await Navigation.PushAsync(new vPrincipal());
    }

    // ===== OLVIDÉ MI CONTRASEÑA =====
    // Registra una solicitud que el administrador ve en su panel.
    private async void btnOlvide_Clicked(object sender, EventArgs e)
    {
        try
        {
            if (!OfflineService.HayInternet)
            {
                await DisplayAlert("Sin conexión", "Necesita internet para enviar la solicitud.", "Cerrar");
                return;
            }

            string? correo = await DisplayPromptAsync("Recuperar contraseña",
                "Escriba su correo. El administrador recibirá la solicitud y le entregará una contraseña temporal.",
                "Enviar", "Cancelar", "nombre@entregasegura.com", maxLength: 80,
                keyboard: Keyboard.Email, initialValue: txtCorreo.Text ?? "");

            if (string.IsNullOrWhiteSpace(correo)) return;
            if (!correo.Contains('@'))
            {
                await DisplayAlert("Aviso", "Ingrese un correo válido", "Aceptar");
                return;
            }

            await SupabaseService.SolicitarRecuperacion(correo);

            // Mismo mensaje exista o no el correo (no se revela qué usuarios existen)
            await DisplayAlert("Solicitud enviada",
                "Si el correo está registrado, el administrador verá su solicitud y le entregará una contraseña temporal. " +
                "Al ingresar con ella, la aplicación le pedirá crear una nueva.",
                "Aceptar");
        }
        catch (Exception ex)
        {
            await DisplayAlert("ERROR", "No se pudo enviar la solicitud: " + ex.Message, "Cerrar");
        }
    }
}
