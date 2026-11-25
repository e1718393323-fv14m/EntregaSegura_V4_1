namespace EntregaSegura.Models
{
    public class Cliente
    {
        public long id { get; set; }
        public string nombre { get; set; } = "";
        public string? ruc { get; set; }
        public string direccion { get; set; } = "";
        public string? telefono { get; set; }
        public double latitud { get; set; }
        public double longitud { get; set; }
    }

    public class Producto
    {
        public long id { get; set; }
        public string codigo { get; set; } = "";
        public string descripcion { get; set; } = "";
        public decimal precio { get; set; }
    }

    public class DetalleOrden
    {
        public int cantidad { get; set; }
        public Producto producto { get; set; } = new Producto();

        public decimal Subtotal => cantidad * producto.precio;
        public string Texto => $"{cantidad} x {producto.descripcion}";
        public string SubtotalTexto => $"${Subtotal:F2}";
    }

    public class Vehiculo
    {
        public string placa { get; set; } = "";
        public string modelo { get; set; } = "";
        public int capacidad_kg { get; set; }
    }

    public class Rol
    {
        public string nombre { get; set; } = "CONDUCTOR";
    }

    public class Conductor
    {
        public long id { get; set; }
        public string email { get; set; } = "";
        public string nombres { get; set; } = "";
        public string cedula { get; set; } = "";
        public string? telefono { get; set; }
        public Vehiculo? vehiculo { get; set; }
        public Rol? rol { get; set; }
        public bool debe_cambiar_clave { get; set; }   // true cuando el administrador asignó una clave temporal

        public bool EsSupervisor => rol?.nombre == "SUPERVISOR";
        public bool EsAdministrador => rol?.nombre == "ADMINISTRADOR";
        public string RolTexto => rol?.nombre switch
        {
            "ADMINISTRADOR" => "Administrador",
            "SUPERVISOR" => "Supervisor",
            _ => "Conductor"
        };

        // Para la lista de usuarios del administrador
        public string Iniciales => string.Concat(nombres.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpper(p[0])));
        public string Detalle => $"{email} · {RolTexto}";
        public string EstadoClave => debe_cambiar_clave ? "Clave temporal asignada" : "";
    }

    // Fila de uis.solicitudes_clave (pedidos de "Olvidé mi contraseña")
    public class SolicitudClave
    {
        public long id { get; set; }
        public string email { get; set; } = "";
        public DateTime fecha { get; set; }
        public string estado { get; set; } = "PENDIENTE";

        public string FechaTexto => fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    }

    // Operación guardada en el teléfono mientras no hay internet (modo offline)
    public class OperacionPendiente
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Tipo { get; set; } = "ENTREGA";          // ENTREGA o NOVEDAD
        public long OrdenId { get; set; }
        public string OrdenNumero { get; set; } = "";
        public string? Recibe { get; set; }
        public long MotivoId { get; set; }
        public string? Descripcion { get; set; }
        public double? Latitud { get; set; }
        public double? Longitud { get; set; }
        public string? ArchivoFoto { get; set; }               // nombre del archivo local
        public string? ArchivoFirma { get; set; }
        public DateTime Fecha { get; set; } = DateTime.Now;
        public int Intentos { get; set; }
        public string? UltimoError { get; set; }
    }
    public class Novedad
    {
        public long id { get; set; }
        public string descripcion { get; set; } = "";
        public DateTime fecha { get; set; }
        public bool revisada { get; set; }
        public string? respuesta { get; set; }
        public string conductor_email { get; set; } = "";
        public MotivoNovedad motivo { get; set; } = new MotivoNovedad();
        public Orden orden { get; set; } = new Orden();

        public string Titulo => $"{orden.numero} - {orden.cliente.nombre}";
        public string Detalle => $"{motivo.descripcion}: {descripcion}";
        public string Info => $"{conductor_email} · {fecha.ToLocalTime():dd/MM HH:mm}";
        public string EstadoTexto => revisada ? "Respuesta: " + (respuesta ?? "") : "Toque para registrar la revisión";
        public Color ColorEstado => revisada ? Color.FromArgb("#1E7B34") : Color.FromArgb("#B3261E");
        public Color FondoEstado => revisada ? Color.FromArgb("#E3F4E7") : Color.FromArgb("#FCE8E6");
        public string Chip => revisada ? "Revisada" : "Por revisar";
    }
    public class MotivoNovedad
    {
        public long id { get; set; }
        public string descripcion { get; set; } = "";

        public override string ToString() => descripcion;
    }
}
