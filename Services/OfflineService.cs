using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EntregaSegura.Models;

namespace EntregaSegura.Services
{
    // =====================================================================
    // MODO OFFLINE
    // 1. Guarda en el teléfono una copia (caché) de las órdenes, el perfil y los motivos.
    // 2. Si no hay internet, las entregas y novedades se guardan en una COLA de pendientes
    //    (con sus fotos y firmas como archivos locales).
    // 3. Cuando vuelve el internet, la cola se envía sola a Supabase (sincronización).
    // 4. Permite entrar sin internet si el usuario ya inició sesión antes en este teléfono.
    // =====================================================================
    public static class OfflineService
    {
        private static readonly JsonSerializerOptions opcionesJson = new()
        {
            IgnoreReadOnlyProperties = true,      // no guardamos las propiedades calculadas (colores, textos)
            PropertyNameCaseInsensitive = true
        };

        private static readonly SemaphoreSlim candado = new(1, 1);   // evita dos sincronizaciones a la vez

        // Avisa a las pantallas cuando cambia la conexión o la cantidad de pendientes
        public static event Action? EstadoCambiado;

        public static bool HayInternet => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

        // Carpeta propia de cada usuario dentro del almacenamiento privado de la app
        private static string Carpeta
        {
            get
            {
                string usuario = string.IsNullOrEmpty(Sesion.UsuarioId) ? "anonimo" : Sesion.UsuarioId;
                string ruta = Path.Combine(FileSystem.AppDataDirectory, "offline", usuario);
                Directory.CreateDirectory(Path.Combine(ruta, "archivos"));
                return ruta;
            }
        }

        // Se llama una vez al iniciar la app (App.xaml.cs)
        public static void Iniciar()
        {
            Connectivity.Current.ConnectivityChanged += async (s, e) =>
            {
                EstadoCambiado?.Invoke();
                if (HayInternet && !string.IsNullOrEmpty(Sesion.UsuarioId))
                    await SincronizarAsync();
            };
        }

        // ===================== CACHÉ (copia local) =====================
        public static void Guardar<T>(string nombre, T datos)
        {
            try
            {
                File.WriteAllText(Path.Combine(Carpeta, nombre + ".json"), JsonSerializer.Serialize(datos, opcionesJson));
            }
            catch (Exception) { /* si no se puede guardar la copia, la app sigue funcionando en línea */ }
        }

        public static T? Leer<T>(string nombre)
        {
            try
            {
                string archivo = Path.Combine(Carpeta, nombre + ".json");
                if (!File.Exists(archivo)) return default;
                return JsonSerializer.Deserialize<T>(File.ReadAllText(archivo), opcionesJson);
            }
            catch (Exception)
            {
                return default;
            }
        }

        // Cambia el estado de una orden en la copia local (para que la lista se actualice sin internet)
        public static void ActualizarEstadoEnCache(long ordenId, string estado)
        {
            var ordenes = Leer<List<Orden>>("ordenes");
            var orden = ordenes?.FirstOrDefault(o => o.id == ordenId);
            if (ordenes == null || orden == null) return;
            orden.estado = estado;
            Guardar("ordenes", ordenes);
        }

        // ===================== COLA DE PENDIENTES =====================
        public static List<OperacionPendiente> Pendientes() => Leer<List<OperacionPendiente>>("pendientes") ?? new List<OperacionPendiente>();

        public static int CantidadPendientes => Pendientes().Count;

        private static void GuardarPendientes(List<OperacionPendiente> lista)
        {
            Guardar("pendientes", lista);
            EstadoCambiado?.Invoke();
        }

        public static string GuardarArchivo(byte[] bytes, string nombre)
        {
            File.WriteAllBytes(Path.Combine(Carpeta, "archivos", nombre), bytes);
            return nombre;
        }

        private static byte[]? LeerArchivo(string? nombre)
        {
            if (string.IsNullOrEmpty(nombre)) return null;
            string ruta = Path.Combine(Carpeta, "archivos", nombre);
            return File.Exists(ruta) ? File.ReadAllBytes(ruta) : null;
        }

        private static void BorrarArchivo(string? nombre)
        {
            if (string.IsNullOrEmpty(nombre)) return;
            string ruta = Path.Combine(Carpeta, "archivos", nombre);
            if (File.Exists(ruta)) File.Delete(ruta);
        }

        public static void Encolar(OperacionPendiente operacion)
        {
            var lista = Pendientes();
            lista.Add(operacion);
            GuardarPendientes(lista);
            ActualizarEstadoEnCache(operacion.OrdenId, operacion.Tipo == "ENTREGA" ? "ENTREGADA" : "NOVEDAD");
        }

        // ===================== SINCRONIZACIÓN =====================
        // Envía a Supabase las operaciones guardadas sin internet, en el orden en que se hicieron.
        // Devuelve cuántas se enviaron y cuántas quedan.
        public static async Task<(int enviadas, int restantes)> SincronizarAsync()
        {
            if (!HayInternet) return (0, CantidadPendientes);
            if (!await candado.WaitAsync(0)) return (0, CantidadPendientes);   // ya hay una sincronización en curso

            int enviadas = 0;
            try
            {
                if (!await SupabaseService.AsegurarSesionAsync())
                    return (0, CantidadPendientes);   // no hay token: debe iniciar sesión con internet

                var lista = Pendientes();
                foreach (var op in lista.ToList())
                {
                    try
                    {
                        string? rutaFoto = null, rutaFirma = null;
                        var bytesFoto = LeerArchivo(op.ArchivoFoto);
                        if (bytesFoto != null)
                            rutaFoto = await SupabaseService.SubirArchivo(bytesFoto, op.ArchivoFoto!, "image/jpeg");

                        if (op.Tipo == "ENTREGA")
                        {
                            var bytesFirma = LeerArchivo(op.ArchivoFirma);
                            if (bytesFirma != null)
                                rutaFirma = await SupabaseService.SubirArchivo(bytesFirma, op.ArchivoFirma!, "image/png");

                            await SupabaseService.RegistrarEntrega(op.OrdenId, op.Recibe ?? "", op.Latitud ?? 0, op.Longitud ?? 0,
                                rutaFoto ?? "", rutaFirma ?? "");
                        }
                        else
                        {
                            await SupabaseService.RegistrarNovedad(op.OrdenId, op.MotivoId, op.Descripcion ?? "",
                                op.Latitud, op.Longitud, rutaFoto);
                        }

                        // Enviada: se quita de la cola y se borran sus archivos locales
                        lista.Remove(op);
                        BorrarArchivo(op.ArchivoFoto);
                        BorrarArchivo(op.ArchivoFirma);
                        GuardarPendientes(lista);
                        enviadas++;
                    }
                    catch (Exception ex)
                    {
                        // Se deja en la cola para el siguiente intento y se detiene para respetar el orden
                        op.Intentos++;
                        op.UltimoError = ex.Message;
                        GuardarPendientes(lista);
                        break;
                    }
                }
                return (enviadas, lista.Count);
            }
            finally
            {
                candado.Release();
            }
        }

        // ===================== INICIO DE SESIÓN SIN INTERNET =====================
        // Después de un inicio de sesión en línea correcto se guarda en el teléfono:
        // - una huella (hash SHA-256) de la contraseña, nunca la contraseña en texto;
        // - el token de renovación (refresh token) en el almacenamiento seguro;
        // - el perfil del usuario (en la caché).
        private static string Huella(string correo, string clave)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{correo.ToLower()}:{clave}:EntregaSegura"));
            return Convert.ToHexString(bytes);
        }

        public static async Task RecordarSesionAsync(string correo, string clave)
        {
            try
            {
                await SecureStorage.Default.SetAsync("huella_" + correo.ToLower(), Huella(correo, clave));
                await SecureStorage.Default.SetAsync("refresh_" + correo.ToLower(), Sesion.RefreshToken);
                Preferences.Set("usuario_id_" + correo.ToLower(), Sesion.UsuarioId);
                Guardar("perfil", Sesion.Conductor);
            }
            catch (Exception) { /* si el teléfono no tiene almacenamiento seguro, solo no habrá acceso offline */ }
        }

        public static async Task<bool> IniciarSesionOfflineAsync(string correo, string clave)
        {
            correo = correo.ToLower();
            string? huella = await SecureStorage.Default.GetAsync("huella_" + correo);
            if (huella == null || huella != Huella(correo, clave)) return false;

            Sesion.Correo = correo;
            Sesion.UsuarioId = Preferences.Get("usuario_id_" + correo, "");
            Sesion.RefreshToken = await SecureStorage.Default.GetAsync("refresh_" + correo) ?? "";
            Sesion.Token = "";                 // se obtendrá uno nuevo cuando vuelva el internet
            Sesion.ModoOffline = true;
            Sesion.Conductor = Leer<Conductor>("perfil");
            return Sesion.Conductor != null;
        }

        public static void OlvidarSesion(string correo)
        {
            SecureStorage.Default.Remove("huella_" + correo.ToLower());
            SecureStorage.Default.Remove("refresh_" + correo.ToLower());
        }

        public static void ActualizarRefreshToken()
        {
            if (!string.IsNullOrEmpty(Sesion.Correo) && !string.IsNullOrEmpty(Sesion.RefreshToken))
                _ = SecureStorage.Default.SetAsync("refresh_" + Sesion.Correo, Sesion.RefreshToken);
        }
    }
}
