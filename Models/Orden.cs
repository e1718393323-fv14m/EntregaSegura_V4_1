namespace EntregaSegura.Models
{
    public class Orden
    {
        public long id { get; set; }
        public string numero { get; set; } = "";
        public int secuencia { get; set; }
        public string estado { get; set; } = "PENDIENTE";
        public DateTime fecha { get; set; }

        public Cliente cliente { get; set; } = new Cliente();
        public Conductor? conductor { get; set; }   
        public List<DetalleOrden> detalle { get; set; } = new List<DetalleOrden>();
        public string Titulo => $"{secuencia}. {numero} - {cliente.nombre}";

        public string ConductorTexto => "Conductor: " + (conductor?.nombres ?? "-");

        public int TotalUnidades => detalle.Sum(d => d.cantidad);

        public decimal Total => detalle.Sum(d => d.Subtotal);

        public string Resumen => $"{detalle.Count} producto(s) · {TotalUnidades} unidades · ${Total:F2}";

        public string EstadoTexto => estado switch
        {
            "ENTREGADA" => "Entregada",
            "NOVEDAD" => "Con novedad",
            _ => "Pendiente"
        };

        public Color FondoEstado => estado switch
        {
            "ENTREGADA" => Color.FromArgb("#E3F4E7"),
            "NOVEDAD" => Color.FromArgb("#FCE8E6"),
            _ => Color.FromArgb("#FFF1DF")
        };

        public string SecuenciaTexto => secuencia.ToString();

        public Color ColorEstado => estado switch
        {
            "ENTREGADA" => Color.FromArgb("#1E7B34"),
            "NOVEDAD" => Color.FromArgb("#B3261E"),
            _ => Color.FromArgb("#8A4B00")
        };
    }
}
