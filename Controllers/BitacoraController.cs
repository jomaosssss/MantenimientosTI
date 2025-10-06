using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MantenimientosTI.Models;
using System.Threading.Tasks;
using System.Linq;

namespace ProyectoMantenimientos.Controllers
{
    [Authorize(Roles = "ADMINISTRADOR")]
    public class BitacoraController : Controller
    {
        private readonly MantenimientosTIContext _context;

        public BitacoraController(MantenimientosTIContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var registros = await _context.RegistroActividad
                                          .OrderByDescending(r => r.FechaHora)
                                          .Take(1000)
                                          .ToListAsync();
            // Le decimos que use la vista "BitacoraVista.cshtml" y le pase los datos
            return View("BitacoraVista", registros); // <-- LÍNEA MODIFICADA
        }
    }
}