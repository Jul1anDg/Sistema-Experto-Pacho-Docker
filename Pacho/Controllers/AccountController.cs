using Microsoft.AspNetCore.Authentication;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pacho.Models;
using Pacho.Models.ViewModels;
using Pacho.Services;
using Microsoft.AspNetCore.WebUtilities;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;

/// <summary>
/// Controlador responsable de gestionar las operaciones de autenticación,
/// inicio y cierre de sesión, y recuperación de contraseñas.
/// </summary>
public class AccountController : Controller
{
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly BrainPachoContext _context;
    private readonly IEmailService _email;

    /// <summary>
    /// Constructor que inyecta el contexto de base de datos, servicio de correo
    /// y entorno del servidor para la carga de plantillas HTML.
    /// </summary>
    public AccountController(
        BrainPachoContext context,
        IEmailService email,
        IWebHostEnvironment webHostEnvironment)
    {
        _context = context;
        _email = email;
        _webHostEnvironment = webHostEnvironment;
    }

    // ============================================================
    // RECUPERACIÓN DE CONTRASEÑA
    // ============================================================

    /// <summary>
    /// Muestra el formulario para solicitar restablecimiento de contraseña.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    /// <summary>
    /// Procesa la solicitud de recuperación de contraseña. 
    /// Genera un token seguro y envía un correo con el enlace de restablecimiento.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        // Mensaje genérico (evita revelar si el correo existe)
        ViewBag.Message = "Si el correo existe, te enviaremos un enlace para restablecer tu contraseña.";

        // Busca el usuario activo asociado al correo
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email && u.Status == 1);
        if (user == null)
            return View();

        // Genera un token aleatorio (RAW) para incluir en el enlace del correo
        var rawTokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = WebEncoders.Base64UrlEncode(rawTokenBytes);

        // Cifra el token y lo almacena en BD junto a su fecha de expiración
        var tokenHash = Sha256(rawToken);
        user.RecoveryToken = tokenHash;
        user.RetokenExpirationDate = DateTime.UtcNow.AddMinutes(30);
        await _context.SaveChangesAsync();

        // Construye la URL de restablecimiento (incluye token RAW)
        var resetUrl = Url.Action("ResetPassword", "Account", new { token = rawToken }, Request.Scheme);

        // Carga y personaliza la plantilla HTML del correo
        var templatePath = Path.Combine(_webHostEnvironment.WebRootPath, "email-templates", "ResetPasswordTemplate.html");
        var html = await System.IO.File.ReadAllTextAsync(templatePath);
        html = html.Replace("{{RESET_URL}}", resetUrl);

        // Envía el correo electrónico
        await _email.SendAsync(user.Email, "Restablecimiento de contraseña", html);

        return View();
    }

    /// <summary>
    /// Calcula el hash SHA256 de una cadena para almacenar el token de forma segura.
    /// </summary>
    private static string Sha256(string input)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.AppendFormat("{0:x2}", b);
        return sb.ToString();
    }

    // ============================================================
    // RESTABLECIMIENTO DE CONTRASEÑA
    // ============================================================

    /// <summary>
    /// Muestra el formulario de restablecimiento de contraseña (GET).
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResetPassword(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest("Token faltante.");

        // Se pasa el token RAW a la vista (será validado por hash en POST)
        return View(new ResetPasswordViewModel { Token = token });
    }

    /// <summary>
    /// Procesa el restablecimiento de contraseña (POST).
    /// Verifica el token, actualiza la contraseña y limpia los campos temporales.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var tokenHash = Sha256(model.Token);

        // Verifica que el token sea válido y no esté expirado
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.RecoveryToken == tokenHash && u.RetokenExpirationDate > DateTime.UtcNow);

        if (user == null)
        {
            ModelState.AddModelError("", "El enlace no es válido o ya expiró.");
            return View(model);
        }

        // Actualiza la contraseña (hasheada con BCrypt)
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
        user.RecoveryToken = null;
        user.RetokenExpirationDate = null;
        await _context.SaveChangesAsync();

        TempData["Success"] = "Tu contraseña fue restablecida correctamente. Inicia sesión.";
        return RedirectToAction("Login", "Account");
    }

    // ============================================================
    // LOGIN / AUTENTICACIÓN
    // ============================================================

    /// <summary>
    /// Muestra el formulario de inicio de sesión.
    /// Si ya hay una sesión activa, redirige al área correspondiente según el rol.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Login()
    {
        // Limpia cualquier cookie de autenticación previa
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // Si ya hay usuario autenticado, redirige según el rol
        if (User.Identity.IsAuthenticated)
        {
            if (User.IsInRole("SuperAdmin"))
                return RedirectToAction("Experts", "Admin");

            if (User.IsInRole("Admin"))
                return RedirectToAction("Dashboard", "Admin");

            if (User.IsInRole("Experto"))
                return RedirectToAction("Index", "Treatment");

            if (User.IsInRole("Usuario"))
                return RedirectToAction("Index", "User");

            return RedirectToAction("Index", "Home");
        }

        return View();
    }

    /// <summary>
    /// Procesa la autenticación del usuario según email y contraseña.
    /// Valida credenciales, crea los claims y redirige al área correspondiente.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password)
    {
        // 1) Verifica credenciales básicas
        var user = await _context.Users
         .Include(u => u.RoleNavigation)
         .FirstOrDefaultAsync(u =>
             u.Email == email &&
             (u.Status == 1 || u.Status == 2 || u.Status == 3));

        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            ModelState.AddModelError("", "Usuario o contraseña inválidos.");
            return View();
        }

        // 2) Si el usuario pertenece al rol "Experto", obtiene su registro
        Expert expert = null;
        if (user.RoleNavigation?.Name == "Experto")
        {
            expert = await _context.Experts.FirstOrDefaultAsync(e => e.UserId == user.IdUser);
            if (expert == null)
            {
                TempData["Error"] = "No se encontró el registro de experto.";
                return RedirectToAction("AccessDenied", "ExpertTest");
            }
        }

        // 3) Construcción de Claims de autenticación (identidad del usuario)
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.IdUser.ToString()),
            new Claim(ClaimTypes.Name, user.Email),
            new Claim("FullName", $"{user.Name} {user.LastName}"),
            new Claim(ClaimTypes.Role, user.RoleNavigation?.Name ?? "SinRol"),
            new Claim("Status", user.Status.ToString())
        };

        if (expert != null)
        {
            claims.Add(new Claim("TestState", expert.TestState ?? ""));
            claims.Add(new Claim("TestGrade", (expert.TestGrade ?? 0).ToString()));
        }

        // Genera la identidad y establece cookie de autenticación
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
            }
        );

        // 4) Actualiza último acceso
        user.LastAccess = DateTime.Now;
        await _context.SaveChangesAsync();

        // 5) Redirige según rol y estado del experto
        var rol = user.RoleNavigation?.Name?.Trim() ?? "SinRol";

        switch (rol)
        {
            case "SuperAdmin":
                return RedirectToAction("PendingExperts", "Admin");

            case "Usuario":
                return RedirectToAction("Index", "User");

            case "Experto":
                // Redirección condicionada por el estado de la prueba de aptitud
                switch (expert.TestState?.Trim().ToLower())
                {
                    case "habilitado":
                        return RedirectToAction("TakeTest", "ExpertTest");
                    case "reprobado":
                        TempData["Error"] = "No has aprobado la prueba de aptitud.";
                        return RedirectToAction("AccessDenied", "ExpertTest");
                    case "aprobado":
                        return RedirectToAction("Index", "Treatment");
                    default:
                        return RedirectToAction("AccessDenied", "ExpertTest");
                }

            default:
                return RedirectToAction("Index", "Home");
        }
    }

    // ============================================================
    // LOGOUT
    // ============================================================

    /// <summary>
    /// Cierra la sesión del usuario actual, limpia las cookies y la caché del navegador.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        // Limpia sesión y cookie de autenticación
        HttpContext.Session.Clear();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // Evita el almacenamiento en caché posterior al logout
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        return RedirectToAction("Index", "Home");
    }
}
