using DocumentFormat.OpenXml.Spreadsheet;
using MantenimientosTI.Models;
using Microsoft.AspNetCore.Mvc;

namespace MantenimientosTI.Controllers
{
    // Controllers/ImageAuditController.cs
    public class ImageAuditController : Controller
    {
        private readonly MantenimientosTIContext _context;

        public ImageAuditController(MantenimientosTIContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Dashboard()
        {
            var alerts = await _context.SuspiciousImageAlerts
                .Include(a => a.Mantenimiento)
                .Include(a => a.Usuario)
                .Where(a => a.Status == "Pending")
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            var stats = new
            {
                TotalAlerts = await _context.SuspiciousImageAlerts.CountAsync(),
                PendingAlerts = await _context.SuspiciousImageAlerts.CountAsync(a => a.Status == "Pending"),
                HighRiskAlerts = await _context.SuspiciousImageAlerts.CountAsync(a => a.RiskLevel == "High"),
                TotalValidations = await _context.ImageValidationLogs.CountAsync()
            };

            ViewBag.Stats = stats;
            return View(alerts);
        }

        [HttpPost]
        public async Task<IActionResult> ResolveAlert(long alertId, string resolution, string notes)
        {
            var alert = await _context.SuspiciousImageAlerts.FindAsync(alertId);
            if (alert == null) return NotFound();

            alert.Status = "Resolved";
            alert.ResolvedBy = User.Identity.Name;
            alert.ResolvedAt = DateTime.Now;
            alert.ResolutionNotes = notes;

            await _context.SaveChangesAsync();

            TempData["Success"] = "Alerta resuelta exitosamente";
            return RedirectToAction("Dashboard");
        }
    }
}
