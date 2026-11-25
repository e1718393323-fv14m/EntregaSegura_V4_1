using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EntregaSegura.Models;

namespace EntregaSegura.Services
{
    public static class SupabaseService
    {
        // ===== DATOS DE CONEXIÓN =====
        private const string Url = "https://psxxnkjmmlqsbiyygxnj.supabase.co";
        private const string ApiKey = "sb_publishable_0O6Kzs6F0xY3hiFzMFVJlA_ExIGXmhF";
        private const string Esquema = "uis";
        private const string Bucket = "fotos-entregas";

        private static readonly HttpClient cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private static HttpRequestMessage CrearPeticion(HttpMethod metodo, string ruta, bool conToken = true)
        {
            var peticion = new HttpRequestMessage(metodo, Url + ruta);
            peticion.Headers.Add("apikey", ApiKey);
            if (conToken && Sesion.Token != "")
                peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Sesion.Token);

            // Nuestras tablas están en el esquema "uis"
            peticion.Headers.Add("Accept-Profile", Esquema);
            peticion.Headers.Add("Content-Profile", Esquema);
            return peticion;
        }

        // Envía la petición; si el token venció (401), lo renueva una vez y repite
        private static async Task<HttpResponseMessage> Enviar(Func<HttpRequestMessage> crear)
        {
            var respuesta = await cliente.SendAsync(crear());
            if ((int)respuesta.StatusCode == 401 && Sesion.RefreshToken != "" && await RenovarSesionAsync())
                respuesta = await cliente.SendAsync(crear());
            return respuesta;
        }

        private static async Task RevisarRespuesta(HttpResponseMessage respuesta)
        {
            if (respuesta.IsSuccessStatusCode) return;
            string detalle = await respuesta.Content.ReadAsStringAsync();

            if (detalle.Contains("PGRST106"))
                throw new Exception("Supabase no expone el esquema 'uis'. Agréguelo en Project Settings > Data API > Exposed schemas.");
            if (detalle.Contains("PGRST202") || detalle.Contains("PGRST205") || detalle.Contains("does not exist"))
                throw new Exception("Faltan tablas o funciones en Supabase. Ejecute los scripts de la carpeta supabase en el SQL Editor.");
            if ((int)respuesta.StatusCode == 401)
                throw new Exception("La sesión expiró. Vuelva a iniciar sesión.");
            if (detalle.Contains("SOLO_ADMIN"))
                throw new Exception("Solo el administrador puede realizar esta acción.");
            if (detalle.Contains("USUARIO_NO_EXISTE"))
                throw new Exception("No existe un usuario con ese correo en Supabase Auth.");
            if (detalle.Contains("CLAVE_CORTA"))
                throw new Exception("La contraseña temporal debe tener al menos 8 caracteres.");

            throw new Exception($"Error del servidor ({(int)respuesta.StatusCode}): {detalle}");
        }

        // ================================================================
        // 1. SESIÓN
        // ================================================================
        public static async Task<bool> IniciarSesion(string correo, string contrasena)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, Url + "/auth/v1/token?grant_type=password");
            peticion.Headers.Add("apikey", ApiKey);
            peticion.Content = JsonContent.Create(new { email = correo, password = contrasena });

            var respuesta = await cliente.SendAsync(peticion);
            if (!respuesta.IsSuccessStatusCode)
                return false;   // usuario o contraseña incorrectos

            await LeerTokens(respuesta);
            Sesion.Correo = correo.ToLower();
            Sesion.ModoOffline = false;
            return true;
        }

        private static async Task LeerTokens(HttpResponseMessage respuesta)
        {
            string json = await respuesta.Content.ReadAsStringAsync();
            using var documento = JsonDocument.Parse(json);
            var raiz = documento.RootElement;
            Sesion.Token = raiz.GetProperty("access_token").GetString() ?? "";
            Sesion.RefreshToken = raiz.TryGetProperty("refresh_token", out var r) ? r.GetString() ?? "" : "";
            Sesion.UsuarioId = raiz.GetProperty("user").GetProperty("id").GetString() ?? "";
        }

        // Pide un token nuevo con el refresh token (sin volver a escribir la contraseña)
        public static async Task<bool> RenovarSesionAsync()
        {
            try
            {
                if (Sesion.RefreshToken == "") return false;
                var peticion = new HttpRequestMessage(HttpMethod.Post, Url + "/auth/v1/token?grant_type=refresh_token");
                peticion.Headers.Add("apikey", ApiKey);
                peticion.Content = JsonContent.Create(new { refresh_token = Sesion.RefreshToken });

                var respuesta = await cliente.SendAsync(peticion);
                if (!respuesta.IsSuccessStatusCode) return false;

                await LeerTokens(respuesta);
                Sesion.ModoOffline = false;
                OfflineService.ActualizarRefreshToken();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Si se entró sin internet, al volver la conexión se obtiene un token válido
        public static async Task<bool> AsegurarSesionAsync()
        {
            if (Sesion.Token != "") return true;
            return await RenovarSesionAsync();
        }

        // ================================================================
        // 2. PERFIL (se guarda en caché para el modo offline)
        // ================================================================
        public static async Task<Conductor?> ObtenerPerfil()
        {
            if (!OfflineService.HayInternet || Sesion.Token == "")
                return OfflineService.Leer<Conductor>("perfil") ?? Sesion.Conductor;

            var respuesta = await Enviar(() => CrearPeticion(HttpMethod.Get,
                "/rest/v1/conductores?select=id,email,nombres,cedula,telefono,debe_cambiar_clave," +
                "vehiculo:vehiculos(placa,modelo,capacidad_kg),rol:roles(nombre)" +
                "&email=eq." + Uri.EscapeDataString(Sesion.Correo)));
            await RevisarRespuesta(respuesta);
            var lista = await respuesta.Content.ReadFromJsonAsync<List<Conductor>>();
            var perfil = lista?.FirstOrDefault();
            if (perfil != null) OfflineService.Guardar("perfil", perfil);
            return perfil;
        }

        // ================================================================
        // 3. ÓRDENES (con caché: sin internet se muestran las últimas descargadas)
        // ================================================================
        public static async Task<List<Orden>> ObtenerOrdenes(string estado)
        {
            List<Orden> todas;

            if (OfflineService.HayInternet && await AsegurarSesionAsync())
            {
                try
                {
                    // Primero enviamos lo pendiente para no mostrar estados viejos
                    if (OfflineService.CantidadPendientes > 0)
                        await OfflineService.SincronizarAsync();

                    string campos = "id,numero,secuencia,estado,fecha," +
                                    "cliente:clientes(id,nombre,ruc,direccion,telefono,latitud,longitud)," +
                                    "conductor:conductores(nombres)," +
                                    "detalle:detalle_orden(cantidad,producto:productos(id,codigo,descripcion,precio))";
                    var respuesta = await Enviar(() => CrearPeticion(HttpMethod.Get,
                        $"/rest/v1/ordenes?select={campos}&order=conductor_id.asc,secuencia.asc"));
                    await RevisarRespuesta(respuesta);
                    todas = await respuesta.Content.ReadFromJsonAsync<List<Orden>>() ?? new List<Orden>();

                    // Las operaciones que siguen en cola se muestran con su nuevo estado
                    foreach (var op in OfflineService.Pendientes())
                    {
                        var o = todas.FirstOrDefault(x => x.id == op.OrdenId);
                        if (o != null) o.estado = op.Tipo == "ENTREGA" ? "ENTREGADA" : "NOVEDAD";
                    }
                    OfflineService.Guardar("ordenes", todas);
                }
                catch (HttpRequestException)
                {
                    todas = OfflineService.Leer<List<Orden>>("ordenes") ?? new List<Orden>();
                }
                catch (TaskCanceledException)
                {
                    todas = OfflineService.Leer<List<Orden>>("ordenes") ?? new List<Orden>();
                }
            }
            else
            {
                todas = OfflineService.Leer<List<Orden>>("ordenes") ?? new List<Orden>();
            }

            return estado switch
            {
                "PENDIENTE" => todas.Where(o => o.estado == "PENDIENTE").ToList(),
                "REALIZADAS" => todas.Where(o => o.estado != "PENDIENTE").ToList(),
                "ENTREGADA" => todas.Where(o => o.estado == "ENTREGADA").ToList(),
                "NOVEDAD" => todas.Where(o => o.estado == "NOVEDAD").ToList(),
                _ => todas
            };
        }

        // ================================================================
        // 4. MOTIVOS DE NOVEDAD (catálogo, también en caché)
        // ================================================================
        public static async Task<List<MotivoNovedad>> ObtenerMotivos()
        {
            if (!OfflineService.HayInternet || !await AsegurarSesionAsync())
                return OfflineService.Leer<List<MotivoNovedad>>("motivos") ?? new List<MotivoNovedad>();

            var respuesta = await Enviar(() => CrearPeticion(HttpMethod.Get, "/rest/v1/motivos_novedad?select=id,descripcion&order=id.asc"));
            await RevisarRespuesta(respuesta);
            var lista = await respuesta.Content.ReadFromJsonAsync<List<MotivoNovedad>>() ?? new List<MotivoNovedad>();
            OfflineService.Guardar("motivos", lista);
            return lista;
        }

        // ================================================================
        // 5. SUPERVISOR: novedades
        // ================================================================
        public static async Task<List<Novedad>> ObtenerNovedades()
        {
            if (!OfflineService.HayInternet || !await AsegurarSesionAsync())
                return OfflineService.Leer<List<Novedad>>("novedades") ?? new List<Novedad>();

            var respuesta = await Enviar(() => CrearPeticion(HttpMethod.Get,
                "/rest/v1/novedades?select=id,descripcion,fecha,revisada,respuesta,conductor_email," +
                "motivo:motivos_novedad(id,descripcion),orden:ordenes(numero,cliente:clientes(nombre))&order=revisada.asc,fecha.desc"));
            await RevisarRespuesta(respuesta);
            var lista = await respuesta.Content.ReadFromJsonAsync<List<Novedad>>() ?? new List<Novedad>();
            OfflineService.Guardar("novedades", lista);
            return lista;
        }

        public static async Task RevisarNovedad(long novedadId, string respuestaSupervisor)
        {
            if (!OfflineService.HayInternet || !await AsegurarSesionAsync())
                throw new Exception("Revisar una novedad requiere internet.");

            var respuesta = await Enviar(() =>
            {
                var p = CrearPeticion(HttpMethod.Patch, $"/rest/v1/novedades?id=eq.{novedadId}");
                p.Content = JsonContent.Create(new { revisada = true, respuesta = respuestaSupervisor });
                return p;
            });
            await RevisarRespuesta(respuesta);
        }

        // ================================================================
        // 6. GUARDAR ENTREGA / NOVEDAD (en línea o en la cola offline)
        // Devuelven true si se envió a Supabase y false si quedó guardada en el teléfono.
        // ================================================================
        public static async Task<bool> GuardarEntrega(Orden orden, string recibe, double latitud, double longitud,
            byte[] foto, byte[] firma)
        {
            string fecha = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string nombreFoto = $"{orden.numero}_foto_{fecha}.jpg";
            string nombreFirma = $"{orden.numero}_firma_{fecha}.png";

            if (OfflineService.HayInternet && await AsegurarSesionAsync())
            {
                try
                {
                    string rutaFoto = await SubirArchivo(foto, nombreFoto, "image/jpeg");
                    string rutaFirma = await SubirArchivo(firma, nombreFirma, "image/png");
                    await RegistrarEntrega(orden.id, recibe, latitud, longitud, rutaFoto, rutaFirma);
                    OfflineService.ActualizarEstadoEnCache(orden.id, "ENTREGADA");
                    return true;
                }
                catch (HttpRequestException) { /* se cayó la conexión: pasa a la cola */ }
                catch (TaskCanceledException) { /* tiempo de espera agotado: pasa a la cola */ }
            }

            OfflineService.Encolar(new OperacionPendiente
            {
                Tipo = "ENTREGA",
                OrdenId = orden.id,
                OrdenNumero = orden.numero,
                Recibe = recibe,
                Latitud = latitud,
                Longitud = longitud,
                ArchivoFoto = OfflineService.GuardarArchivo(foto, nombreFoto),
                ArchivoFirma = OfflineService.GuardarArchivo(firma, nombreFirma)
            });
            return false;
        }

        public static async Task<bool> GuardarNovedad(Orden orden, long motivoId, string descripcion,
            double? latitud, double? longitud, byte[]? foto)
        {
            string nombreFoto = $"{orden.numero}_novedad_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";

            if (OfflineService.HayInternet && await AsegurarSesionAsync())
            {
                try
                {
                    string? rutaFoto = foto != null ? await SubirArchivo(foto, nombreFoto, "image/jpeg") : null;
                    await RegistrarNovedad(orden.id, motivoId, descripcion, latitud, longitud, rutaFoto);
                    OfflineService.ActualizarEstadoEnCache(orden.id, "NOVEDAD");
                    return true;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
            }

            OfflineService.Encolar(new OperacionPendiente
            {
                Tipo = "NOVEDAD",
                OrdenId = orden.id,
                OrdenNumero = orden.numero,
                MotivoId = motivoId,
                Descripcion = descripcion,
                Latitud = latitud,
                Longitud = longitud,
                ArchivoFoto = foto != null ? OfflineService.GuardarArchivo(foto, nombreFoto) : null
            });
            return false;
        }

        // ----- Operaciones básicas (las usa también la sincronización) -----
        public static async Task<string> SubirArchivo(byte[] bytes, string nombreArchivo, string tipo)
        {
            string ruta = $"{Sesion.UsuarioId}/{nombreArchivo}";
            var respuesta = await Enviar(() =>
            {
                var p = CrearPeticion(HttpMethod.Post, $"/storage/v1/object/{Bucket}/{ruta}");
                p.Content = new ByteArrayContent(bytes);
                p.Content.Headers.ContentType = new MediaTypeHeaderValue(tipo);
                return p;
            });

            // Si el archivo ya existe (un reintento de sincronización), se considera subido
            if (!respuesta.IsSuccessStatusCode)
            {
                string detalle = await respuesta.Content.ReadAsStringAsync();
                if (detalle.Contains("Duplicate") || detalle.Contains("already exists")) return ruta;
                throw new Exception($"No se pudo subir el archivo ({(int)respuesta.StatusCode}): {detalle}");
            }
            return ruta;
        }

        public static async Task RegistrarEntrega(long ordenId, string recibe, double latitud, double longitud,
            string foto, string firma)
        {
            var respuesta = await Enviar(() =>
            {
                var p = CrearPeticion(HttpMethod.Post, "/rest/v1/entregas");
                p.Content = JsonContent.Create(new { orden_id = ordenId, recibe, latitud, longitud, foto, firma });
                return p;
            });
            // 23505 = la entrega ya estaba registrada (reintento): no es un error
            if (!respuesta.IsSuccessStatusCode)
            {
                string detalle = await respuesta.Content.ReadAsStringAsync();
                if (!detalle.Contains("23505"))
                    throw new Exception($"No se pudo registrar la entrega ({(int)respuesta.StatusCode}): {detalle}");
            }

            await CambiarEstado(ordenId, "ENTREGADA");
        }

        public static async Task RegistrarNovedad(long ordenId, long motivoId, string descripcion,
            double? latitud, double? longitud, string? foto)
        {
            var respuesta = await Enviar(() =>
            {
                var p = CrearPeticion(HttpMethod.Post, "/rest/v1/novedades");
                p.Content = JsonContent.Create(new { orden_id = ordenId, motivo_id = motivoId, descripcion, latitud, longitud, foto });
                return p;
            });
            await RevisarRespuesta(respuesta);
            await CambiarEstado(ordenId, "NOVEDAD");
        }

        private static async Task CambiarEstado(long ordenId, string estado)
        {
            var respuesta = await Enviar(() =>
            {
                var p = CrearPeticion(HttpMethod.Patch, $"/rest/v1/ordenes?id=eq.{ordenId}");
                p.Content = JsonContent.Create(new { estado });
                return p;
            });
            await RevisarRespuesta(respuesta);
        }

        // ================================================================
        // 7. RECUPERACIÓN DE CONTRASEÑA
        // ================================================================

        // Desde el Login, SIN sesión: registra la solicitud para el administrador.
        // La función responde igual exista o no el correo, para no revelar usuarios.
        public static async Task SolicitarRecuperacion(string correo)
        {
            var p = CrearPeticion(HttpMethod.Post, "/rest/v1/rpc/solicitar_recuperacion", conToken: false);
            p.Content = JsonContent.Create(new { p_email = correo.Trim().ToLower() });
            await RevisarRespuesta(await cliente.SendAsync(p));
        }

        // ADMINISTRADOR: usuarios registrados
        public static async Task<List<Conductor>> ObtenerUsuarios()
        {
            var respuesta = await Enviar(() => CrearPeticion(HttpMethod.Get,
                "/rest/v1/conductores?select=id,email,nombres,cedula,telefono,debe_cambiar_clave,rol:roles(nombre)&order=nombres.asc"));
            await RevisarRespuesta(respuesta);
            return await respuesta.Content.ReadFromJsonAsync<List<Conductor>>() ?? new List<Conductor>();
        }

        // ADMINISTRADOR: solicitudes pendientes de "Olvidé mi contraseña"
        public static async Task<List<SolicitudClave>> ObtenerSolicitudes()
        {
            var respuesta = await Enviar(() => CrearPeticion(HttpMethod.Get,
                "/rest/v1/solicitudes_clave?select=id,email,fecha,estado&estado=eq.PENDIENTE&order=fecha.asc"));
            await RevisarRespuesta(respuesta);
            return await respuesta.Content.ReadFromJsonAsync<List<SolicitudClave>>() ?? new List<SolicitudClave>();
        }

        // ADMINISTRADOR: asigna una contraseña temporal (función segura en la base de datos)
        public static async Task RestablecerClave(string correo, string claveTemporal)
        {
            if (!OfflineService.HayInternet || !await AsegurarSesionAsync())
                throw new Exception("Restablecer contraseñas requiere internet.");

            var respuesta = await Enviar(() =>
            {
                var p = CrearPeticion(HttpMethod.Post, "/rest/v1/rpc/admin_restablecer_clave");
                p.Content = JsonContent.Create(new { p_email = correo, p_clave = claveTemporal });
                return p;
            });
            await RevisarRespuesta(respuesta);
        }

        // USUARIO: cambia su propia contraseña (después de recibir una temporal)
        public static async Task CambiarMiClave(string nuevaClave)
        {
            var respuesta = await Enviar(() =>
            {
                var p = new HttpRequestMessage(HttpMethod.Put, Url + "/auth/v1/user");
                p.Headers.Add("apikey", ApiKey);
                p.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Sesion.Token);
                p.Content = JsonContent.Create(new { password = nuevaClave });
                return p;
            });
            if (!respuesta.IsSuccessStatusCode)
            {
                string detalle = await respuesta.Content.ReadAsStringAsync();
                if (detalle.Contains("same_password") || detalle.Contains("different from the old"))
                    throw new Exception("La nueva contraseña debe ser distinta de la temporal.");
                if (detalle.Contains("weak_password"))
                    throw new Exception("La contraseña es muy débil. Use al menos 8 caracteres con letras y números.");
                throw new Exception($"No se pudo cambiar la contraseña ({(int)respuesta.StatusCode}): {detalle}");
            }

            var marca = await Enviar(() =>
            {
                var p = CrearPeticion(HttpMethod.Post, "/rest/v1/rpc/marcar_clave_cambiada");
                p.Content = JsonContent.Create(new { });
                return p;
            });
            await RevisarRespuesta(marca);
        }
    }
}
