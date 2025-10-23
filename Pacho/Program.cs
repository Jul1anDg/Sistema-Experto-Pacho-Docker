using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Pacho.Models;
using Pacho.Services;

var builder = WebApplication.CreateBuilder(args);

// === Data Protection (persistente en carpeta del proyecto, monta volumen en Docker)
var keyRingPath = Path.Combine(builder.Environment.ContentRootPath, "PachoKeys");
Directory.CreateDirectory(keyRingPath);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
    .UseCryptographicAlgorithms(new AuthenticatedEncryptorConfiguration
    {
        EncryptionAlgorithm = EncryptionAlgorithm.AES_256_CBC,
        ValidationAlgorithm = ValidationAlgorithm.HMACSHA256
    });
// En Linux/Docker no uses .ProtectKeysWithDpapi(); si necesitas multi-host, usa certificado.

// Email + servicio
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddScoped<IEmailService, EmailService>();

// === DbContext con reintentos y timeout (ideal para Docker)
builder.Services.AddDbContext<BrainPachoContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseSqlServer(cs, sql =>
    {
           // ⬅️ si las migraciones NO están aquí, pon el nombre de ese ensamblado
        sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
        sql.CommandTimeout(120);
    });
});


// Auth cookies
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = false;
    });

// MVC + no-cache global
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new ResponseCacheAttribute
    {
        NoStore = true,
        Location = ResponseCacheLocation.None
    });
});

// Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Autorización
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ActiveExpert", policy =>
        policy.RequireRole("Experto")
              .RequireAssertion(context =>
              {
                  var statusClaim = context.User.FindFirst("Status")?.Value;
                  return statusClaim == "1";
              }));
});

var app = builder.Build();


using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BrainPachoContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbStartup");

    // Si NO hay migraciones en el proyecto (borraste la carpeta Migrations)
    var hasMigrations = db.Database.GetMigrations().Any();

    if (hasMigrations)
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("Migrate(): BD creada/actualizada con migraciones.");
    }
    else
    {
        // Caminito sin migraciones: usar el creador relacional directamente
        var creator = db.GetService<IRelationalDatabaseCreator>();

        // 1) Crear la BD si no existe
        if (!await creator.ExistsAsync())
        {
            await creator.CreateAsync();
            logger.LogInformation("Base de datos creada (sin migraciones).");
        }

        // 2) Crear tablas si no existen (si ya existen, esta llamada lanza pero la ignoramos)
        try
        {
            await creator.CreateTablesAsync();
            logger.LogInformation("Tablas creadas (sin migraciones).");
        }
        catch (SqlException ex) when (ex.Message.Contains("There is already an object named", StringComparison.OrdinalIgnoreCase))
        {
            // Algunas tablas ya existen: ignorar
            logger.LogInformation("Algunas tablas ya existían; se continúa.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudieron crear todas las tablas. Se continúa si ya existen.");
        }

        // 3) CHECK ('Si','No') en diagnostic_answers (déjalo igual)
        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID('dbo.diagnostic_answers','U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints 
               WHERE name = 'CK_diag_answers_text'
                 AND parent_object_id = OBJECT_ID('dbo.diagnostic_answers'))
        ALTER TABLE dbo.diagnostic_answers DROP CONSTRAINT CK_diag_answers_text;

    ALTER TABLE dbo.diagnostic_answers
    ADD CONSTRAINT CK_diag_answers_text
    CHECK ([answer_text] IN ('Si','No'));
END
");
        logger.LogInformation("CHECK CK_diag_answers_text verificado/creado.");

        // ===== Seed de DiagnosticQuestions con textos y orden exactos =====
        string[] diagTexts =
        {
    "¿Las hojas tienen un pelito o vello gris sobre ellas?",
    "¿Has visto manchas que parecen mojadas o transparentes en las hojas?",
    "¿Las hojas tienen manchas marrones?",
    "¿En estos días ha habido mucha humedad en el cultivo?",
    "¿Los últimos días han estado nublados, sin mucho sol?",
    "¿Ha hecho frío últimamente en el cultivo, especialmente en las mañanas o noches?",
    "¿Sientes que no corre bien el aire entre las plantas o dentro del invernadero?",
    "¿La planta ha tenido heridas por poda, golpes o insectos?",
    "¿Ha hecho calor últimamente en el cultivo?",
    "¿El riego se hace por aspersión (como lluvia artificial)?"
};
        if (!await db.Roles.AnyAsync())
        {
            var superAdminPermits = @"{""diseases"":{""create"":true,""read"":true,""edit"":true,""delete"":true},
                               ""experts"":{""create"":true,""read"":true,""edit"":true,""delete"":true},
                               ""users"":{""create"":true,""read"":true,""edit"":true,""delete"":true}}";

            var expertoPermits = @"{""treatments"":{""create"":true,""read"":true,""edit"":true,""delete"":false},
                               ""diagnoses"":{""read"":true}}";

            var roles = new List<Role>
    {
        new Role
        {
            Name = "SuperAdmin",
            Description = "Todos los permisos en la plataforma",
            Permits = superAdminPermits,
            Asset = true
        },
        new Role
        {
            Name = "Usuario",
            Description = "Usuario del Bot",
            Permits = @"{}",
            Asset = true
        },
        new Role
        {
            Name = "Experto",
            Description = "Usuario con conocimientos en fitopatología",
            Permits = expertoPermits,
            Asset = true
        }
    };

            db.Roles.AddRange(roles);
            await db.SaveChangesAsync();
            logger.LogInformation("Seed: Roles insertados (SuperAdmin/Usuario/Experto).");
        }

        // 4.x) Seed del usuario SuperAdmin
        const string superAdminEmail = "juliandge@outlook.com";
        if (!await db.Users.AnyAsync(u => u.Email == superAdminEmail))
        {
            // Hash seguro con BCrypt (cost 12 por defecto)
            var passwordHash = BCrypt.Net.BCrypt.HashPassword("Pacho.25"); // ⚠️ cámbiala luego por una real

            var user = new User
            {
                // id_user: identity → lo asigna SQL
                Email = superAdminEmail,
                PasswordHash = passwordHash,
                Name = "Julian",
                LastName = "Gonzalez",
                Role = 1,              // FK a roles.id_role (SuperAdmin)
                Status = 1,            // activo
                RegistrationDate = DateTime.Now, // o DateTime.UtcNow si prefieres
                LastAccess = DateTime.Now,
                Phone = "3136632135",
                RecoveryToken = null,
                RetokenExpirationDate = null
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();
            logger.LogInformation("Seed: usuario SuperAdmin creado ({Email}).", superAdminEmail);
        }
        else
        {
            logger.LogInformation("Seed: usuario SuperAdmin ya existe ({Email}).", superAdminEmail);
        }


        // 4.x) Seed de Diseases (si no hay registros aún)
        if (!await db.Diseases.AnyAsync())
        {
            var diseases = new List<Disease>
    {
        new Disease
        {
            // IdDisease: identity/autonumérico → lo asigna SQL
            ScientificName = "Xanthomonas hortorum pv. vitians",
            CommonName     = "Mancha bacteriana de la lechuga",
            Description    = "Manchas acuosas angulares que se oscurecen y perforan el limbo; reduce calidad comercial.",
            Symptoms       = "Puntos acuosos entre nervaduras que se vuelven pardo-negruzcos, halos amarillentos, perforaciones en hojas; venas y pecíolos pueden oscurecerse.",
            Conditions     = "Humedad alta y humedad foliar prolongada; salpicadura por lluvia/riego; 20–30 °C, escasa ventilación",
            Asset          = true,
            ReferenceImage = null,
            ReferenceImageEncrypted = null,
            ReferenceImageContentType = null
        },
        new Disease
        {
            ScientificName = "Botrytis cinerea",
            CommonName     = "Moho gris de la lechuga",
            Description    = "Podredumbre blanda con micelio gris ceniciento sobre hojas y tallos, especialmente en tejido senescente o herido.",
            Symptoms       = "Lesiones pardo claras que avanzan, tejido acuoso; esporulación gris en superficie; colapso de hojas externas y necrosis en el borde.",
            Conditions     = "Alta humedad relativa (>90 %) y mala ventilación; condensación; 12–20 °C; presencia de restos vegetales o heridas",
            Asset          = true,
            ReferenceImage = null,
            ReferenceImageEncrypted = null,
            ReferenceImageContentType = null
        }
    };

            db.Diseases.AddRange(diseases);
            await db.SaveChangesAsync();
            logger.LogInformation("Seed: Diseases insertadas ({Count}).", diseases.Count);
        }
        // Si no hay preguntas, insertar 1..10 con esos textos.
        // Si ya hay, actualiza texto/orden para mantener el set correcto sin borrar respuestas.
        var existingQs = await db.DiagnosticQuestions
                                 .OrderBy(q => q.Id)
                                 .ToListAsync();

        if (!existingQs.Any())
        {
            var qs = new List<DiagnosticQuestion>();
            for (int i = 0; i < diagTexts.Length; i++)
            {
                qs.Add(new DiagnosticQuestion
                {
                    Id = i + 1,                 // IDs determinísticos 1..10
                    QuestionOrder = i + 1,      // orden 1..10
                    QuestionText = diagTexts[i]
                });
            }
            db.DiagnosticQuestions.AddRange(qs);
            await db.SaveChangesAsync();
            logger.LogInformation("Seed: DiagnosticQuestions insertadas ({Count}).", qs.Count);
        }
        else
        {
            // Upsert liviano: alinea texto/orden por Id=1..10 sin tocar respuestas
            for (int i = 0; i < diagTexts.Length; i++)
            {
                int id = i + 1;
                var q = existingQs.FirstOrDefault(x => x.Id == id);
                if (q == null)
                {
                    db.DiagnosticQuestions.Add(new DiagnosticQuestion
                    {
                        Id = id,
                        QuestionOrder = id,
                        QuestionText = diagTexts[i]
                    });
                }
                else
                {
                    q.QuestionOrder = id;
                    q.QuestionText = diagTexts[i];
                }
            }
            await db.SaveChangesAsync();
            logger.LogInformation("Seed: DiagnosticQuestions actualizadas/alineadas a textos/orden.");
        }

        // ===== Seed de respuestas Si/No (solo si la tabla está vacía) =====
        if (!await db.DiagnosticAnswers.AnyAsync())
        {
            var answers = new List<DiagnosticAnswer>();
            for (int i = 1; i <= 10; i++)
            {
                answers.Add(new DiagnosticAnswer { Id = (i - 1) * 2 + 1, QuestionId = i, AnswerOrder = 1, AnswerText = "Si" });
                answers.Add(new DiagnosticAnswer { Id = (i - 1) * 2 + 2, QuestionId = i, AnswerOrder = 2, AnswerText = "No" });
            }
            db.DiagnosticAnswers.AddRange(answers);
            await db.SaveChangesAsync();
            logger.LogInformation("Seed: DiagnosticAnswers insertadas (Si/No).");
        }

        // 4.x) Seed de Questions (orden, texto y created_at)
        // Textos basados en tu captura; edítalos si quieres el wording exacto.
        var qSeed = new (int Order, string Text, DateTime CreatedAt)[]
        {
    (1,  "¿Qué hongo es responsable de la podredumbre blanca en el cultivo?",                     new DateTime(2025, 9, 13, 16, 18, 52, DateTimeKind.Utc)),
    (2,  "En Colombia, ¿cómo ha variado la incidencia de enfermedades fúngicas recientemente?",   new DateTime(2025, 9, 13, 16, 19, 34, DateTimeKind.Utc)),
    (3,  "Desde la práctica empírica, ¿qué señal de campo alerta sobre ataque temprano?",         new DateTime(2025, 9, 13, 16, 20, 19, DateTimeKind.Utc)),
    (4,  "¿Qué práctica tradicional ayuda a reducir la presión de inóculo en el lote?",           new DateTime(2025, 9, 13, 16, 21, 5,  DateTimeKind.Utc)),
    (5,  "La marchitez vascular en lechuga, ¿está asociada principalmente a qué agente?",         new DateTime(2025, 9, 13, 16, 21, 54, DateTimeKind.Utc)),
    (6,  "Desde la visión técnica, ¿qué biocontrolador es más útil en este escenario?",           new DateTime(2025, 9, 13, 16, 22, 36, DateTimeKind.Utc)),
    (7,  "¿Cuál de estas prácticas culturales NO es recomendada para el manejo?",                 new DateTime(2025, 9, 13, 16, 23, 19, DateTimeKind.Utc)),
    (8,  "Según saberes tradicionales, ¿qué material se ha utilizado históricamente para cubrir?",new DateTime(2025, 9, 13, 16, 24, 16, DateTimeKind.Utc)),
    (9,  "¿Qué estrategia refleja mejor un enfoque de Manejo Integrado de Plagas (MIP)?",         new DateTime(2025, 9, 13, 16, 25, 9,  DateTimeKind.Utc)),
    (10, "En lechuga crespa, ¿qué diferencia clave permite distinguir el problema a campo?",       new DateTime(2025, 9, 13, 16, 26, 13, DateTimeKind.Utc)),
        };

        if (!await db.Questions.AnyAsync())
        {
            var toInsert = qSeed.Select(x => new Question
            {
                // Id: identity → lo asigna SQL
                QuestionText = x.Text,
                CreatedAt = x.CreatedAt,      // respeta tu “fecha de creación”
                Order = x.Order           // se mapea a 'order_position'
            }).ToList();

            db.Questions.AddRange(toInsert);
            await db.SaveChangesAsync();
            logger.LogInformation("Seed: Questions insertadas ({Count}).", toInsert.Count);
        }
        else
        {
            // Upsert por orden: alinea texto/fecha sin borrar nada
            foreach (var item in qSeed)
            {
                var q = await db.Questions.SingleOrDefaultAsync(q => q.Order == item.Order);
                if (q == null)
                {
                    db.Questions.Add(new Question
                    {
                        QuestionText = item.Text,
                        CreatedAt = item.CreatedAt,
                        Order = item.Order
                    });
                }
                else
                {
                    q.QuestionText = item.Text;
                    q.CreatedAt = item.CreatedAt; // si quieres conservar la fecha existente, comenta esta línea
                }
            }
            await db.SaveChangesAsync();
            logger.LogInformation("Seed: Questions actualizadas/alineadas por orden.");
        }

        // ====== SEED de Answers (para preguntas 'questions') ======
        // Mapea por ORDEN de la pregunta (1..10) → lista de respuestas.
        // EDITA los textos y los flags (isCorrect, isActive) según tus dos archivos.
        var answersByOrder = new Dictionary<int, (string Text, bool IsCorrect, bool IsActive)[]>
        {
            // EJEMPLOS (pon aquí tus respuestas reales)
            // Q1
            [1] = new[] {
        ("Pythium ultimum",                 true,  true),
        ("Sclerotinia sclerotiorum",        false, true),
        ("Alternaria alternata",            false, true),
        ("Fusarium oxysporum",              false, true),
    },
            // Q2
            [2] = new[] {
        ("Incidencia creciente por humedad",    true,  true),
        ("Incidencia nula en temporada seca",   false, true),
        ("No hay cambios reportados",           false, true),
        ("Baja por radiación intensa",          false, true),
    },
            // Q3
            [3] = new[] {
        ("Manchas acuosas en hojas",            true,  true),
        ("Lesiones por sol directo",            false, true),
        ("Daño mecánico por viento",            false, true),
        ("Deficiencia de magnesio",             false, true),
    },
            // Q4
            [4] = new[] {
        ("Rotación de cultivos",                true,  true),
        ("Siembra continua en el mismo lote",   false, true),
        ("Riego por aspersión nocturno",        false, true),
        ("Alta densidad de siembra",            false, true),
    },
            // Q5
            [5] = new[] {
        ("Fusarium oxysporum",                  true,  true),
        ("Botrytis cinerea",                    false, true),
        ("Rhizoctonia solani",                  false, true),
        ("Pseudomonas syringae",                false, true),
    },
            // Q6
            [6] = new[] {
        ("Bacillus thuringiensis",              true,  true),
        ("Extractos de nicotina",               false, true),
        ("Trichoderma spp.",                    true,  true),
        ("Carbón mineral",                      false, true),
    },
            // Q7
            [7] = new[] {
        ("Uso exclusivo de fungicidas sintéticos", false, true),
        ("Manejo integrado (MIP)",                true,  true),
        ("Dejar que la enfermedad progrese",      false, true),
        ("Sal común en hojas",                    false, true),
    },
            // Q8
            [8] = new[] {
        ("Paja / cobertura vegetal",            true,  true),
        ("Plástico negro siempre",              false, true),
        ("Ceniza de madera en exceso",          false, true),
        ("Cal agrícola en exceso",              false, true),
    },
            // Q9
            [9] = new[] {
        ("Mejorar ventilación en invernadero",  true,  true),
        ("Evitar riego nocturno por aspersión", true,  true),
        ("Exceso de densidad de siembra",       false, true),
    },
            // Q10
            [10] = new[] {
        ("Presencia de micelio gris",           true,  true),
        ("Solarización del suelo",              false, true),
        ("Residuo infectado como abono",        false, true),
    },
        };

        // Construye mapa ORDEN → ID real de la pregunta en BD
        var orderToId = await db.Questions
            .Select(q => new { q.Id, q.Order })
            .ToDictionaryAsync(x => x.Order, x => x.Id);

        // Para evitar duplicados: lee respuestas existentes (por pregunta) y compara por texto
        foreach (var kv in answersByOrder)
        {
            var order = kv.Key;
            if (!orderToId.TryGetValue(order, out var questionId))
            {
                logger.LogWarning("Seed Answers: no existe question con order={Order}. Se omite.", order);
                continue;
            }

            var desired = kv.Value;

            // respuestas existentes para esta pregunta
            var existingTexts = await db.Answers
                .Where(a => a.QuestionId == questionId)
                .Select(a => a.AnswerText)
                .ToListAsync();

            // prepara nuevas (solo las que no existan por texto)
            var toAdd = desired
                .Where(x => !existingTexts.Contains(x.Text))
                .Select(x => new Answer
                {
                    // Id: identity ⇒ lo asigna SQL
                    QuestionId = questionId,
                    AnswerText = x.Text,
                    IsCorrect = x.IsCorrect,
                    IsActive = x.IsActive
                })
                .ToList();

            if (toAdd.Count > 0)
            {
                db.Answers.AddRange(toAdd);
                await db.SaveChangesAsync();
                logger.LogInformation("Seed Answers: agregadas {Count} respuestas para question_id={Q}.", toAdd.Count, questionId);
            }
            else
            {
                // (Opcional) sincroniza flags si ya existían
                var existing = await db.Answers.Where(a => a.QuestionId == questionId).ToListAsync();
                foreach (var ans in existing)
                {
                    var spec = desired.FirstOrDefault(d => d.Text == ans.AnswerText);
                    if (spec.Text != null)
                    {
                        ans.IsCorrect = spec.IsCorrect;
                        ans.IsActive = spec.IsActive;
                    }
                }
                await db.SaveChangesAsync();
                logger.LogInformation("Seed Answers: actualizados flags para question_id={Q}.", questionId);
            }
        }
    }
}

// Pipeline HTTP
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // OJO: si en Docker no tienes HTTPS expuesto/configurado, podrías desactivar esta línea:
    app.UseHsts();
}

// Si no tienes HTTPS en contenedor, considera comentar esta línea para evitar redirección a https:
// app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

// Recomendado: auth/autorization y luego session
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
