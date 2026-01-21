namespace MantenimientosTI.Models
{
    public partial class MantenimientoRefacciones
    {
        public int NumOrden { get; set; }
        public int ClaveRefaccion { get; set; }
        public string Observaciones { get; set; } = null!;
        public virtual Mantenimiento NumOrdenNavigation { get; set; } = null!;
        public virtual Refaccion ClaveRefaccionNavigation { get; set; } = null!;
    }
}