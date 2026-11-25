namespace EntregaSegura.Models
{
    public static class Sesion
    {
        public static string Token { get; set; } = "";
        public static string UsuarioId { get; set; } = "";
        public static string Correo { get; set; } = "";
        public static string RefreshToken { get; set; } = "";   // para renovar el token sin volver a pedir la clave

        // true cuando el usuario entró sin internet con los datos guardados en el teléfono
        public static bool ModoOffline { get; set; }

        public static Conductor? Conductor { get; set; }

        public static string Nombre => Conductor?.nombres ?? (Correo.Contains('@') ? Correo.Split('@')[0] : Correo);

        public static void Cerrar()
        {
            Token = "";
            UsuarioId = "";
            Correo = "";
            RefreshToken = "";
            ModoOffline = false;
            Conductor = null;
        }
    }
}
