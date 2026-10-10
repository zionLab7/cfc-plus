namespace CfcPilot;

public class State
{
    public bool RealData { get; set; }
    public Dictionary<string, object> Statistics { get; set; } = [];
    public List<Unit> Units { get; set; } = [];
    public List<Student> Students { get; set; } = [];
    public List<Package> Packages { get; set; } = [];
    public List<Enrollment> Enrollments { get; set; } = [];
    public List<Lesson> Lessons { get; set; } = [];
    public List<Exam> Exams { get; set; } = [];
    public List<Entry> Entries { get; set; } = [];
    public List<Installment> Installments { get; set; } = [];
    public List<Instructor> Instructors { get; set; } = [];
    public List<Vehicle> Vehicles { get; set; } = [];
    public List<Lead> Leads { get; set; } = [];
    public List<PendingTask> Tasks { get; set; } = [];
    public List<WaitItem> Waitlist { get; set; } = [];
    public List<Template> Templates { get; set; } = [];
    public List<Document> Documents { get; set; } = [];
    public List<Message> Messages { get; set; } = [];
    public List<Audit> Audit { get; set; } = [];
    public List<BrowserProfile> BrowserProfiles { get; set; } = [];
    public List<User> Users { get; set; } = [];
    public Dictionary<string,long> ReceiptCounters { get; set; } = [];
    public Dictionary<string, Processed> Processed { get; set; } = [];
    public Policy Policy { get; set; } = new();
    public long Revision { get; set; }
}
public class LegacyEntity { public string SourceTable { get; set; } = ""; public string SourceId { get; set; } = ""; public bool Imported { get; set; } }
public class Unit : LegacyEntity { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Address { get; set; } = ""; public string Cfc { get; set; } = ""; }
public class Student
{
    public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Cpf { get; set; } = "";
    public string Rg { get; set; } = ""; public string RgIssued { get; set; } = ""; public string Birth { get; set; } = "";
    public string Email { get; set; } = ""; public string Phone { get; set; } = ""; public string Address { get; set; } = "";
    public string Mother { get; set; } = ""; public string Father { get; set; } = ""; public string Nationality { get; set; } = "Brasileira";
    public string Birthplace { get; set; } = ""; public string UnitId { get; set; } = ""; public string Renach { get; set; } = "";
    public string MedicalUntil { get; set; } = ""; public bool Ear { get; set; } public bool Ladv { get; set; }
    public string Notes { get; set; } = ""; public bool Blocked { get; set; }
    public string SourceId { get; set; } = ""; public bool Imported { get; set; } public string SourceTable { get; set; } = "";
    public List<string> LegacyUnitIds { get; set; } = []; public List<string> LegacySourceIds { get; set; } = []; public List<string> Phones { get; set; } = []; public string Process { get; set; } = ""; public string Category { get; set; } = "";
    public string SearchName => RelationalStore.SearchText(Name);
}
public class Package : LegacyEntity { public bool NeedsConfiguration { get; set; } public bool Active { get; set; } = true; public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Category { get; set; } = "B"; public string Service { get; set; } = "Primeira habilitação"; public int Lessons { get; set; } = 20; public decimal Price { get; set; } public int Parts { get; set; } = 4; }
public class Enrollment : LegacyEntity { public Dictionary<string,int> LessonLimits { get; set; } = []; public string Number { get; set; } = ""; public string Id { get; set; } = ""; public string StudentId { get; set; } = ""; public string PackageId { get; set; } = ""; public string UnitId { get; set; } = ""; public string Category { get; set; } = ""; public string Service { get; set; } = ""; public int LessonLimit { get; set; } public decimal Price { get; set; } public string Status { get; set; } = "Ativa"; public string Created { get; set; } = ""; }
public class Lesson : LegacyEntity { public bool ExcludedFromAgenda { get; set; } public string CanonicalLessonId { get; set; } = ""; public string AgendaCorrectionReason { get; set; } = ""; public string Category { get; set; } = ""; public bool AttendanceStart { get; set; } public bool AttendanceEnd { get; set; } public string SourceStatus { get; set; } = ""; public string Id { get; set; } = ""; public string StudentId { get; set; } = ""; public string EnrollmentId { get; set; } = ""; public string InstructorId { get; set; } = ""; public string VehicleId { get; set; } = ""; public string UnitId { get; set; } = ""; public string Start { get; set; } = ""; public int Minutes { get; set; } = 50; public string Status { get; set; } = "Agendada"; public bool Validated { get; set; } public string Notes { get; set; } = ""; }
public class Exam : LegacyEntity { public string Id { get; set; } = ""; public string StudentId { get; set; } = ""; public string EnrollmentId { get; set; } = ""; public string Type { get; set; } = "Prático"; public string Start { get; set; } = ""; public string Location { get; set; } = ""; public string Status { get; set; } = "Agendado"; public string Protocol { get; set; } = ""; public string UnitId { get; set; } = ""; public string InstructorId { get; set; } = ""; public string VehicleId { get; set; } = ""; }
public class Entry : LegacyEntity { public string Id { get; set; } = ""; public string StudentId { get; set; } = ""; public string EnrollmentId { get; set; } = ""; public string UnitId { get; set; } = ""; public string Kind { get; set; } = "Recebimento"; public decimal Amount { get; set; } public string Method { get; set; } = "PIX"; public string Description { get; set; } = ""; public string Date { get; set; } = ""; public string UserId { get; set; } = ""; public string Receipt { get; set; } = ""; public bool Closed { get; set; } public bool Voided { get; set; } public string Reverses { get; set; } = ""; public Dictionary<string, decimal> Allocations { get; set; } = []; }
public class Installment : LegacyEntity { public string Status { get; set; } = "Aberta"; public string Id { get; set; } = ""; public string EnrollmentId { get; set; } = ""; public string StudentId { get; set; } = ""; public string UnitId { get; set; } = ""; public string Due { get; set; } = ""; public decimal Amount { get; set; } public decimal Paid { get; set; } }
public class Instructor : LegacyEntity { public List<InstructorSlot> Slots { get; set; } = []; public List<InstructorBlock> Blocks { get; set; } = []; public string DefaultVehicleId { get; set; } = ""; public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Cpf { get; set; } = ""; public string Category { get; set; } = "B"; public string UnitId { get; set; } = ""; public string CnhUntil { get; set; } = ""; public string EcpfUntil { get; set; } = ""; public string WorkStart { get; set; } = "07:00"; public string WorkEnd { get; set; } = "19:00"; public string Days { get; set; } = "1,2,3,4,5,6"; public string BlockFrom { get; set; } = ""; public string BlockTo { get; set; } = ""; public decimal HourRate { get; set; } = 30; public bool Active { get; set; } = true; }
public class Vehicle : LegacyEntity { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Plate { get; set; } = ""; public string Category { get; set; } = "B"; public string UnitId { get; set; } = ""; public int Km { get; set; } public bool Pcd { get; set; } public string Status { get; set; } = "Disponível"; public decimal Fuel { get; set; } public decimal Maintenance { get; set; } }
public class Lead : LegacyEntity { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Phone { get; set; } = ""; public string Source { get; set; } = "WhatsApp"; public string Stage { get; set; } = "Novo contato"; public string UnitId { get; set; } = ""; public string Followup { get; set; } = ""; public string Notes { get; set; } = ""; public string StudentId { get; set; } = ""; }
public class PendingTask { public string Id { get; set; } = ""; public string Title { get; set; } = ""; public string UnitId { get; set; } = ""; public string Owner { get; set; } = ""; public string Due { get; set; } = ""; public bool Done { get; set; } }
public class WaitItem { public string Id { get; set; } = ""; public string StudentId { get; set; } = ""; public string UnitId { get; set; } = ""; public string Category { get; set; } = "B"; public string Availability { get; set; } = ""; public string Since { get; set; } = ""; }
public class Template { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Text { get; set; } = ""; }
public class Document : LegacyEntity { public string Id { get; set; } = ""; public string StudentId { get; set; } = ""; public string EnrollmentId { get; set; } = ""; public string Name { get; set; } = ""; public string Text { get; set; } = ""; public string Created { get; set; } = ""; }
public class Message { public string Id { get; set; } = ""; public string StudentId { get; set; } = ""; public string Phone { get; set; } = ""; public string Text { get; set; } = ""; public string Created { get; set; } = ""; public string Status { get; set; } = "Rascunho"; }
public class Audit { public string Id { get; set; } = ""; public string User { get; set; } = ""; public string Action { get; set; } = ""; public string Detail { get; set; } = ""; public string Date { get; set; } = ""; }
public class User : LegacyEntity { public string UnitId { get; set; } = ""; public string LegacyGroup { get; set; } = ""; public List<string> Permissions { get; set; } = []; public bool MustChangePassword { get; set; } public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Login { get; set; } = ""; public string PasswordHash { get; set; } = ""; public string Role { get; set; } = "Atendente"; public string LinkedId { get; set; } = ""; public bool Active { get; set; } = true; }
public class Policy
{
    public int CancelHours { get; set; } = 24; public int MaxDaily { get; set; } = 3; public int Minutes { get; set; } = 50;
    public string DebtMode { get; set; } = "Aviso"; public bool RequireExternalValidation { get; set; } = true;
    public string RetroactiveLessons { get; set; } = "Bloqueado"; public string RetroactiveExams { get; set; } = "Bloqueado"; public string RetroactiveEntries { get; set; } = "Bloqueado";
    public int MaxRetroactiveDays { get; set; } = 0; public bool RequireExceptionReason { get; set; } = true; public bool AllowFutureEntries { get; set; }
    public bool EnforceInstructorSchedule { get; set; } = true; public bool EnforceInstructorBlocks { get; set; } = true;
    public bool EnforceDailyLimit { get; set; } = true; public bool EnforceLessonBalance { get; set; } = true; public bool EnforceCategory { get; set; } = true;
    public bool EnforceStudentConflict { get; set; } = true; public bool EnforceInstructorConflict { get; set; } = true; public bool EnforceVehicleConflict { get; set; } = true;
    public bool RequireActiveEnrollment { get; set; } = true; public bool BlockBlockedStudents { get; set; } = true; public bool RequireAvailableResources { get; set; } = true; public bool CheckInstructorCnh { get; set; } = true;
    public bool RequireMedicalForExams { get; set; } = true; public bool RequireLadvForPractical { get; set; } = true; public bool RequireTheoryForPractical { get; set; } = true; public bool RequireLessonsForPractical { get; set; } = true;
    public bool AllowMultipleOpenExams { get; set; } public bool AllowHistoricalStatusEdits { get; set; }
}
public class Processed { public string Fingerprint { get; set; } = ""; public string Result { get; set; } = ""; }
public class BrowserProfile { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Kind { get; set; } = "Diretor"; public string InstructorId { get; set; } = ""; public string StudentId { get; set; } = ""; public string Created { get; set; } = ""; }
public class RuleException(string message, int status = 400) : Exception(message) { public int Status { get; } = status; }

public class InstructorSlot { public string Since { get; set; } = ""; public int Day { get; set; } public string Time { get; set; } = ""; }
public class InstructorBlock { public string Start { get; set; } = ""; public string End { get; set; } = ""; public string Reason { get; set; } = ""; }
