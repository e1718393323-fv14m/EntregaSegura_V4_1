using EntregaSegura.Models;
using EntregaSegura.Services;

namespace EntregaSegura.Views;

// Se abre después del login cuando el administrador asignó una contraseña temporal
public partial class vCambiarClave : ContentPage
{
    public vCambiarClave()
    {
        InitializeComponent();
        ActualizarReglas();
    }

    private void Clave_TextChanged(object sender, TextChangedEventArgs e) => ActualizarReglas();

    // Reglas visibles que se marcan mientras el usuario escribe
    private void ActualizarReglas()
    {
        string clave = txtNueva.Text ?? "";
        bool largo = clave.Length >= 8;
        bool letra = clave.Any(char.IsLetter);
        bool numero = clave.Any(char.IsDigit);
        bool iguales = clave != "" && clave == (txtRepetir.Text ?? "");

        lblReglas.Text =
            $"{M(largo)} Al menos 8 caracteres\n" +
            $"{M(letra)} Contiene letras\n" +
            $"{M(numero)} Contiene números\n" +
            $"{M(iguales)} Las dos contraseñas coinciden";

        btnGuardar.IsEnabled = largo && letra && numero && iguales;
    }

    private static string M(bool ok) => ok ? "✔" : "○";

    private async void btnGuardar_Clicked(object sender, EventArgs e)
    {
        try
        {
            btnGuardar.IsEnabled = false;
            indicador.IsRunning = true;

            string nueva = txtNueva.Text!;
            await SupabaseService.CambiarMiClave(nueva);
            Sesion.Conductor!.debe_cambiar_clave = false;

            // Con la clave nueva ya puede entrar también sin internet
            await OfflineService.RecordarSesionAsync(Sesion.Correo, nueva);

            await DisplayAlert("Listo", "Su contraseña se actualizó correctamente.", "Continuar");

            Page menu = Sesion.Conductor.EsAdministrador ? new vAdministrador()
                      : Sesion.Conductor.EsSupervisor ? new vSupervisor()
                      : new vPrincipal();
            Navigation.InsertPageBefore(menu, this);
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("ERROR", ex.Message, "Cerrar");
            ActualizarReglas();
        }
        finally
        {
            indicador.IsRunning = false;
        }
    }

    private async void btnSalir_Clicked(object sender, EventArgs e)
    {
        Sesion.Cerrar();
        await Navigation.PopToRootAsync();
    }
}
