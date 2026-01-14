using System;

namespace MantenimientosTI.Models.ViewModels
{
    public enum NivelAlerta
    {
        Bajo,
        Medio,
        Alto,
        Critico
    }

    public class AuditoriaViewModel
    {
        public int NumOrden { get; set; }
        public string Rpe { get; set; } = string.Empty;
        public string NombreTecnico { get; set; } = string.Empty;
        public string Zona { get; set; } = string.Empty;
        public string Agencia { get; set; } = string.Empty;
        public DateTime FechaRegistro { get; set; }
        public string Etapa { get; set; } = string.Empty;
        public string TipoAnomalia { get; set; } = string.Empty;

        // Datos del Origen
        public bool ExisteOriginal { get; set; }
        public int? NumOrdenOriginal { get; set; }
        public string? RpeOriginal { get; set; }
        public string? NombreOriginal { get; set; }
        public DateTime? FechaOriginal { get; set; }
        public string? AgenciaOriginal { get; set; } // Agregado para la vista
        public string? ZonaOriginal { get; set; }    // Agregado para la vista

        // Lógica de Presentación (Helper Properties)
        public string DiferenciaTiempo
        {
            get
            {
                if (!ExisteOriginal || !FechaOriginal.HasValue)
                    return string.Empty;

                var diferencia = FechaRegistro - FechaOriginal.Value;

                if (diferencia.TotalDays > 365)
                    return $"Hace {(int)(diferencia.TotalDays / 365)} años";
                else if (diferencia.TotalDays > 30)
                    return $"Hace {(int)(diferencia.TotalDays / 30)} meses";
                else if (diferencia.TotalDays > 1)
                    return $"Hace {(int)diferencia.TotalDays} días";
                else if (diferencia.TotalHours > 1)
                    return $"Hace {(int)diferencia.TotalHours} horas";
                else
                    return "Hace unos minutos";
            }
        }

        public NivelAlerta NivelAlerta
        {
            get
            {
                if (TipoAnomalia.Contains("DUPLICADO") && ExisteOriginal && Rpe == RpeOriginal)
                    return NivelAlerta.Alto; // Auto-plagio
                if (TipoAnomalia.Contains("DUPLICADO") && ExisteOriginal && Rpe != RpeOriginal)
                    return NivelAlerta.Critico; // Robo de evidencia
                if (TipoAnomalia.Contains("FECHA"))
                    return NivelAlerta.Medio;

                return NivelAlerta.Bajo;
            }
        }

        public string NivelAlertaClaseCSS
        {
            get
            {
                return NivelAlerta switch
                {
                    NivelAlerta.Critico => "table-danger",
                    NivelAlerta.Alto => "table-warning",
                    _ => ""
                };
            }
        }

        public string NivelAlertaIcono
        {
            get
            {
                return NivelAlerta switch
                {
                    NivelAlerta.Critico => "fas fa-skull-crossbones",
                    NivelAlerta.Alto => "fas fa-exclamation-circle",
                    NivelAlerta.Medio => "fas fa-clock",
                    _ => "fas fa-info-circle"
                };
            }
        }

        public string ObtenerDescripcionAnomalia()
        {
            if (TipoAnomalia == "IMAGEN DUPLICADA" && !ExisteOriginal)
                return "Posible copia interna (misma sesión)";
            if (TipoAnomalia == "FECHA ANTIGUA")
                return "Metadatos EXIF no coinciden con fecha reporte";

            return TipoAnomalia;
        }

        // Propiedades auxiliares para la vista
        public bool EsMismoTecnico => ExisteOriginal && Rpe == RpeOriginal;
        public bool EsMismaZona => ExisteOriginal && Zona == ZonaOriginal;
    }

    // Clase para las estadísticas del dashboard
    public class EstadisticasAuditoriaViewModel
    {
        public int TotalAnomalias { get; set; }
        public int TecnicosInvolucrados { get; set; }
        public int ZonasAfectadas { get; set; }
        public int Critico { get; set; }
    }
}