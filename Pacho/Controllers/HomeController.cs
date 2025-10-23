using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Diagnostics;
using Pacho.Models;

namespace Pacho.Controllers
{
    /// <summary>
    /// Controlador principal del sistema.
    /// Gestiona la página de inicio, navegación pública y redirección según el rol del usuario autenticado.
    /// Implementa cierre de sesión y visualización de vistas informativas.
    /// </summary>
    public class HomeController : Controller
    {
        public HomeController()
        {
        }

        /// <summary>
        /// Acción principal de inicio del sistema.
        /// - Permite acceso anónimo.
        /// - Si se solicita `logout=true`, ejecuta el cierre de sesión seguro (sign-out + limpieza de sesión).
        /// - Si el usuario ya está autenticado, redirige automáticamente según su rol:
        ///   * SuperAdmin → vista de expertos pendientes (AdminController)
        ///   * Usuario → vista principal del usuario (UserController)
        ///   * Experto → permanece en Home si lo desea.
        /// </summary>
        /// <param name="logout">Indica si se debe cerrar la sesión actual.</param>
        [AllowAnonymous]
        public async Task<IActionResult> Index(bool logout = false)
        {
            // Si el parámetro logout es verdadero, se finaliza la sesión activa y se limpia el contexto
            if (logout)
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                HttpContext.Session.Clear();
                return RedirectToAction("Index");
            }

            // Si hay un usuario autenticado, redirige según su rol correspondiente
            if (User.Identity.IsAuthenticated)
            {
                if (User.IsInRole("SuperAdmin"))
                    return RedirectToAction("PendingExperts", "Admin");

                if (User.IsInRole("Usuario"))
                    return RedirectToAction("Index", "User");

                // Los usuarios con rol "Experto" pueden permanecer en Home
            }

            // Renderiza la vista principal de bienvenida
            return View();
        }

        /// <summary>
        /// Muestra la vista informativa sobre la presentación del bot de diagnóstico.
        /// Accesible sin autenticación.
        /// </summary>
        [AllowAnonymous]
        public IActionResult BotPresentation()
        {
            return View();
        }

        /// <summary>
        /// Muestra la política de privacidad del sistema.
        /// Accesible públicamente.
        /// </summary>
        [AllowAnonymous]
        public IActionResult Privacy()
        {
            return View();
        }

        /// <summary>
        /// Muestra la información del equipo de desarrollo o colaboradores del proyecto.
        /// Accesible públicamente.
        /// </summary>
        [AllowAnonymous]
        public IActionResult OurTeam()
        {
            return View();
        }

        /// <summary>
        /// Acción estándar de manejo de errores.
        /// Muestra la vista de error con el identificador de solicitud actual.
        /// Se desactiva el almacenamiento en caché de la respuesta.
        /// </summary>
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }
}
