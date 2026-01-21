namespace MantenimientosTI.Models
{
    public partial class CatTipoRefaccion
    {
        public CatTipoRefaccion()
        {
            Refacciones = new HashSet<Refaccion>();
        }

        public int ClaveTipoRefaccion { get; set; }
        public int ClaveTipoUnidadMedida { get; set; }
        public string NombreTipoRefaccion { get; set; } = null!;

        public virtual CatTipoUnidadMedida CatTipoUnidadMedida { get; set; } = null!;
        public virtual ICollection<Refaccion> Refacciones { get; set; }
    }
}