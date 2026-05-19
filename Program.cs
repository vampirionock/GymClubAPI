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

var connStr  = builder.Configuration.GetConnectionString("GymClubDB")!;
var photoRoot = builder.Configuration["PhotoPath"]
                ?? Path.Combine(AppContext.BaseDirectory, "photo");
MySqlConnection Db() => new MySqlConnection(connStr);

var cloudName = Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME") ?? "dvboll7as";
var apiKey    = Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY")    ?? "453577598156417";
var apiSecret = Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET") ?? "-DFCIRiHVyTkUpmj8YxjzrIVUWw";

// ── helper: SHA1 подпись ──────────────────────────────────────────────────
static string Sha1Hex(string input)
{
    using var sha = System.Security.Cryptography.SHA1.Create();
    var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

app.MapPost("/api/login", async (LoginRequest req) =>
{
    using var db = Db();
    var member = await db.QueryFirstOrDefaultAsync<Member>(
        "SELECT * FROM Members WHERE Phone = @Phone", new { req.Phone });
    if (member != null)
        return Results.Ok(new LoginResponse { Found=true, Role="member", Id=member.MemberID, FullName=member.FullName, Phone=member.Phone, Email=member.Email });
    var trainer = await db.QueryFirstOrDefaultAsync<Trainer>(
        "SELECT * FROM Trainers WHERE Phone = @Phone", new { req.Phone });
    if (trainer != null)
        return Results.Ok(new LoginResponse { Found=true, Role="trainer", Id=trainer.TrainerID, FullName=trainer.FullName, Phone=trainer.Phone, Email=trainer.Email });
    return Results.Ok(new LoginResponse { Found = false });
});

app.MapGet("/api/members/{id:int}", async (int id) =>
{
    using var db = Db();
    var m = await db.QueryFirstOrDefaultAsync<Member>("SELECT * FROM Members WHERE MemberID = @id", new { id });
    return m is null ? Results.NotFound() : Results.Ok(m);
});

app.MapGet("/api/members/{id:int}/memberships", async (int id) =>
{
    using var db = Db();
    var sql = "SELECT mm.MemberMembershipID,mm.MemberID,mm.PlanID,mp.PlanName,mp.Price,mp.DurationMonths,mm.StartDate,mm.EndDate,mm.Status,mm.Comment FROM MemberMemberships mm JOIN MembershipPlans mp ON mm.PlanID=mp.PlanID WHERE mm.MemberID=@id ORDER BY mm.StartDate DESC";
    return Results.Ok(await db.QueryAsync<Membership>(sql, new { id }));
});

app.MapGet("/api/members/{id:int}/visits", async (int id, int limit = 20) =>
{
    using var db = Db();
    var sql = "SELECT v.VisitID,v.MemberID,m.FullName AS MemberName,v.TrainerID,t.FullName AS TrainerName,v.ProgramID,wp.ProgramName,v.VisitDate,v.VisitType,v.ResultNote FROM Visits v JOIN Members m ON v.MemberID=m.MemberID LEFT JOIN Trainers t ON v.TrainerID=t.TrainerID LEFT JOIN WorkoutPrograms wp ON v.ProgramID=wp.ProgramID WHERE v.MemberID=@id ORDER BY v.VisitDate DESC LIMIT @limit";
    return Results.Ok(await db.QueryAsync<Visit>(sql, new { id, limit }));
});

app.MapGet("/api/trainers/{id:int}", async (int id) =>
{
    using var db = Db();
    var t = await db.QueryFirstOrDefaultAsync<Trainer>("SELECT * FROM Trainers WHERE TrainerID = @id", new { id });
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
    var sql = "SELECT v.VisitID,v.MemberID,m.FullName AS MemberName,v.TrainerID,t.FullName AS TrainerName,v.ProgramID,wp.ProgramName,v.VisitDate,v.VisitType,v.ResultNote FROM Visits v JOIN Members m ON v.MemberID=m.MemberID LEFT JOIN Trainers t ON v.TrainerID=t.TrainerID LEFT JOIN WorkoutPrograms wp ON v.ProgramID=wp.ProgramID WHERE v.TrainerID=@id ORDER BY v.VisitDate DESC LIMIT @limit";
    return Results.Ok(await db.QueryAsync<Visit>(sql, new { id, limit }));
});

app.MapGet("/api/trainers/{id:int}/programs", async (int id) =>
{
    using var db = Db();
    var sql = "SELECT wp.ProgramID,wp.ProgramName,wp.TrainerID,t.FullName AS TrainerName,wp.DifficultyLevel,wp.DurationWeeks,wp.Goal,wp.Description FROM WorkoutPrograms wp JOIN Trainers t ON wp.TrainerID=t.TrainerID WHERE wp.TrainerID=@id ORDER BY wp.ProgramName";
    return Results.Ok(await db.QueryAsync<WorkoutProgram>(sql, new { id }));
});

app.MapGet("/api/plans", async () =>
{
    using var db = Db();
    return Results.Ok(await db.QueryAsync<MembershipPlan>("SELECT * FROM MembershipPlans ORDER BY Price"));
});

app.MapGet("/api/programs", async () =>
{
    using var db = Db();
    var sql = "SELECT wp.ProgramID,wp.ProgramName,wp.TrainerID,t.FullName AS TrainerName,wp.DifficultyLevel,wp.DurationWeeks,wp.Goal,wp.Description FROM WorkoutPrograms wp JOIN Trainers t ON wp.TrainerID=t.TrainerID ORDER BY wp.DifficultyLevel,wp.ProgramName";
    return Results.Ok(await db.QueryAsync<WorkoutProgram>(sql));
});

app.MapPut("/api/members/{id:int}", async (int id, UpdateMemberRequest req) =>
{
    using var db = Db();
    var rows = await db.ExecuteAsync("UPDATE Members SET Email=@Email,Address=@Address,FitnessGoal=@FitnessGoal,Notes=@Notes WHERE MemberID=@id", new { req.Email, req.Address, req.FitnessGoal, req.Notes, id });
    return rows > 0 ? Results.Ok(new { success=true, message="Профиль обновлён" }) : Results.NotFound(new { success=false });
});

app.MapGet("/api/trainers/{id:int}/members", async (int id) =>
{
    using var db = Db();
    var sql = "SELECT m.MemberID,m.FullName,m.Phone,m.Email,m.Gender,m.FitnessGoal,COUNT(v.VisitID) AS TotalVisits FROM Members m JOIN Visits v ON m.MemberID=v.MemberID WHERE v.TrainerID=@id GROUP BY m.MemberID,m.FullName,m.Phone,m.Email,m.Gender,m.FitnessGoal ORDER BY m.FullName";
    return Results.Ok(await db.QueryAsync(sql, new { id }));
});

app.MapGet("/api/trainers/{id:int}/clients", async (int id) =>
{
    using var db = Db();
    var sql = "SELECT m.MemberID,m.FullName,m.Phone,m.Photo,m.FitnessGoal,mm.Status AS MembershipStatus,mp.PlanName,mp.VisitLimit,mm.EndDate,(SELECT COUNT(*) FROM Visits v2 WHERE v2.MemberID=m.MemberID AND v2.TrainerID=@id AND mm.StartDate IS NOT NULL AND v2.VisitDate>=mm.StartDate AND v2.VisitDate<=IFNULL(mm.EndDate,NOW())) AS UsedVisits FROM Members m JOIN Visits v ON m.MemberID=v.MemberID AND v.TrainerID=@id LEFT JOIN MemberMemberships mm ON mm.MemberID=m.MemberID AND mm.Status='Активен' AND mm.EndDate>=CURDATE() LEFT JOIN MembershipPlans mp ON mm.PlanID=mp.PlanID GROUP BY m.MemberID,m.FullName,m.Phone,m.Photo,m.FitnessGoal,mm.Status,mp.PlanName,mp.VisitLimit,mm.EndDate,mm.StartDate ORDER BY m.FullName";
    return Results.Ok(await db.QueryAsync(sql, new { id }));
});

app.MapPost("/api/visits", async (CreateVisitRequest req) =>
{
    using var db = Db();
    var sql = "INSERT INTO Visits (MemberID,TrainerID,ProgramID,VisitDate,VisitType,ResultNote) VALUES (@MemberID,@TrainerID,@ProgramID,@VisitDate,@VisitType,@ResultNote); SELECT LAST_INSERT_ID();";
    var newId = await db.ExecuteScalarAsync<long>(sql, new { req.MemberID, req.TrainerID, req.ProgramID, VisitDate=DateTime.Now, req.VisitType, req.ResultNote });
    return Results.Ok(new { success=true, visitID=newId, message="Тренировка отмечена" });
});

app.MapGet("/api/members/{id:int}/trainer", async (int id) =>
{
    using var db = Db();
    var sql = "SELECT t.TrainerID,t.FullName,t.Phone,t.Email,t.Specialization,t.ExperienceYears,t.Bio,t.WorkSchedule,t.Photo,COUNT(v.VisitID) AS SessionCount FROM Visits v JOIN Trainers t ON v.TrainerID=t.TrainerID WHERE v.MemberID=@id AND v.TrainerID IS NOT NULL GROUP BY t.TrainerID,t.FullName,t.Phone,t.Email,t.Specialization,t.ExperienceYears,t.Bio,t.WorkSchedule,t.Photo ORDER BY COUNT(v.VisitID) DESC LIMIT 1";
    var trainer = await db.QueryFirstOrDefaultAsync(sql, new { id });
    return trainer is null ? Results.Ok(new { found=false }) : Results.Ok(new { found=true, trainer });
});

app.MapGet("/api/health", async () =>
{
    try { using var db = Db(); await db.OpenAsync(); return Results.Ok(new { status="ok", message="API работает, БД подключена (Railway MySQL)", time=DateTime.Now }); }
    catch (Exception ex) { return Results.Problem($"Ошибка: {ex.Message}"); }
});

// ════════════════════════════════════════════════════════════════════════════
//  ЗАГРУЗКА ФОТО В CLOUDINARY (signed upload)
//  POST /api/photos/upload
// ════════════════════════════════════════════════════════════════════════════
app.MapPost("/api/photos/upload", async (HttpRequest request) =>
{
    try
    {
        if (!request.HasFormContentType)
            return Results.BadRequest("Нужен multipart/form-data");

        var form     = await request.ReadFormAsync();
        var file     = form.Files.GetFile("file");
        var publicId = form["public_id"].ToString(); // "move/visitors/ivan_petrov"

        if (file == null || file.Length == 0)
            return Results.BadRequest("Файл не выбран");

        var timestamp  = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var presetName = "movex_upload";

        // Cloudinary signed upload: параметры строго в алфавитном порядке
        // public_id < timestamp < upload_preset
        var toSign = string.IsNullOrEmpty(publicId)
            ? $"timestamp={timestamp}&upload_preset={presetName}{apiSecret}"
            : $"public_id={publicId}&timestamp={timestamp}&upload_preset={presetName}{apiSecret}";

        var signature = Sha1Hex(toSign);

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;

        using var httpClient = new HttpClient();
        httpClient.Timeout   = TimeSpan.FromSeconds(60);

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(ms),              "file",          file.FileName);
        content.Add(new StringContent(apiKey),          "api_key");
        content.Add(new StringContent(timestamp),       "timestamp");
        content.Add(new StringContent(signature),       "signature");
        content.Add(new StringContent(presetName),      "upload_preset");
        if (!string.IsNullOrEmpty(publicId))
            content.Add(new StringContent(publicId),    "public_id");

        var response = await httpClient.PostAsync(
            $"https://api.cloudinary.com/v1_1/{cloudName}/image/upload", content);

        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return Results.Problem($"Cloudinary error: {json}");

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var url = doc.RootElement.GetProperty("secure_url").GetString();
        var pid = doc.RootElement.GetProperty("public_id").GetString();

        return Results.Ok(new { url, public_id = pid });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Ошибка: {ex.Message}\n{ex.StackTrace}");
    }
});

// ════════════════════════════════════════════════════════════════════════════
//  УДАЛЕНИЕ ФОТО ИЗ CLOUDINARY
//  DELETE /api/photos/{**publicId}
// ════════════════════════════════════════════════════════════════════════════
app.MapDelete("/api/photos/{**publicId}", async (string publicId) =>
{
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
    var toSign    = $"public_id={publicId}&timestamp={timestamp}{apiSecret}";
    var signature = Sha1Hex(toSign);

    using var httpClient = new HttpClient();
    using var content    = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["public_id"] = publicId,
        ["api_key"]   = apiKey,
        ["timestamp"] = timestamp,
        ["signature"] = signature
    });

    var response = await httpClient.PostAsync(
        $"https://api.cloudinary.com/v1_1/{cloudName}/image/destroy", content);

    return response.IsSuccessStatusCode ? Results.Ok() : Results.Problem("Ошибка удаления фото");
});

app.Run();

record UpdateMemberRequest(string? Email, string? Address, string? FitnessGoal, string? Notes);
record CreateVisitRequest(int MemberID, int TrainerID, int? ProgramID, string VisitType, string? ResultNote);
