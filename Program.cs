using Dapper;
using GymClubAPI.Models;
using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);

// ── CORS: разрешаем любой origin ─────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();
app.UseCors("AllowAll");

// ── Строка подключения ───────────────────────────────────────────────────────
var connStr   = builder.Configuration.GetConnectionString("GymClubDB")!;
var photoRoot = builder.Configuration["PhotoPath"]
                ?? Path.Combine(AppContext.BaseDirectory, "photo");
MySqlConnection Db() => new MySqlConnection(connStr);

// ════════════════════════════════════════════════════════════════════════════
//  АВТОРИЗАЦИЯ
//  POST /api/login
//  Body: { "phone": "+37379123456" }
// ════════════════════════════════════════════════════════════════════════════
app.MapPost("/api/login", async (LoginRequest req) =>
{
    using var db = Db();

    var member = await db.QueryFirstOrDefaultAsync<Member>(
        "SELECT * FROM Members WHERE Phone = @Phone", new { req.Phone });

    if (member != null)
        return Results.Ok(new LoginResponse
        {
            Found    = true,
            Role     = "member",
            Id       = member.MemberID,
            FullName = member.FullName,
            Phone    = member.Phone,
            Email    = member.Email
        });

    var trainer = await db.QueryFirstOrDefaultAsync<Trainer>(
        "SELECT * FROM Trainers WHERE Phone = @Phone", new { req.Phone });

    if (trainer != null)
        return Results.Ok(new LoginResponse
        {
            Found    = true,
            Role     = "trainer",
            Id       = trainer.TrainerID,
            FullName = trainer.FullName,
            Phone    = trainer.Phone,
            Email    = trainer.Email
        });

    return Results.Ok(new LoginResponse { Found = false });
});

// ════════════════════════════════════════════════════════════════════════════
//  ПРОФИЛЬ УЧАСТНИКА
//  GET /api/members/{id}
// ════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/members/{id:int}", async (int id) =>
{
    using var db = Db();
    var member = await db.QueryFirstOrDefaultAsync<Member>(
        "SELECT * FROM Members WHERE MemberID = @id", new { id });
    return member is null ? Results.NotFound() : Results.Ok(member);
});

// ════════════════════════════════════════════════════════════════════════════
//  АБОНЕМЕНТЫ УЧАСТНИКА
//  GET /api/members/{id}/memberships
// ════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/members/{id:int}/memberships", async (int id) =>
{
    using var db = Db();
    var sql = """
        SELECT
            mm.MemberMembershipID,
            mm.MemberID,
            mm.PlanID,
            mp.PlanName,
            mp.Price,
            mp.DurationMonths,
            mm.StartDate,
            mm.EndDate,
            mm.Status,
            mm.Comment
        FROM MemberMemberships mm
        JOIN MembershipPlans mp ON mm.PlanID = mp.PlanID
        WHERE mm.MemberID = @id
        ORDER BY mm.StartDate DESC
        """;
    var result = await db.QueryAsync<Membership>(sql, new { id });
    return Results.Ok(result);
});

// ════════════════════════════════════════════════════════════════════════════
//  ПОСЕЩЕНИЯ УЧАСТНИКА
//  GET /api/members/{id}/visits?limit=20
// ════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/members/{id:int}/visits", async (int id, int limit = 20) =>
{
    using var db = Db();
    var sql = """
        SELECT
            v.VisitID,
            v.MemberID,
            m.FullName  AS MemberName,
            v.TrainerID,
            t.FullName  AS TrainerName,
            v.ProgramID,
            wp.ProgramName,
            v.VisitDate,
            v.VisitType,
            v.ResultNote
        FROM Visits v
        JOIN Members m ON v.MemberID = m.MemberID
        LEFT JOIN Trainers t ON v.TrainerID = t.TrainerID
        LEFT JOIN WorkoutPrograms wp ON v.ProgramID = wp.ProgramID
        WHERE v.MemberID = @id
        ORDER BY v.VisitDate DESC
        LIMIT @limit
        """;
    var result = await db.QueryAsync<Visit>(sql, new { id, limit });
    return Results.Ok(result);
});

// ════════════════════════════════════════════════════════════════════════════
//  ПРОФИЛЬ ТРЕНЕРА
//  GET /api/trainers/{id}
// ════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/trainers/{id:int}", async (int id) =>
{
    using var db = Db();
    var trainer = await db.QueryFirstOrDefaultAsync<Trainer>(
        "SELECT * FROM Trainers WHERE TrainerID = @id", new { id });
    return trainer is null ? Results.NotFound() : Results.Ok(trainer);
});

// ════════════════════════════════════════════════════════════════════════════
//  СПИСОК ВСЕХ ТРЕНЕРОВ
//  GET /api/trainers
// ════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/trainers", async () =>
{
    using var db = Db();
    var trainers = await db.QueryAsync<Trainer>("SELECT * FROM Trainers ORDER BY FullName");
    return Results.Ok(trainers);
});

// ════════════════════════════════════════════════════════════════════════════
//  ТРЕНИРОВКИ ТРЕНЕРА
//  GET /api/trainers/{id}/visits?limit=20
// ════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/trainers/{id:int}/visits", async (int id, int limit = 20) =>
{
    using var db = Db();
    var sql = """
        SELECT
            v.VisitID,
            v.MemberID,
            m.FullName  AS MemberName,
            v.TrainerID,
            t.FullName  AS TrainerName,
            v.ProgramID,
            wp.ProgramName,
            v.VisitDate,
            v.VisitType,
            v.ResultNote
        FROM Visits v
        JOIN Members m ON v.MemberID = m.MemberID
        LEFT JOIN Trainers t ON v.TrainerID = t.TrainerID
        LEFT JOIN WorkoutPrograms wp ON v.ProgramID = wp.ProgramID
        WHERE v.TrainerID = @id
        ORDER BY v.VisitDate DESC
        LIMIT @limit
        """;
    var result = await db.QueryAsync<Visit>(sql, new { id, limit });
    return Results.Ok(result);
});

// ════════════════════════════════════════════════════════════════════════════
//  ПРОГРАММЫ ТРЕНЕРА
//  GET /api/trainers/{id}/programs
// ════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/trainers/{id:int}/programs", async (int id) =>
{
    using var db = Db();
    var sql = """
        SELECT
            wp.ProgramID,
            wp.ProgramName,
            wp.TrainerID,
            t.FullName AS TrainerName,
            wp.DifficultyLevel,
            wp.DurationWeeks,
            wp.Goal,
            wp.Description
        FROM WorkoutPrograms wp
        JOIN Trainers t ON wp.TrainerID = t.TrainerID
        WHERE wp.TrainerID = @id
        ORDER BY wp.ProgramName
        """;
    var result = await db.QueryAsync<WorkoutProgram>(sql, new { id });
    return Results.Ok(result);
});

// ════════════════════════════════════════════════════════════════════════════
//  СПИСОК ТАРИФНЫХ ПЛАНОВ
//  GET /api/plans
// ════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/plans", async () =>
{
    using var db = Db();
    var plans = await db.QueryAsync<MembershipPlan>(
        "SELECT * FROM MembershipPlans ORDER BY Price");
    return Results.Ok(plans);
});

// ════════════════════════════════════════════════════════════════════════════
//  СПИСОК ВСЕХ ПРОГРАММ
//  GET /api/programs
// ════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/programs", async () =>
{
    using var db = Db();
    var sql = """
        SELECT
            wp.ProgramID,
            wp.ProgramName,
            wp.TrainerID,
            t.FullName AS TrainerName,
            wp.DifficultyLevel,
            wp.DurationWeeks,
            wp.Goal,
            wp.Description
        FROM WorkoutPrograms wp
        JOIN Trainers t ON wp.TrainerID = t.TrainerID
        ORDER BY wp.DifficultyLevel, wp.ProgramName
        """;
    var result = await db.QueryAsync<WorkoutProgram>(sql);
    return Results.Ok(result);
});

// ════════════════════════════════════════════════════════════════════════════
//  ОБНОВИТЬ ПРОФИЛЬ УЧАСТНИКА
//  PUT /api/members/{id}
// ════════════════════════════════════════════════════════════════════════════
app.MapPut("/api/members/{id:int}", async (int id, UpdateMemberRequest req) =>
{
    using var db = Db();
    var sql = """
        UPDATE Members
        SET Email       = @Email,
            Address     = @Address,
            FitnessGoal = @FitnessGoal,
            Notes       = @Notes
        WHERE MemberID  = @id
        """;
    var rows = await db.ExecuteAsync(sql, new
    {
        req.Email, req.Address, req.FitnessGoal, req.Notes, id
    });
    return rows > 0
        ? Results.Ok(new { success = true, message = "Профиль обновлён" })
        : Results.NotFound(new { success = false, message = "Участник не найден" });
});

// ════════════════════════════════════════════════════════════════════════════
//  КЛИЕНТЫ ТРЕНЕРА (список участников)
//  GET /api/trainers/{id}/members
// ════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/trainers/{id:int}/members", async (int id) =>
{
    using var db = Db();
    var sql = """
        SELECT
            m.MemberID,
            m.FullName,
            m.Phone,
            m.Email,
            m.Gender,
            m.FitnessGoal,
            COUNT(v.VisitID) AS TotalVisits
        FROM Members m
        JOIN Visits v ON m.MemberID = v.MemberID
        WHERE v.TrainerID = @id
        GROUP BY m.MemberID, m.FullName, m.Phone, m.Email, m.Gender, m.FitnessGoal
        ORDER BY m.FullName
        """;
    var result = await db.QueryAsync(sql, new { id });
    return Results.Ok(result);
});

// ════════════════════════════════════════════════════════════════════════════
//  КЛИЕНТЫ ТРЕНЕРА С ОСТАТКОМ ПОСЕЩЕНИЙ
//  GET /api/trainers/{id}/clients
// ════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/trainers/{id:int}/clients", async (int id) =>
{
    using var db = Db();
    var sql = """
        SELECT
            m.MemberID,
            m.FullName,
            m.Phone,
            m.Photo,
            m.FitnessGoal,
            mm.Status        AS MembershipStatus,
            mp.PlanName,
            mp.VisitLimit,
            mm.EndDate,
            (SELECT COUNT(*) FROM Visits v2
             WHERE v2.MemberID  = m.MemberID
               AND v2.TrainerID = @id
               AND mm.StartDate IS NOT NULL
               AND v2.VisitDate >= mm.StartDate
               AND v2.VisitDate <= IFNULL(mm.EndDate, NOW())
            ) AS UsedVisits
        FROM Members m
        JOIN Visits v ON m.MemberID = v.MemberID AND v.TrainerID = @id
        LEFT JOIN MemberMemberships mm ON mm.MemberID = m.MemberID
            AND mm.Status = 'Активен'
            AND mm.EndDate >= CURDATE()
        LEFT JOIN MembershipPlans mp ON mm.PlanID = mp.PlanID
        GROUP BY
            m.MemberID, m.FullName, m.Phone, m.Photo, m.FitnessGoal,
            mm.Status, mp.PlanName, mp.VisitLimit, mm.EndDate, mm.StartDate
        ORDER BY m.FullName
        """;
    var result = await db.QueryAsync(sql, new { id });
    return Results.Ok(result);
});

// ════════════════════════════════════════════════════════════════════════════
//  ОТМЕТИТЬ ТРЕНИРОВКУ
//  POST /api/visits
// ════════════════════════════════════════════════════════════════════════════
app.MapPost("/api/visits", async (CreateVisitRequest req) =>
{
    using var db = Db();
    var sql = """
        INSERT INTO Visits (MemberID, TrainerID, ProgramID, VisitDate, VisitType, ResultNote)
        VALUES (@MemberID, @TrainerID, @ProgramID, @VisitDate, @VisitType, @ResultNote);
        SELECT LAST_INSERT_ID();
        """;
    var newId = await db.ExecuteScalarAsync<long>(sql, new
    {
        req.MemberID,
        req.TrainerID,
        req.ProgramID,
        VisitDate  = DateTime.Now,
        req.VisitType,
        req.ResultNote
    });
    return Results.Ok(new { success = true, visitID = newId, message = "Тренировка отмечена" });
});

// ════════════════════════════════════════════════════════════════════════════
//  ТРЕНЕР УЧАСТНИКА
//  GET /api/members/{id}/trainer
// ════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/members/{id:int}/trainer", async (int id) =>
{
    using var db = Db();
    var sql = """
        SELECT
            t.TrainerID,
            t.FullName,
            t.Phone,
            t.Email,
            t.Specialization,
            t.ExperienceYears,
            t.Bio,
            t.WorkSchedule,
            t.Photo,
            COUNT(v.VisitID) AS SessionCount
        FROM Visits v
        JOIN Trainers t ON v.TrainerID = t.TrainerID
        WHERE v.MemberID = @id AND v.TrainerID IS NOT NULL
        GROUP BY
            t.TrainerID, t.FullName, t.Phone, t.Email,
            t.Specialization, t.ExperienceYears, t.Bio, t.WorkSchedule, t.Photo
        ORDER BY COUNT(v.VisitID) DESC
        LIMIT 1
        """;
    var trainer = await db.QueryFirstOrDefaultAsync(sql, new { id });
    return trainer is null
        ? Results.Ok(new { found = false })
        : Results.Ok(new { found = true, trainer });
});

// ════════════════════════════════════════════════════════════════════════════
//  ФОТО
//  GET /api/photos/{role}/{filename}
// ════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/photos/{role}/{filename}", (string role, string filename) =>
{
    var subfolder = role == "trainers" ? "trainers" : "visitors";
    var filePath  = Path.Combine(photoRoot, subfolder, Path.GetFileName(filename));

    if (!File.Exists(filePath))
        return Results.NotFound(new { message = "Фото не найдено" });

    var ext  = Path.GetExtension(filename).ToLower();
    var mime = ext switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png"            => "image/png",
        ".gif"            => "image/gif",
        ".bmp"            => "image/bmp",
        _                 => "application/octet-stream"
    };

    var bytes = File.ReadAllBytes(filePath);
    return Results.File(bytes, mime);
});

// ════════════════════════════════════════════════════════════════════════════
//  ПРОВЕРКА РАБОТЫ API
//  GET /api/health
// ════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/health", async () =>
{
    try
    {
        using var db = Db();
        await db.OpenAsync();
        return Results.Ok(new
        {
            status  = "ok",
            message = "API работает, БД подключена (Railway MySQL)",
            time    = DateTime.Now
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Ошибка подключения к БД: {ex.Message}");
    }
});

app.Run();

// ── Вспомогательные модели запросов ─────────────────────────────────────────

record UpdateMemberRequest(
    string? Email,
    string? Address,
    string? FitnessGoal,
    string? Notes
);

record CreateVisitRequest(
    int    MemberID,
    int    TrainerID,
    int?   ProgramID,
    string VisitType,
    string? ResultNote
);
