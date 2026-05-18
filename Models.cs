namespace GymClubAPI.Models;

// ── Авторизация ──────────────────────────────────────────────────────────────

public class LoginRequest
{
    public string Phone { get; set; } = "";
}

public class LoginResponse
{
    public bool Found       { get; set; }
    public string Role      { get; set; } = ""; // "member" | "trainer"
    public int    Id        { get; set; }
    public string FullName  { get; set; } = "";
    public string Phone     { get; set; } = "";
    public string? Email    { get; set; }
}

// ── Участник ─────────────────────────────────────────────────────────────────

public class Member
{
    public int     MemberID         { get; set; }
    public string  FullName         { get; set; } = "";
    public string  Phone            { get; set; } = "";
    public string? Email            { get; set; }
    public DateTime DateOfBirth     { get; set; }
    public string  Gender           { get; set; } = "";
    public string? Address          { get; set; }
    public DateTime RegistrationDate{ get; set; }
    public string? FitnessGoal      { get; set; }
    public string? Notes            { get; set; }
    public string? Photo            { get; set; }  // имя файла фото
}

// ── Тренер ───────────────────────────────────────────────────────────────────

public class Trainer
{
    public int     TrainerID        { get; set; }
    public string  FullName         { get; set; } = "";
    public string  Phone            { get; set; } = "";
    public string? Email            { get; set; }
    public string  Specialization   { get; set; } = "";
    public int     ExperienceYears  { get; set; }
    public string? Bio              { get; set; }
    public string? WorkSchedule     { get; set; }
    public string? Photo            { get; set; }  // имя файла фото
}

// ── Абонемент ─────────────────────────────────────────────────────────────────

public class Membership
{
    public int     MemberMembershipID { get; set; }
    public int     MemberID           { get; set; }
    public int     PlanID             { get; set; }
    public string  PlanName           { get; set; } = "";
    public decimal Price              { get; set; }
    public int     DurationMonths     { get; set; }
    public DateTime StartDate         { get; set; }
    public DateTime EndDate           { get; set; }
    public string  Status             { get; set; } = "";
    public string? Comment            { get; set; }
}

// ── Тарифный план ────────────────────────────────────────────────────────────

public class MembershipPlan
{
    public int     PlanID         { get; set; }
    public string  PlanName       { get; set; } = "";
    public int     DurationMonths { get; set; }
    public decimal Price          { get; set; }
    public int?    VisitLimit     { get; set; }
    public string? Description    { get; set; }
}

// ── Посещение ────────────────────────────────────────────────────────────────

public class Visit
{
    public int      VisitID      { get; set; }
    public int      MemberID     { get; set; }
    public string   MemberName   { get; set; } = "";
    public int?     TrainerID    { get; set; }
    public string?  TrainerName  { get; set; }
    public int?     ProgramID    { get; set; }
    public string?  ProgramName  { get; set; }
    public DateTime VisitDate    { get; set; }
    public string   VisitType    { get; set; } = "";
    public string?  ResultNote   { get; set; }
}

// ── Программа тренировок ─────────────────────────────────────────────────────

public class WorkoutProgram
{
    public int     ProgramID       { get; set; }
    public string  ProgramName     { get; set; } = "";
    public int     TrainerID       { get; set; }
    public string  TrainerName     { get; set; } = "";
    public string  DifficultyLevel { get; set; } = "";
    public int     DurationWeeks   { get; set; }
    public string  Goal            { get; set; } = "";
    public string? Description     { get; set; }
}

// ── API ответ-обёртка ────────────────────────────────────────────────────────

public class ApiResult<T>
{
    public bool   Success { get; set; }
    public string Message { get; set; } = "";
    public T?     Data    { get; set; }
}
