// Models/MotivoCancelacion.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MantenimientosTI.Models;

// Models/MotivoCancelacion.cs (actualizado)
public partial class MotivoCancelacion
{
    public int ClaveMotivoCancelacion { get; set; }

    // NUEVO: Relación con el catálogo
    public int ClaveMotivo { get; set; }

    public int ClaveAgenda { get; set; }
    public string Justificacion { get; set; } = null!;

    // CAMBIAR A RPE (varchar(5))
    public string? UsuarioSolicitud { get; set; }
    public DateTime? FechaSolicitud { get; set; }
    public string? UsuarioAprobacion { get; set; }
    public DateTime? FechaAprobacion { get; set; }



    // NAVIGATION PROPERTIES
    public virtual CatMotivoCancelacion CatMotivo { get; set; } = null!;
    public virtual Agendum ClaveAgendaNavigation { get; set; } = null!;
}