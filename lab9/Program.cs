using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DomainModelDemo
{
    // ==================== МОДЕЛІ ПРЕДМЕТНОЇ ОБЛАСТІ ====================

    public interface IEntity
    {
        int Id { get; }
    }

    public abstract class BaseEntity : IEntity
    {
        public int Id { get; set; }
    }

    public class Course : BaseEntity
    {
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int Hours { get; set; }
        public int MaxStudents { get; set; }
    }

    public class Student : BaseEntity
    {
        public string FullName { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public double AverageGrade { get; set; }
    }

    public class Enrollment : BaseEntity
    {
        public int CourseId { get; set; }
        public int StudentId { get; set; }
        public DateTime EnrolledAtUtc { get; set; } = DateTime.UtcNow;
    }

    // ==================== ВАЛІДАЦІЯ ====================

    public interface IValidator<in T>
    {
        void Validate(T entity);
    }

    public class CourseValidator : IValidator<Course>
    {
        public void Validate(Course course)
        {
            if (string.IsNullOrWhiteSpace(course.Title))
                throw new ArgumentException("Назва курсу не може бути порожньою.");
            if (course.Hours <= 0)
                throw new ArgumentOutOfRangeException(nameof(course.Hours), "Кількість годин має бути більшою за 0.");
            if (course.MaxStudents <= 0)
                throw new ArgumentOutOfRangeException(nameof(course.MaxStudents), "Ліміт місць має бути більшим за 0.");
        }
    }

    public class StudentValidator : IValidator<Student>
    {
        public void Validate(Student student)
        {
            if (string.IsNullOrWhiteSpace(student.FullName))
                throw new ArgumentException("ПІБ студента не може бути порожнім.");
            if (student.AverageGrade < 0 || student.AverageGrade > 100)
                throw new ArgumentOutOfRangeException(nameof(student.AverageGrade), "Середній бал має бути в межах 0–100.");
        }
    }

    // ==================== ПОДІЇ ТА АСИНХРОННЕ ЛОГУВАННЯ ====================

    public sealed class DomainActivityEventArgs : EventArgs
    {
        public string OperationName { get; }
        public string Details { get; }
        public DateTime Timestamp { get; } = DateTime.Now;

        public DomainActivityEventArgs(string operationName, string details)
        {
            OperationName = operationName;
            Details = details;
        }
    }

    public class FileAuditLogger : IDisposable
    {
        private readonly string _logFilePath;
        private readonly SemaphoreSlim _fileGate = new(1, 1);

        public FileAuditLogger(string logFilePath)
        {
            _logFilePath = Path.GetFullPath(logFilePath);
            string? dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        public async Task LogAsync(string level, string message, CancellationToken cancellationToken = default)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";

            await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await File.AppendAllTextAsync(_logFilePath, line, Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _fileGate.Release();
            }
        }

        public void Dispose() => _fileGate.Dispose();
    }

    // ==================== АСИНХРОННИЙ ФАЙЛОВИЙ РЕПОЗИТОРІЙ ====================

    public interface IAsyncRepository<T> where T : class, IEntity
    {
        IReadOnlyList<T> Items { get; }
        Task LoadAsync(CancellationToken cancellationToken = default);
        Task AddAndSaveAsync(T entity, CancellationToken cancellationToken = default);
        T? GetById(int id);
    }

    public class JsonFileRepository<T> : IAsyncRepository<T> where T : class, IEntity
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly string _storagePath;
        private readonly List<T> _cache = new();
        private readonly SemaphoreSlim _ioLock = new(1, 1);

        public IReadOnlyList<T> Items => _cache.AsReadOnly();

        public JsonFileRepository(string storagePath)
        {
            _storagePath = Path.GetFullPath(storagePath);
            string? dir = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        public async Task LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _cache.Clear();
                if (!File.Exists(_storagePath))
                    return;

                string json = await File.ReadAllTextAsync(_storagePath, Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(json))
                {
                    var items = JsonSerializer.Deserialize<List<T>>(json, JsonOptions);
                    if (items is not null)
                        _cache.AddRange(items);
                }
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task AddAndSaveAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity is null)
                throw new ArgumentNullException(nameof(entity));

            cancellationToken.ThrowIfCancellationRequested();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_cache.Any(x => x.Id == entity.Id))
                    throw new InvalidOperationException($"Сутність типу {typeof(T).Name} із ID={entity.Id} вже існує.");

                _cache.Add(entity);

                string json = JsonSerializer.Serialize(_cache, JsonOptions);
                await File.WriteAllTextAsync(_storagePath, json, Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public T? GetById(int id) => _cache.FirstOrDefault(x => x.Id == id);
    }

    // ==================== АСИНХРОННИЙ СЕРВІС ЗВІТНОСТІ (LINQ + FILE I/O) ====================

    public class AsyncReportService
    {
        public async Task<string> ExportAnalyticsReportAsync(
            IReadOnlyList<Course> courses,
            IReadOnlyList<Student> students,
            IReadOnlyList<Enrollment> enrollments,
            string reportFilePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Імітація тривалої агрегації даних перед формуванням документа
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);

            var sb = new StringBuilder();
            sb.AppendLine("=== АНАЛІТИЧНИЙ ЗВІТ НАВЧАЛЬНОЇ ПЛАТФОРМИ ===");
            sb.AppendLine($"Сформовано: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Усього курсів: {courses.Count} | Студентів: {students.Count} | Записів: {enrollments.Count}");

            if (students.Any())
                sb.AppendLine($"Середній бал студентів: {students.Average(s => s.AverageGrade):F2}");

            sb.AppendLine("\nСтатистика заповненості курсів:");
            var courseSummaries = courses
                .Select(c => new
                {
                    c.Title,
                    c.Category,
                    Enrolled = enrollments.Count(e => e.CourseId == c.Id),
                    c.MaxStudents
                })
                .OrderByDescending(x => x.Enrolled);

            foreach (var item in courseSummaries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine($"  - [{item.Category}] \"{item.Title}\": {item.Enrolled}/{item.MaxStudents} місць");
            }

            string fullPath = Path.GetFullPath(reportFilePath);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(fullPath, sb.ToString(), Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);

            return fullPath;
        }
    }

    // ==================== ГОЛОВНИЙ БІЗНЕС-СЕРВІС ПЛАТФОРМИ ====================

    public class EducationPlatformService
    {
        private readonly IAsyncRepository<Course> _courseRepo;
        private readonly IAsyncRepository<Student> _studentRepo;
        private readonly IAsyncRepository<Enrollment> _enrollmentRepo;
        private readonly IValidator<Course> _courseValidator;
        private readonly IValidator<Student> _studentValidator;
        private readonly FileAuditLogger _logger;
        private readonly AsyncReportService _reportService;

        public event EventHandler<DomainActivityEventArgs>? ActivityOccurred;

        public EducationPlatformService(
            IAsyncRepository<Course> courseRepo,
            IAsyncRepository<Student> studentRepo,
            IAsyncRepository<Enrollment> enrollmentRepo,
            IValidator<Course> courseValidator,
            IValidator<Student> studentValidator,
            FileAuditLogger logger,
            AsyncReportService reportService)
        {
            _courseRepo = courseRepo;
            _studentRepo = studentRepo;
            _enrollmentRepo = enrollmentRepo;
            _courseValidator = courseValidator;
            _studentValidator = studentValidator;
            _logger = logger;
            _reportService = reportService;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await Task.WhenAll(
                _courseRepo.LoadAsync(cancellationToken),
                _studentRepo.LoadAsync(cancellationToken),
                _enrollmentRepo.LoadAsync(cancellationToken)).ConfigureAwait(false);

            await _logger.LogAsync("INFO", "Сховища даних успішно завантажено асинхронно.", cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Course> CreateCourseAsync(Course course, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _courseValidator.Validate(course);

                await _courseRepo.AddAndSaveAsync(course, cancellationToken).ConfigureAwait(false);
                await _logger.LogAsync("INFO", $"Створено курс \"{course.Title}\" (ID={course.Id}).", cancellationToken)
                    .ConfigureAwait(false);

                ActivityOccurred?.Invoke(this, new DomainActivityEventArgs("CreateCourse", $"Курс \"{course.Title}\" збережено."));
                return course;
            }
            catch (OperationCanceledException)
            {
                await _logger.LogAsync("WARN", $"Створення курсу \"{course.Title}\" було скасовано.", CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await _logger.LogAsync("ERROR", $"Помилка створення курсу: {ex.Message}", CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
        }

        public async Task<Student> RegisterStudentAsync(Student student, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _studentValidator.Validate(student);

            await _studentRepo.AddAndSaveAsync(student, cancellationToken).ConfigureAwait(false);
            await _logger.LogAsync("INFO", $"Зареєстровано студента {student.FullName} (ID={student.Id}).", cancellationToken)
                .ConfigureAwait(false);

            ActivityOccurred?.Invoke(this, new DomainActivityEventArgs("RegisterStudent", $"Студент {student.FullName} доданий."));
            return student;
        }

        public async Task<Enrollment> EnrollStudentAsync(int courseId, int studentId, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var course = _courseRepo.GetById(courseId)
                    ?? throw new KeyNotFoundException($"Курс ID={courseId} не знайдено.");
                var student = _studentRepo.GetById(studentId)
                    ?? throw new KeyNotFoundException($"Студента ID={studentId} не знайдено.");

                int currentCount = _enrollmentRepo.Items.Count(e => e.CourseId == courseId);
                if (currentCount >= course.MaxStudents)
                    throw new InvalidOperationException($"На курсі \"{course.Title}\" немає вільних місць.");

                if (_enrollmentRepo.Items.Any(e => e.CourseId == courseId && e.StudentId == studentId))
                    throw new InvalidOperationException($"Студент {student.FullName} уже зарахований на курс.");

                // Імітація асинхронної перевірки квоти в зовнішньому модулі
                await Task.Delay(80, cancellationToken).ConfigureAwait(false);

                int nextId = _enrollmentRepo.Items.Any() ? _enrollmentRepo.Items.Max(e => e.Id) + 1 : 1;
                var enrollment = new Enrollment { Id = nextId, CourseId = courseId, StudentId = studentId };

                await _enrollmentRepo.AddAndSaveAsync(enrollment, cancellationToken).ConfigureAwait(false);
                await _logger.LogAsync("INFO", $"Студента {student.FullName} зараховано на \"{course.Title}\".", cancellationToken)
                    .ConfigureAwait(false);

                ActivityOccurred?.Invoke(this, new DomainActivityEventArgs("EnrollStudent", $"{student.FullName} -> \"{course.Title}\""));
                return enrollment;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await _logger.LogAsync("ERROR", $"Помилка зарахування (Course={courseId}, Student={studentId}): {ex.Message}", CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
        }

        public async Task<string> BuildAndSaveReportAsync(string reportPath, CancellationToken cancellationToken = default)
        {
            string savedFile = await _reportService.ExportAnalyticsReportAsync(
                _courseRepo.Items,
                _studentRepo.Items,
                _enrollmentRepo.Items,
                reportPath,
                cancellationToken).ConfigureAwait(false);

            await _logger.LogAsync("INFO", $"Аналітичний звіт експортовано у {savedFile}.", cancellationToken)
                .ConfigureAwait(false);

            ActivityOccurred?.Invoke(this, new DomainActivityEventArgs("ExportReport", $"Звіт збережено: {Path.GetFileName(savedFile)}"));
            return savedFile;
        }
    }

    // ==================== ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ ====================

    internal class Program
    {
        static async Task Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            string baseDir = Path.Combine("AsyncLabData", Guid.NewGuid().ToString("N")[..8]);
            using var logger = new FileAuditLogger(Path.Combine(baseDir, "audit.log"));

            var service = new EducationPlatformService(
                new JsonFileRepository<Course>(Path.Combine(baseDir, "courses.json")),
                new JsonFileRepository<Student>(Path.Combine(baseDir, "students.json")),
                new JsonFileRepository<Enrollment>(Path.Combine(baseDir, "enrollments.json")),
                new CourseValidator(),
                new StudentValidator(),
                logger,
                new AsyncReportService());

            // Підписка на події доменної моделі
            service.ActivityOccurred += (_, e) =>
                Console.WriteLine($"  [Подія | {e.Timestamp:HH:mm:ss}] {e.OperationName}: {e.Details}");

            Console.WriteLine("=== 1. АСИНХРОННА ІНІЦІАЛІЗАЦІЯ ТА ВИКОНАННЯ БІЗНЕС-ОПЕРАЦІЙ ===\n");
            await service.InitializeAsync();

            var c1 = await service.CreateCourseAsync(new Course { Id = 1, Title = "Асинхронне програмування в C#", Category = "Programming", Hours = 60, MaxStudents = 2 });
            var c2 = await service.CreateCourseAsync(new Course { Id = 2, Title = "Хмарна архітектура .NET", Category = "Architecture", Hours = 45, MaxStudents = 5 });

            var s1 = await service.RegisterStudentAsync(new Student { Id = 101, FullName = "Олександр Ковальчук", GroupName = "ІПЗ-21", AverageGrade = 94.0 });
            var s2 = await service.RegisterStudentAsync(new Student { Id = 102, FullName = "Марія Ткаченко", GroupName = "КН-22", AverageGrade = 89.5 });
            var s3 = await service.RegisterStudentAsync(new Student { Id = 103, FullName = "Дмитро Бондаренко", GroupName = "ІПЗ-21", AverageGrade = 82.0 });

            await service.EnrollStudentAsync(c1.Id, s1.Id);
            await service.EnrollStudentAsync(c1.Id, s2.Id);
            await service.EnrollStudentAsync(c2.Id, s3.Id);

            // 2. Перевірка обробки виняткових ситуацій в async-методах (валідація та бізнес-ліміт)
            Console.WriteLine("\n=== 2. ПЕРЕВІРКА ОБРОБКИ ВИНЯТКІВ У ASYNC-МЕТОДАХ ===\n");
            try
            {
                // Спроба перевищити ліміт місць на курсі c1 (MaxStudents = 2)
                await service.EnrollStudentAsync(c1.Id, s3.Id);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"  [Перехоплено бізнес-помилку]: {ex.Message}");
            }

            try
            {
                // Спроба створити невалідний курс
                await service.CreateCourseAsync(new Course { Id = 3, Title = "", Hours = -5, MaxStudents = 10 });
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"  [Перехоплено помилку валідації]: {ex.Message}");
            }

            // 3. Асинхронне формування звіту
            Console.WriteLine("\n=== 3. АСИНХРОННЕ ФОРМУВАННЯ АНАЛІТИЧНОГО ЗВІТУ ===\n");
            string reportFile = await service.BuildAndSaveReportAsync(Path.Combine(baseDir, "final_report.txt"));
            Console.WriteLine("\nВміст згенерованого файлу звіту:");
            Console.WriteLine(await File.ReadAllTextAsync(reportFile, Encoding.UTF8));

            // 4. Демонстрація коректного скасування через CancellationToken
            Console.WriteLine("=== 4. ПЕРЕВІРКА СКАСУВАННЯ ЧЕРЕЗ CANCELLATIONTOKEN ===\n");
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Імітуємо скасування операції користувачем

            try
            {
                await service.BuildAndSaveReportAsync(Path.Combine(baseDir, "canceled_report.txt"), cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("  [Скасовано]: Побудову звіту коректно перервано через CancellationToken без аварійного завершення.");
            }
        }
    }
}