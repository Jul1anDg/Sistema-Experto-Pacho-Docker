using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Pacho.Models;

namespace Pacho.Controllers
{
    /// <summary>
    /// Controlador encargado del registro de expertos en la plataforma.
    /// Gestiona la creación de solicitudes de expertos y el almacenamiento
    /// de la información básica del usuario y su experiencia.
    /// </summary>
    public class ExpertsController : Controller
    {
        private readonly BrainPachoContext _context;

        public ExpertsController(BrainPachoContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Muestra el formulario de registro de expertos.
        /// Carga las opciones disponibles para el tipo de experiencia.
        /// </summary>
        [HttpGet]
        public IActionResult RegisterExpert()
        {
            ViewBag.ExperienceTypes = new SelectList(new[] { "Empírica", "Técnica", "Profesional", "Tradición" });
            return View();
        }

        /// <summary>
        /// Procesa la solicitud de registro de un nuevo experto.
        /// Crea el usuario asociado, valida que el correo no esté duplicado
        /// y guarda la solicitud en estado "pendiente" hasta revisión del administrador.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterExpert(ExpertRegistrationViewModel model)
        {
            // Recarga la lista de tipos de experiencia para mantener el formulario coherente
            ViewBag.ExperienceTypes = new SelectList(new[] { "Empírica", "Técnica", "Profesional", "Tradición" });

            if (!ModelState.IsValid)
                return View(model);

            // Verifica si el correo ya está registrado
            if (await _context.Users.AnyAsync(u => u.Email == model.Email))
            {
                ModelState.AddModelError(nameof(model.Email), "Este correo ya está registrado.");
                return View(model);
            }

            // Crea el registro del usuario con su contraseña hasheada
            var user = new User
            {
                Email = model.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
                Name = model.Name,
                LastName = model.LastName,
                Phone = model.Phone,
                Role = 3, // Rol de experto
                Status = 2, // Pendiente de aprobación
                RegistrationDate = DateTime.Now
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Crea la solicitud del experto asociada al usuario creado
            var expert = new Expert
            {
                UserId = user.IdUser,
                ExperienceType = model.ExperienceType,
                ExperienceYears = model.ExperienceYears,
                TestState = "pendiente",
                ConfidenceLevel = "nuevo"
            };

            _context.Experts.Add(expert);
            await _context.SaveChangesAsync();

            // Indica al usuario que la solicitud fue enviada correctamente
            ViewBag.SolicitudEnviada = true;
            return View();
        }
    }
}
