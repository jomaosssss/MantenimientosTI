// ViewModels/UnifiedDashboardViewModel.cs
using System.ComponentModel.DataAnnotations;

namespace MantenimientosTI.Models.ViewModels
{
    public class UnifiedDashboardViewModel
    {
        public DateTime StartDate { get; set; } = DateTime.Today.AddDays(-7);
        public DateTime EndDate { get; set; } = DateTime.Today.AddDays(1);
        public string UserId { get; set; }

        // Estadísticas principales (Fase 1)
        public int TotalSessions { get; set; }
        public int TotalPageViews { get; set; }
        public int TotalActions { get; set; }
        public int ActiveUsers { get; set; }

        // Nuevas estadísticas (Fase 2)
        public int FailedLogins { get; set; }
        public int SuccessfulLogins { get; set; }
        public int CsvUploads { get; set; }
        public int MaintenanceActions { get; set; }

        // Datos para gráficos
        public List<ActivityByHour> ActivityByHour { get; set; } = new List<ActivityByHour>();
        public List<ActivityByType> ActivityByType { get; set; } = new List<ActivityByType>();
        public List<TopUser> TopUsers { get; set; } = new List<TopUser>();

        // Actividad reciente
        public List<RecentActivity> RecentActivities { get; set; } = new List<RecentActivity>();

        // Nuevos datos para Fase 2
        public List<PageViewInfo> TopPages { get; set; } = new List<PageViewInfo>();
        public List<SecurityEvent> SecurityEvents { get; set; } = new List<SecurityEvent>();
    }

    // MODELO MEJORADO: TopUser con detalles de acciones
    public class TopUser
    {
        public string UserName { get; set; }
        public int ActivityCount { get; set; }
        public string Role { get; set; }
        public List<UserActionDetail> ActionDetails { get; set; } = new List<UserActionDetail>();
    }

    // NUEVO MODELO: Detalle de acciones por usuario
    public class UserActionDetail
    {
        public string ActionType { get; set; }
        public string ActionDescription { get; set; }
        public int Count { get; set; }
        public DateTime LastPerformed { get; set; }
    }

    // Modelos existentes (Fase 1)
    public class ActivityByHour
    {
        public int Hour { get; set; }
        public int Count { get; set; }
    }

    public class RecentActivity
    {
        public DateTime Timestamp { get; set; }
        public string UserName { get; set; }
        public string Action { get; set; }
        public string Description { get; set; }
    }

    // Nuevos modelos (Fase 2)
    public class ActivityByType
    {
        public string ActionType { get; set; }
            public string Description { get; set; } // ✅ CORRECTO (con una 'c')

        public int Count { get; set; }
        public string Color { get; set; }
    }

    public class PageViewInfo
    {
        public string PageName { get; set; }
        public int ViewCount { get; set; }
        public string Controller { get; set; }
    }

    public class SecurityEvent
    {
        public DateTime Timestamp { get; set; }
        public string UserName { get; set; }
        public string EventType { get; set; }
        public string Description { get; set; }
        public string Severity { get; set; } // "low", "medium", "high"
    }
}