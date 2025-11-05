// Models/MotivoCancelacion.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MantenimientosTI.Models;

public partial class MotivoCancelacion
{
    public int ClaveMotivoCancelacion { get; set; }
    public int ClaveAgenda { get; set; }

    [StringLength(100)]
    public string Motivo { get; set; } = null!;

    [StringLength(500)]
    public string Justificacion { get; set; } = null!;

    [StringLength(100)]
    public string? UsuarioSolicitud { get; set; }
    public DateTime? FechaSolicitud { get; set; }

    [StringLength(100)]
    public string? UsuarioAprobacion { get; set; }
    public DateTime? FechaAprobacion { get; set; }

    public virtual Agendum ClaveAgendaNavigation { get; set; } = null!;
}