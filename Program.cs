using Dapper;
using GymClubAPI.Models;
using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();
app.UseCors("AllowAll");

var connStr   = builder.Configuration.GetConnectionString("GymClubDB")!;
var photoRoot = builder.Configuration["PhotoPath"]
                ?? Path.Combine(AppContext.BaseDirectory, "photo");
MySqlConnection Db() => new MySqlConnection(connStr);

// ════════════════════════════════════════════════════════════════════════════
//  АВТОРИЗАЦИЯ
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
            Email    = member.Email,
            Photo    = member.Photo,
            Barcode  = member.BarcodeValue ?? $"GYM-{member.MemberID:D5}"
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
            Email    = trainer.Email,
            Photo    = trainer.Photo
        });

    return Results.Ok(new LoginResponse { Found = false });
});

// ════════════════════════════════════════════════════════════════════════════
//  ШТРИХКОД — проверка на входе в зал
// ════════════════════════════════════════════════════════════════════════════

app.MapPost("/api/barcode/verify", async (BarcodeVerifyRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.BarcodeValue))
        return Results.BadRequest(new BarcodeVerifyResponse
        {
            Allowed = false,
            Message = "Пустой штрихкод."
        });

    using var db = Db();

    var member = await db.QueryFirstOrDefaultAsync<Member>(
        "SELECT * FROM Members WHERE BarcodeValue = @barcodeValue",
        new { barcodeValue = req.BarcodeValue.Trim().ToUpper() });

    if (member == null)
        return Results.Ok(new BarcodeVerifyResponse
        {
            Allowed = false,
            Message = "Участник не найден. Обратитесь на рецепцию."
        });

    var sql = @"
        SELECT mm.MemberMembershipID, mm.Status, mm.EndDate,
               mp.PlanName, mp.VisitLimit,
               (SELECT COUNT(*) FROM Visits v
                WHERE v.MemberID = mm.MemberID
                  AND v.VisitDate >= mm.StartDate
                  AND v.VisitDate <= IFNULL(mm.EndDate, NOW())) AS UsedVisits
        FROM MemberMemberships mm
        JOIN MembershipPlans mp ON mm.PlanID = mp.PlanID
        WHERE mm.MemberID = @memberId
          AND mm.Status = 'Активен'
          AND mm.EndDate >= CURDATE()
        ORDER BY mm.EndDate DESC
        LIMIT 1";

    var membership = await db.QueryFirstOrDefaultAsync(sql, new { memberId = member.MemberID });

    if (membership == null)
        return Results.Ok(new BarcodeVerifyResponse
        {
            Allowed    = false,
            MemberName = member.FullName,
            Photo      = member.Photo,
            Message    = "Нет активного абонемента. Обратитесь на рецепцию."
        });

    if (membership.VisitLimit != null && membership.UsedVisits >= membership.VisitLimit)
        return Results.Ok(new BarcodeVerifyResponse
        {
            Allowed    = false,
            MemberName = member.FullName,
            Photo      = member.Photo,
            PlanName   = membership.PlanName,
            Message    = $"Лимит посещений исчерпан ({membership.UsedVisits}/{membership.VisitLimit})."
        });

    await db.ExecuteAsync(
        @"INSERT INTO Visits (MemberID, VisitDate, VisitType, ResultNote)
          VALUES (@memberId, NOW(), 'Самостоятельная тренировка', 'Вход через штрихкод')",
        new { memberId = member.MemberID });

    int daysLeft = (int)(membership.EndDate - DateTime.Today).TotalDays;

    return Results.Ok(new BarcodeVerifyResponse
    {
        Allowed    = true,
        MemberName = member.FullName,
        Photo      = member.Photo,
        PlanName   = membership.PlanName,
        Message    = daysLeft <= 7
            ? $"Добро пожаловать, {member.FullName}! Абонемент истекает через {daysLeft} дн."
            : $"Добро пожаловать, {member.FullName}!",
        DaysLeft   = daysLeft
    });
});

// ════════════════════════════════════════════════════════════════════════════
//  УЧАСТНИКИ
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/members/{id:int}", async (int id) =>
{
    using var db = Db();
    var m = await db.QueryFirstOrDefaultAsync<Member>(
        "SELECT * FROM Members WHERE MemberID = @id", new { id });
    return m is null ? Results.NotFound() : Results.Ok(m);
});

app.MapGet("/api/members/{id:int}/memberships", async (int id) =>
{
    using var db = Db();
    var sql = @"SELECT mm.MemberMembershipID, mm.MemberID, mm.PlanID,
                       mp.PlanName, mp.Price, mp.DurationMonths,
                       mm.TrainerID, mm.SessionsUsed,
                       mm.StartDate, mm.EndDate, mm.Status, mm.Comment,
                       mp.VisitLimit
                FROM MemberMemberships mm
                JOIN MembershipPlans mp ON mm.PlanID = mp.PlanID
                WHERE mm.MemberID = @id
                ORDER BY mm.StartDate DESC";
    return Results.Ok(await db.QueryAsync(sql, new { id }));
});

app.MapGet("/api/members/{id:int}/visits", async (int id, int limit = 20) =>
{
    using var db = Db();
    var sql = @"SELECT v.VisitID, v.MemberID, m.FullName AS MemberName,
                       v.TrainerID, t.FullName AS TrainerName,
                       v.ProgramID, wp.ProgramName,
                       v.VisitDate, v.VisitType, v.ResultNote
                FROM Visits v
                JOIN Members m ON v.MemberID = m.MemberID
                LEFT JOIN Trainers t ON v.TrainerID = t.TrainerID
                LEFT JOIN WorkoutPrograms wp ON v.ProgramID = wp.ProgramID
                WHERE v.MemberID = @id
                ORDER BY v.VisitDate DESC
                LIMIT @limit";
    return Results.Ok(await db.QueryAsync<Visit>(sql, new { id, limit }));
});

app.MapPut("/api/members/{id:int}", async (int id, UpdateMemberRequest req) =>
{
    using var db = Db();
    var rows = await db.ExecuteAsync(
        "UPDATE Members SET Email=@Email, Address=@Address, FitnessGoal=@FitnessGoal, Notes=@Notes WHERE MemberID=@id",
        new { req.Email, req.Address, req.FitnessGoal, req.Notes, id });
    return rows > 0
        ? Results.Ok(new { success = true, message = "Профиль обновлён" })
        : Results.NotFound(new { success = false });
});

// ════════════════════════════════════════════════════════════════════════════
//  ТРЕНЕРЫ
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/trainers/{id:int}", async (int id) =>
{
    using var db = Db();
    var t = await db.QueryFirstOrDefaultAsync<Trainer>(
        "SELECT * FROM Trainers WHERE TrainerID = @id", new { id });
    return t is null ? Results.NotFound() : Results.Ok(t);
});

app.MapGet("/api/trainers", async () =>
{
    using var db = Db();
    return Results.Ok(await db.QueryAsync<Trainer>("SELECT * FROM Trainers ORDER BY FullName"));
});

app.MapGet("/api/trainers/{id:int}/visits", async (int id, int limit = 20) =>
{
    using var db = Db();
    var sql = @"SELECT v.VisitID, v.MemberID, m.FullName AS MemberName,
                       m.Photo,
                       v.TrainerID, t.FullName AS TrainerName,
                       v.ProgramID, wp.ProgramName,
                       v.VisitDate, v.VisitType, v.ResultNote
                FROM Visits v
                JOIN Members m ON v.MemberID = m.MemberID
                LEFT JOIN Trainers t ON v.TrainerID = t.TrainerID
                LEFT JOIN WorkoutPrograms wp ON v.ProgramID = wp.ProgramID
                WHERE v.TrainerID = @id
                ORDER BY v.VisitDate DESC
                LIMIT @limit";
    return Results.Ok(await db.QueryAsync<Visit>(sql, new { id, limit }));
});

// ── Обновить фото тренера ────────────────────────────────────────────────────
app.MapPut("/api/trainers/{id:int}/photo", async (int id, UpdatePhotoRequest req) =>
{
    using var db = Db();
    var rows = await db.ExecuteAsync(
        "UPDATE Trainers SET Photo = @Photo WHERE TrainerID = @id",
        new { Photo = req.PhotoUrl, id });
    return rows > 0
        ? Results.Ok(new { success = true })
        : Results.NotFound(new { success = false });
});

// ── Обновить фото участника ──────────────────────────────────────────────────
app.MapPut("/api/members/{id:int}/photo", async (int id, UpdatePhotoRequest req) =>
{
    using var db = Db();
    var rows = await db.ExecuteAsync(
        "UPDATE Members SET Photo = @Photo WHERE MemberID = @id",
        new { Photo = req.PhotoUrl, id });
    return rows > 0
        ? Results.Ok(new { success = true })
        : Results.NotFound(new { success = false });
});

app.MapGet("/api/trainers/{id:int}/programs", async (int id) =>
{
    using var db = Db();
    var sql = @"SELECT wp.ProgramID, wp.ProgramName, wp.TrainerID,
                       t.FullName AS TrainerName, wp.DifficultyLevel,
                       wp.DurationWeeks, wp.Goal, wp.Description
                FROM WorkoutPrograms wp
                JOIN Trainers t ON wp.TrainerID = t.TrainerID
                WHERE wp.TrainerID = @id
                ORDER BY wp.ProgramName";
    return Results.Ok(await db.QueryAsync<WorkoutProgram>(sql, new { id }));
});

app.MapGet("/api/trainers/{id:int}/members", async (int id) =>
{
    using var db = Db();
    var sql = @"SELECT m.MemberID, m.FullName, m.Phone, m.Email, m.Gender,
                       m.FitnessGoal, COUNT(v.VisitID) AS TotalVisits
                FROM Members m
                JOIN Visits v ON m.MemberID = v.MemberID
                WHERE v.TrainerID = @id
                GROUP BY m.MemberID, m.FullName, m.Phone, m.Email, m.Gender, m.FitnessGoal
                ORDER BY m.FullName";
    return Results.Ok(await db.QueryAsync(sql, new { id }));
});

// ════════════════════════════════════════════════════════════════════════════
//  ТРЕНЕР — КЛИЕНТЫ С АБОНЕМЕНТАМИ (обновлено)
//  GET /api/trainers/{id}/clients
//  Возвращает клиентов, чьи абонементы привязаны к этому тренеру.
//  Показывает счётчик сессий из абонемента (SessionsUsed / VisitLimit).
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/trainers/{id:int}/clients", async (int id) =>
{
    using var db = Db();
    var sql = @"
        SELECT
            m.MemberID,
            m.FullName,
            m.Phone,
            m.Photo,
            m.FitnessGoal,
            mm.MemberMembershipID,
            mp.PlanName,
            mm.Status        AS MembershipStatus,
            mm.EndDate,
            mp.VisitLimit    AS SessionLimit,
            mm.SessionsUsed
        FROM MemberMemberships mm
        JOIN Members m           ON mm.MemberID  = m.MemberID
        JOIN MembershipPlans mp  ON mm.PlanID    = mp.PlanID
        WHERE mm.TrainerID = @id
          AND mm.Status    = 'Активен'
          AND mm.EndDate   >= CURDATE()
        ORDER BY m.FullName";

    var clients = await db.QueryAsync<TrainerClient>(sql, new { id });
    return Results.Ok(clients);
});

// ════════════════════════════════════════════════════════════════════════════
//  ТРЕНЕР — СПИСОК ВСЕХ КЛИЕНТОВ (включая без активного абонемента)
//  GET /api/trainers/{id}/clients/all
//  Все клиенты, у кого когда-либо был абонемент с этим тренером
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/trainers/{id:int}/clients/all", async (int id) =>
{
    using var db = Db();
    var sql = @"
        SELECT
            m.MemberID,
            m.FullName,
            m.Phone,
            m.Photo,
            m.FitnessGoal,
            mm.MemberMembershipID,
            mp.PlanName,
            mm.Status        AS MembershipStatus,
            mm.EndDate,
            mp.VisitLimit    AS SessionLimit,
            mm.SessionsUsed
        FROM MemberMemberships mm
        JOIN Members m           ON mm.MemberID  = m.MemberID
        JOIN MembershipPlans mp  ON mm.PlanID    = mp.PlanID
        WHERE mm.TrainerID = @id
        ORDER BY mm.Status DESC, m.FullName";

    var clients = await db.QueryAsync<TrainerClient>(sql, new { id });
    return Results.Ok(clients);
});

// ════════════════════════════════════════════════════════════════════════════
//  ТРЕНЕР — СПИСАТЬ ОДНУ СЕССИЮ
//  POST /api/memberships/{membershipId}/use-session
//  Вызывается когда тренер нажимает "Записать тренировку".
//  Одновременно создаёт Visit и увеличивает SessionsUsed.
// ════════════════════════════════════════════════════════════════════════════

app.MapPost("/api/memberships/{membershipId:int}/use-session",
    async (int membershipId, UseSessionRequest req) =>
{
    using var db = Db();

    // 1. Загружаем абонемент
    var membership = await db.QueryFirstOrDefaultAsync(@"
        SELECT mm.MemberMembershipID, mm.MemberID, mm.TrainerID,
               mm.Status, mm.EndDate, mm.SessionsUsed,
               mp.VisitLimit, mp.PlanName
        FROM MemberMemberships mm
        JOIN MembershipPlans mp ON mm.PlanID = mp.PlanID
        WHERE mm.MemberMembershipID = @membershipId",
        new { membershipId });

    if (membership == null)
        return Results.NotFound(new { success = false, message = "Абонемент не найден." });

    if (membership.Status != "Активен")
        return Results.BadRequest(new { success = false, message = "Абонемент не активен." });

    if (membership.EndDate < DateTime.Today)
        return Results.BadRequest(new { success = false, message = "Срок абонемента истёк." });

    // 2. Проверяем лимит сессий
    if (membership.VisitLimit != null && membership.SessionsUsed >= membership.VisitLimit)
        return Results.BadRequest(new
        {
            success = false,
            message = $"Все сессии исчерпаны ({membership.SessionsUsed}/{membership.VisitLimit}). Клиенту нужен новый абонемент."
        });

    // 3. Определяем дату тренировки и длительность
    var visitDate      = req.VisitDate ?? DateTime.Now;
    int durationMinutes = req.DurationMinutes ?? 60;
    var visitEnd       = visitDate.AddMinutes(durationMinutes);
    int trainerId      = (int)membership.TrainerID;

    // 4. Проверяем конфликт расписания тренера:
    //    есть ли уже тренировка в этот промежуток времени?
    var conflict = await db.QueryFirstOrDefaultAsync(@"
        SELECT v.VisitDate, m.FullName AS MemberName,
               COALESCE(v.DurationMinutes, 60) AS DurationMinutes
        FROM Visits v
        JOIN Members m ON v.MemberID = m.MemberID
        WHERE v.TrainerID = @tId
          AND v.VisitDate < @newEnd
          AND DATE_ADD(v.VisitDate, INTERVAL COALESCE(v.DurationMinutes, 60) MINUTE) > @newStart
        LIMIT 1",
        new { tId = trainerId, newStart = visitDate, newEnd = visitEnd });

    if (conflict != null)
    {
        string conflictTime  = ((DateTime)conflict.VisitDate).ToString("HH:mm");
        int    conflictDur   = (int)conflict.DurationMinutes;
        string conflictEnd   = ((DateTime)conflict.VisitDate).AddMinutes(conflictDur).ToString("HH:mm");
        string conflictName  = (string)conflict.MemberName;
        return Results.Conflict(new
        {
            success = false,
            message = $"На это время уже записан {conflictName} ({conflictTime}–{conflictEnd}). Выберите другое время."
        });
    }

    // 5. Создаём Visit
    var visitId = await db.ExecuteScalarAsync<long>(@"
        INSERT INTO Visits (MemberID, TrainerID, ProgramID, VisitDate, DurationMinutes, VisitType, ResultNote)
        VALUES (@MemberID, @TrainerID, @ProgramID, @VisitDate, @DurationMinutes, 'Персональная тренировка', @ResultNote);
        SELECT LAST_INSERT_ID();",
        new
        {
            MemberID        = (int)membership.MemberID,
            TrainerID       = trainerId,
            ProgramID       = req.ProgramID,
            VisitDate       = visitDate,
            DurationMinutes = durationMinutes,
            ResultNote      = req.ResultNote
        });

    // 5. Увеличиваем счётчик сессий в абонементе
    await db.ExecuteAsync(@"
        UPDATE MemberMemberships
        SET SessionsUsed = SessionsUsed + 1
        WHERE MemberMembershipID = @membershipId",
        new { membershipId });

    // 6. Считаем остаток
    int newUsed = (int)membership.SessionsUsed + 1;
    int? limit  = membership.VisitLimit != null ? (int?)membership.VisitLimit : null;
    int? left   = limit.HasValue ? Math.Max(0, limit.Value - newUsed) : null;

    return Results.Ok(new
    {
        success      = true,
        visitId,
        sessionsUsed = newUsed,
        sessionLimit = limit,
        sessionsLeft = left,
        message      = left.HasValue
            ? $"Тренировка записана. Осталось сессий: {left}"
            : "Тренировка записана."
    });
});

// ════════════════════════════════════════════════════════════════════════════
//  ТРЕНЕР — ИНФОРМАЦИЯ ОБ АБОНЕМЕНТЕ КЛИЕНТА
//  GET /api/memberships/{membershipId}/session-info
//  Быстрая проверка перед записью — сколько сессий осталось
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/memberships/{membershipId:int}/session-info", async (int membershipId) =>
{
    using var db = Db();
    var info = await db.QueryFirstOrDefaultAsync(@"
        SELECT mm.MemberMembershipID,
               mm.MemberID,
               m.FullName AS MemberName,
               mm.TrainerID,
               mm.Status,
               mm.EndDate,
               mm.SessionsUsed,
               mp.VisitLimit AS SessionLimit,
               mp.PlanName
        FROM MemberMemberships mm
        JOIN Members m          ON mm.MemberID = m.MemberID
        JOIN MembershipPlans mp ON mm.PlanID   = mp.PlanID
        WHERE mm.MemberMembershipID = @membershipId",
        new { membershipId });

    if (info == null) return Results.NotFound();

    int used    = (int)info.SessionsUsed;
    int? limit  = info.SessionLimit != null ? (int?)info.SessionLimit : null;
    int? left   = limit.HasValue ? Math.Max(0, limit.Value - used) : null;

    return Results.Ok(new
    {
        membershipId   = (int)info.MemberMembershipID,
        memberId       = (int)info.MemberID,
        memberName     = (string)info.MemberName,
        planName       = (string)info.PlanName,
        status         = (string)info.Status,
        endDate        = (DateTime)info.EndDate,
        sessionsUsed   = used,
        sessionLimit   = limit,
        sessionsLeft   = left,
        isExhausted    = limit.HasValue && used >= limit.Value
    });
});

// ════════════════════════════════════════════════════════════════════════════
//  УЧАСТНИК — ТРЕНЕР (кто чаще всего тренировал)
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/members/{id:int}/trainer", async (int id) =>
{
    using var db = Db();
    var sql = @"SELECT t.TrainerID, t.FullName, t.Phone, t.Email,
                       t.Specialization, t.ExperienceYears,
                       t.Bio, t.WorkSchedule, t.Photo,
                       COUNT(v.VisitID) AS SessionCount
                FROM Visits v
                JOIN Trainers t ON v.TrainerID = t.TrainerID
                WHERE v.MemberID = @id AND v.TrainerID IS NOT NULL
                GROUP BY t.TrainerID, t.FullName, t.Phone, t.Email,
                         t.Specialization, t.ExperienceYears, t.Bio, t.WorkSchedule, t.Photo
                ORDER BY COUNT(v.VisitID) DESC
                LIMIT 1";
    var trainer = await db.QueryFirstOrDefaultAsync(sql, new { id });
    if (trainer is null) return Results.Ok(new { found = false });
    // Возвращаем плоский объект — мобильное приложение читает поля напрямую
    return Results.Ok(new
    {
        found           = true,
        trainerID       = (int)trainer.TrainerID,
        fullName        = (string)(trainer.FullName ?? ""),
        phone           = (string)(trainer.Phone ?? ""),
        email           = (string)(trainer.Email ?? ""),
        specialization  = (string)(trainer.Specialization ?? ""),
        experienceYears = (int)(trainer.ExperienceYears ?? 0),
        bio             = (string)(trainer.Bio ?? ""),
        workSchedule    = (string)(trainer.WorkSchedule ?? ""),
        photo           = (string)(trainer.Photo ?? ""),
        sessionCount    = (int)trainer.SessionCount
    });
});

// ════════════════════════════════════════════════════════════════════════════
//  ПЛАНЫ, ПРОГРАММЫ, ПОСЕЩЕНИЯ
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/plans", async () =>
{
    using var db = Db();
    return Results.Ok(await db.QueryAsync<MembershipPlan>(
        "SELECT * FROM MembershipPlans ORDER BY Price"));
});

app.MapGet("/api/programs", async () =>
{
    using var db = Db();
    var sql = @"SELECT wp.ProgramID, wp.ProgramName, wp.TrainerID,
                       t.FullName AS TrainerName, wp.DifficultyLevel,
                       wp.DurationWeeks, wp.Goal, wp.Description
                FROM WorkoutPrograms wp
                JOIN Trainers t ON wp.TrainerID = t.TrainerID
                ORDER BY wp.DifficultyLevel, wp.ProgramName";
    return Results.Ok(await db.QueryAsync<WorkoutProgram>(sql));
});

app.MapPost("/api/visits", async (CreateVisitRequest req) =>
{
    using var db = Db();
    var sql = @"INSERT INTO Visits (MemberID, TrainerID, ProgramID, VisitDate, VisitType, ResultNote)
                VALUES (@MemberID, @TrainerID, @ProgramID, @VisitDate, @VisitType, @ResultNote);
                SELECT LAST_INSERT_ID();";
    var newId = await db.ExecuteScalarAsync<long>(sql, new
    {
        req.MemberID, req.TrainerID, req.ProgramID,
        VisitDate = DateTime.Now,
        req.VisitType, req.ResultNote
    });
    return Results.Ok(new { success = true, visitID = newId, message = "Тренировка отмечена" });
});

// ════════════════════════════════════════════════════════════════════════════
//  ФОТО — Supabase Storage
// ════════════════════════════════════════════════════════════════════════════

var supabaseUrl    = "https://cgivukurkmlnqdrtkvop.supabase.co";
var supabaseKey    = "sb_secret_NSXh0Q9MNFWRjzLK1YNkAA_nAflcbXM";
var supabaseBucket = "photos";

app.MapPost("/api/photos/upload", async (HttpRequest request) =>
{
    try
    {
        if (!request.HasFormContentType)
            return Results.BadRequest("Нужен multipart/form-data");

        var form     = await request.ReadFormAsync();
        var file     = form.Files.GetFile("file");
        var filePath = form["file_path"].ToString();

        if (file == null || file.Length == 0)
            return Results.BadRequest("Файл не выбран");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var fileBytes = ms.ToArray();

        string ext  = Path.GetExtension(file.FileName).ToLower();
        string mime = ext == ".png" ? "image/png" : "image/jpeg";

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {supabaseKey}");
        httpClient.DefaultRequestHeaders.Add("apikey", supabaseKey);

        await httpClient.DeleteAsync(
            $"{supabaseUrl}/storage/v1/object/{supabaseBucket}/{filePath}");

        var byteContent = new ByteArrayContent(fileBytes);
        byteContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(mime);

        var response = await httpClient.PostAsync(
            $"{supabaseUrl}/storage/v1/object/{supabaseBucket}/{filePath}", byteContent);

        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            return Results.Problem($"Supabase error: {json}");

        string publicUrl =
            $"{supabaseUrl}/storage/v1/object/public/{supabaseBucket}/{filePath}";
        return Results.Ok(new { url = publicUrl });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Ошибка: {ex.Message}");
    }
});

app.MapDelete("/api/photos/{**filePath}", async (string filePath) =>
{
    using var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {supabaseKey}");
    httpClient.DefaultRequestHeaders.Add("apikey", supabaseKey);

    var response = await httpClient.DeleteAsync(
        $"{supabaseUrl}/storage/v1/object/{supabaseBucket}/{filePath}");

    return response.IsSuccessStatusCode
        ? Results.Ok()
        : Results.Problem("Ошибка удаления фото");
});

// ════════════════════════════════════════════════════════════════════════════
//  HEALTH CHECK
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
        return Results.Problem($"Ошибка: {ex.Message}");
    }
});

app.Run();

// ════════════════════════════════════════════════════════════════════════════
//  ЗАПИСИ (record types)
// ════════════════════════════════════════════════════════════════════════════

record UpdateMemberRequest(string? Email, string? Address, string? FitnessGoal, string? Notes);
record UpdatePhotoRequest(string PhotoUrl);
record CreateVisitRequest(int MemberID, int TrainerID, int? ProgramID, string VisitType, string? ResultNote);

/// <summary>Тело запроса для верификации штрихкода.</summary>
record BarcodeVerifyRequest(string BarcodeValue);

/// <summary>Ответ на проверку штрихкода.</summary>
record BarcodeVerifyResponse
{
    public bool    Allowed    { get; init; }
    public string? MemberName { get; init; }
    public string? Photo      { get; init; }
    public string? PlanName   { get; init; }
    public string  Message    { get; init; } = "";
    public int     DaysLeft   { get; init; }
}

/// <summary>
/// Тело запроса для списания сессии тренером.
/// VisitDate — опционально, если null используется текущее время.
/// </summary>
record UseSessionRequest(
    int?      ProgramID,
    string?   ResultNote,
    DateTime? VisitDate,
    int?      DurationMinutes
);
