// Refaccion.cs
namespace MantenimientosTI.Models
{
    public partial class Refaccion
    {
        public Refaccion()
        {
            MantenimientoRefacciones = new HashSet<MantenimientoRefacciones>();
        }

        public int ClaveRefaccion { get; set; }
        public string ClaveDivision { get; set; }
        public string ClaveZona { get; set; }
        public int ClaveTipoRefaccion { get; set; }
        public string? NumeroSerie { get; set; }
        public string Modelo { get; set; } = null!;
        public string Marca { get; set; } = null!;
        public DateTime FechaAdquisicion { get; set; }
        public int NumContratoAdquisicion { get; set; }
        public string? Ocupado { get; set; }
        public int? Cantidad { get; set; }

        public virtual CatZona CatZona { get; set; }
        public virtual CatTipoRefaccion CatTipoRefaccion { get; set; }
        public virtual ICollection<MantenimientoRefacciones> MantenimientoRefacciones { get; set; }
    }
}