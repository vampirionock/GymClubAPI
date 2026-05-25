namespace GymClubAPI.Models;

// ── Авторизация ──────────────────────────────────────────────────────────────

public class LoginRequest
{
    public string Phone { get; set; } = "";
}

public class LoginResponse
{
    public bool    Found      { get; set; }
    public string  Role       { get; set; } = ""; // "member" | "trainer"
    public int     Id         { get; set; }
    public string  FullName   { get; set; } = "";
    public string  Phone      { get; set; } = "";
    public string? Email      { get; set; }
    public string? Photo      { get; set; }
    /// <summary>Штрихкод участника (только для Role == "member").</summary>
    public string? Barcode    { get; set; }
}

// ── Участник ─────────────────────────────────────────────────────────────────

public class Member
{
    public int      MemberID          { get; set; }
    public string   FullName          { get; set; } = "";
    public string   Phone             { get; set; } = "";
    public string?  Email             { get; set; }
    public DateTime DateOfBirth       { get; set; }
    public string   Gender            { get; set; } = "";
    public string?  Address           { get; set; }
    public DateTime RegistrationDate  { get; set; }
    public string?  FitnessGoal       { get; set; }
    public string?  Notes             { get; set; }
    public string?  Photo             { get; set; }
    /// <summary>Уникальный штрихкод, формат GYM-XXXXX.</summary>
    public string?  BarcodeValue      { get; set; }
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
    public string? Photo            { get; set; }
}

// ── Абонемент ─────────────────────────────────────────────────────────────────

public class Membership
{
    public int      MemberMembershipID { get; set; }
    public int      MemberID           { get; set; }
    public int      PlanID             { get; set; }
    public string   PlanName           { get; set; } = "";
    public decimal  Price              { get; set; }
    public int      DurationMonths     { get; set; }
    /// <summary>ID тренера, с которым связан абонемент. NULL = клубный без тренера.</summary>
    public int?     TrainerID          { get; set; }
    /// <summary>Количество тренировок с тренером, проведённых по этому абонементу.</summary>
    public int      SessionsUsed       { get; set; }
    public DateTime StartDate          { get; set; }
    public DateTime EndDate            { get; set; }
    public string   Status             { get; set; } = "";
    public string?  Comment            { get; set; }
}

// ── Тарифный план ────────────────────────────────────────────────────────────

public class MembershipPlan
{
    public int     PlanID         { get; set; }
    public string  PlanName       { get; set; } = "";
    public int     DurationMonths { get; set; }
    public decimal Price          { get; set; }
    /// <summary>Лимит посещений/сессий по плану. NULL = безлимит.</summary>
    public int?    VisitLimit     { get; set; }
    public string? Description    { get; set; }
}

// ── Посещение ────────────────────────────────────────────────────────────────

public class Visit
{
    public int      VisitID      { get; set; }
    public int      MemberID     { get; set; }
    public string   MemberName   { get; set; } = "";
    /// <summary>Фото участника (из таблицы Members).</summary>
    public string?  Photo        { get; set; }
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

// ── Клиент тренера (расширенный) ──────────────────────────────────────────────

/// <summary>
/// Клиент в списке у тренера — включает данные абонемента с тренером и счётчик сессий.
/// </summary>
public class TrainerClient
{
    public int     MemberID           { get; set; }
    public string  FullName           { get; set; } = "";
    public string  Phone              { get; set; } = "";
    public string? Photo              { get; set; }
    public string? FitnessGoal        { get; set; }

    // Абонемент
    public int?    MemberMembershipID { get; set; }
    public string? PlanName           { get; set; }
    public string? MembershipStatus   { get; set; }
    public DateTime? EndDate          { get; set; }

    // Сессии
    /// <summary>Лимит сессий по абонементу. NULL = безлимит.</summary>
    public int?    SessionLimit       { get; set; }
    /// <summary>Использовано сессий по этому абонементу.</summary>
    public int     SessionsUsed       { get; set; }
    /// <summary>Осталось сессий. NULL если безлимит.</summary>
    public int?    SessionsLeft       => SessionLimit.HasValue
                                        ? Math.Max(0, SessionLimit.Value - SessionsUsed)
                                        : null;
    /// <summary>Все сессии исчерпаны (только когда есть лимит).</summary>
    public bool    IsSessionsExhausted => SessionLimit.HasValue && SessionsUsed >= SessionLimit.Value;
}

// ── API ответ-обёртка ────────────────────────────────────────────────────────

public class ApiResult<T>
{
    public bool   Success { get; set; }
    public string Message { get; set; } = "";
    public T?     Data    { get; set; }
}
