using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MantenimientosTI.Models
{
    public partial class CatMotivoCancelacion
    {
        public CatMotivoCancelacion()
        {
            MotivosCancelacion = new HashSet<MotivoCancelacion>();
        }

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ClaveMotivo { get; set; }

        [Required(ErrorMessage = "El motivo de cancelación es requerido")]
        [StringLength(100, ErrorMessage = "El motivo no puede exceder los 100 caracteres")]
        [Column("MotivoCancelacion")]
        public string MotivoCancelacion { get; set; } = null!;

        [Required]
        [StringLength(10)]
        [Column("Estatus")]
        public string Estatus { get; set; } = "ACTIVO";

        // Navigation property
        public virtual ICollection<MotivoCancelacion> MotivosCancelacion { get; set; }
    }
}