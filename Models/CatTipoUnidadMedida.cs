using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MantenimientosTI.Models
{
    [Table("CatTipoUnidadMedida")]
    public partial class CatTipoUnidadMedida
    {
        public CatTipoUnidadMedida()
        {
            CatTipoRefacciones = new HashSet<CatTipoRefaccion>();
        }

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ClaveTipoUnidadMedida { get; set; }

        [Required]
        [StringLength(100)]
        public string NombreUnidadMedida { get; set; } = null!;

        [Required]
        [StringLength(2)]
        public string NumSerieRequerido { get; set; } = null!;

        public virtual ICollection<CatTipoRefaccion> CatTipoRefacciones { get; set; }
    }
}