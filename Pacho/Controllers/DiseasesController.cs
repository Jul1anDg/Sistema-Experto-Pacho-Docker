using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pacho.Models;

namespace Pacho.Controllers
{
    /// <summary>
    /// Controlador encargado de la gestión de enfermedades dentro del sistema.
    /// Permite crear, editar, eliminar y visualizar enfermedades, incluyendo el manejo
    /// cifrado y descifrado de imágenes almacenadas en la base de datos.
    /// </summary>
    public class DiseasesController : Controller
    {
        private readonly BrainPachoContext _context;
        private readonly IDataProtector _protector;

        /// <summary>
        /// Constructor del controlador de enfermedades.
        /// Inicializa el contexto de base de datos y el protector de datos
        /// para manejar el cifrado seguro de imágenes.
        /// </summary>
        /// <param name="context">Contexto principal de la base de datos.</param>
        /// <param name="dp">Proveedor de protección de datos.</param>
        public DiseasesController(BrainPachoContext context, IDataProtectionProvider dp)
        {
            _context = context;
            _protector = dp.CreateProtector("Pacho.ProtectedImages.v1");
        }

        /// <summary>
        /// Muestra el listado de todas las enfermedades registradas.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index()
            => View(await _context.Diseases.ToListAsync());

        /// <summary>
        /// Retorna la vista para crear una nueva enfermedad.
        /// </summary>
        public IActionResult Create() => View(new Disease());

        /// <summary>
        /// Muestra los detalles de una enfermedad específica.
        /// </summary>
        /// <param name="id">Identificador de la enfermedad.</param>
        [HttpGet]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var disease = await _context.Diseases.FirstOrDefaultAsync(m => m.IdDisease == id);
            if (disease == null) return NotFound();

            return View(disease);
        }

        /// <summary>
        /// Retorna la vista de edición para una enfermedad existente.
        /// </summary>
        /// <param name="id">Identificador de la enfermedad.</param>
        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var disease = await _context.Diseases.FindAsync(id);
            if (disease == null) return NotFound();

            return View(disease);
        }

        /// <summary>
        /// Crea un nuevo registro de enfermedad en la base de datos,
        /// cifrando la imagen de referencia antes de su almacenamiento.
        /// </summary>
        /// <param name="disease">Entidad de enfermedad a crear.</param>
        /// <param name="imageFile">Archivo de imagen asociado a la enfermedad.</param>
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Disease disease, IFormFile imageFile)
        {
            if (imageFile != null)
            {
                var okExt = new[] { ".jpg", ".jpeg", ".png" };
                var ext = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
                if (!okExt.Contains(ext))
                    ModelState.AddModelError("ReferenceImage", "Solo se permiten archivos .jpg, .jpeg o .png");
            }

            if (!ModelState.IsValid) return View(disease);

            if (imageFile != null && imageFile.Length > 0)
            {
                using var ms = new MemoryStream();
                await imageFile.CopyToAsync(ms);
                var plain = ms.ToArray();

                // Cifra los bytes completos de la imagen antes de almacenarlos
                var cipher = _protector.Protect(plain);
                disease.ReferenceImageEncrypted = cipher;
                disease.ReferenceImageContentType = imageFile.ContentType;
            }

            disease.CreationDate = DateTime.Now;
            disease.TreatmentsTotal = 0;

            _context.Add(disease);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Actualiza los datos de una enfermedad existente.
        /// Si se adjunta una nueva imagen, reemplaza la imagen cifrada almacenada.
        /// </summary>
        /// <param name="id">Identificador de la enfermedad a editar.</param>
        /// <param name="disease">Entidad con los nuevos datos.</param>
        /// <param name="imageFile">Nueva imagen (opcional).</param>
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Disease disease, IFormFile imageFile)
        {
            if (id != disease.IdDisease) return NotFound();

            if (imageFile != null)
            {
                var okExt = new[] { ".jpg", ".jpeg", ".png" };
                var ext = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
                if (!okExt.Contains(ext))
                    ModelState.AddModelError("ReferenceImage", "Solo se permiten archivos .jpg, .jpeg o .png");
            }

            if (!ModelState.IsValid) return View(disease);

            var existing = await _context.Diseases.FindAsync(id);
            if (existing == null) return NotFound();

            existing.ScientificName = disease.ScientificName;
            existing.CommonName = disease.CommonName;
            existing.Description = disease.Description;
            existing.Symptoms = disease.Symptoms;
            existing.Conditions = disease.Conditions;
            existing.Asset = disease.Asset;

            if (imageFile != null && imageFile.Length > 0)
            {
                using var ms = new MemoryStream();
                await imageFile.CopyToAsync(ms);
                var plain = ms.ToArray();

                var cipher = _protector.Protect(plain);
                existing.ReferenceImageEncrypted = cipher;
                existing.ReferenceImageContentType = imageFile.ContentType;
            }

            _context.Update(existing);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Muestra la vista de confirmación de eliminación de una enfermedad.
        /// </summary>
        /// <param name="id">Identificador de la enfermedad.</param>
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var disease = await _context.Diseases.FirstOrDefaultAsync(m => m.IdDisease == id);
            if (disease == null) return NotFound();

            return View(disease);
        }

        /// <summary>
        /// Elimina una enfermedad y sus tratamientos asociados.
        /// </summary>
        /// <param name="id">Identificador de la enfermedad a eliminar.</param>
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var disease = await _context.Diseases.FindAsync(id);
            if (disease == null) return RedirectToAction(nameof(Index));

            var tratamientos = _context.Treatments.Where(t => t.DiseaseId == id);
            _context.Treatments.RemoveRange(tratamientos);

            _context.Diseases.Remove(disease);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Descifra y sirve la imagen almacenada cifradamente en la base de datos.
        /// Incluye medidas anti-cache para evitar almacenamiento temporal del archivo.
        /// </summary>
        /// <param name="id">Identificador de la enfermedad.</param>
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Image(int id)
        {
            var disease = await _context.Diseases.FindAsync(id);
            if (disease == null || disease.ReferenceImageEncrypted == null) return NotFound();

            byte[] bytes;
            try
            {
                bytes = _protector.Unprotect(disease.ReferenceImageEncrypted);
            }
            catch
            {
                return BadRequest("No se pudo descifrar la imagen.");
            }

            var contentType = string.IsNullOrWhiteSpace(disease.ReferenceImageContentType)
                ? "image/jpeg"
                : disease.ReferenceImageContentType;

            // Configuración para evitar caché del navegador
            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";

            return File(bytes, contentType);
        }
    }
}
