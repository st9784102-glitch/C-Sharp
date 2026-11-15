using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;

namespace DomainModelDemo
{
    // ==================== СТАТИЧНІ ІНФРАСТРУКТУРНІ МОДУЛІ ====================

    public static class IdGenerator
    {
        private static int _lastId = 0;
        public static int NextId() => Interlocked.Increment(ref _lastId);
    }

    public static class ValidationHelper
    {
        public static string RequireNonEmpty(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Поле \"{fieldName}\" не може бути порожнім.");
            return value.Trim();
        }

        public static int RequirePositive(int value, string fieldName)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(fieldName, $"Значення \"{fieldName}\" має бути більшим за 0.");
            return value;
        }

        public static int RequireInRange(int value, int min, int max, string fieldName)
        {
            if (value < min || value > max)
                throw new ArgumentOutOfRangeException(fieldName, $"Поле \"{fieldName}\" має бути в межах від {min} до {max}.");
            return value;
        }

        public static double RequireInRange(double value, double min, double max, string fieldName)
        {
            if (value < min || value > max)
                throw new ArgumentOutOfRangeException(fieldName, $"Поле \"{fieldName}\" має бути в межах від {min} до {max}.");
            return value;
        }

        public static decimal RequireNonNegative(decimal value, string fieldName)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(fieldName, $"Поле \"{fieldName}\" не може бути від'ємним.");
            return value;
        }
    }

    // ==================== СУТНОСТІ ПРЕДМЕТНОЇ ОБЛАСТІ ====================

    public abstract class BaseEntity
    {
        public readonly int Id;

        protected BaseEntity()
        {
            Id = IdGenerator.NextId();
        }
    }

    public class Course : BaseEntity
    {
        private string _title;
        private int _hours;
        private int _maxStudents;

        public string Title
        {
            get => _title;
            set => _title = ValidationHelper.RequireNonEmpty(value, "Назва курсу");
        }

        public int Hours
        {
            get => _hours;
            set => _hours = ValidationHelper.RequirePositive(value, "Кількість годин");
        }

        public int MaxStudents
        {
            get => _maxStudents;
            set => _maxStudents = ValidationHelper.RequirePositive(value, "Ліміт місць");
        }

        public int? InstructorId { get; set; }

        public Course(string title, int hours, int maxStudents)
        {
            Title = title;
            Hours = hours;
            MaxStudents = maxStudents;
        }
    }

    public class Person : BaseEntity
    {
        private string _fullName;
        private int _age;

        public string FullName
        {
            get => _fullName;
            set => _fullName = ValidationHelper.RequireNonEmpty(value, "ПІБ");
        }

        public int Age
        {
            get => _age;
            set => _age = ValidationHelper.RequireInRange(value, 16, 100, "Вік");
        }

        public Person(string fullName, int age)
        {
            FullName = fullName;
            Age = age;
        }

        public virtual string GetRoleName() => "Учасник";
    }

    public class Student : Person
    {
        private string _groupName;
        private double _averageGrade;

        public string GroupName
        {
            get => _groupName;
            set => _groupName = ValidationHelper.RequireNonEmpty(value, "Назва групи");
        }

        public double AverageGrade
        {
            get => _averageGrade;
            set => _averageGrade = ValidationHelper.RequireInRange(value, 0, 100, "Середній бал");
        }

        public Student(string fullName, int age, string groupName, double averageGrade)
            : base(fullName, age)
        {
            GroupName = groupName;
            AverageGrade = averageGrade;
        }

        public override string GetRoleName() => "Студент";
    }

    public class Instructor : Person
    {
        private string _specialization;
        private decimal _hourlyRate;

        public string Specialization
        {
            get => _specialization;
            set => _specialization = ValidationHelper.RequireNonEmpty(value, "Спеціалізація");
        }

        public decimal HourlyRate
        {
            get => _hourlyRate;
            set => _hourlyRate = ValidationHelper.RequireNonNegative(value, "Погодинна ставка");
        }

        public Instructor(string fullName, int age, string specialization, decimal hourlyRate)
            : base(fullName, age)
        {
            Specialization = specialization;
            HourlyRate = hourlyRate;
        }

        public override string GetRoleName() => "Викладач";
    }

    public class Enrollment : BaseEntity
    {
        public int CourseId { get; }
        public int StudentId { get; }
        public DateTime EnrolledAt { get; }

        public Enrollment(int courseId, int studentId)
        {
            CourseId = courseId;
            StudentId = studentId;
            EnrolledAt = DateTime.Now;
        }
    }

    // ==================== УЗАГАЛЬНЕНИЙ РЕПОЗИТОРІЙ ====================

    public class Repository<T> where T : BaseEntity
    {
        private readonly List<T> _items = new();

        public void Add(T entity)
        {
            if (entity is null) throw new ArgumentNullException(nameof(entity));
            if (_items.Any(item => item.Id == entity.Id))
                throw new InvalidOperationException($"Сутність із ID={entity.Id} вже існує.");
            _items.Add(entity);
        }

        public T? GetById(int id) => _items.FirstOrDefault(item => item.Id == id);
        public IReadOnlyList<T> GetAll() => _items.AsReadOnly();
        public IReadOnlyList<T> Find(Func<T, bool> predicate) => _items.Where(predicate).ToList().AsReadOnly();
        public bool Any(Func<T, bool> predicate) => _items.Any(predicate);
        public bool Remove(int id)
        {
            var entity = GetById(id);
            return entity is not null && _items.Remove(entity);
        }
    }

    // ==================== СЕРВІСНИЙ ШАР ПРЕДМЕТНОЇ ОБЛАСТІ ====================

    public class EducationService
    {
        private readonly Repository<Course> _courseRepo = new();
        private readonly Repository<Person> _participantRepo = new();
        private readonly Repository<Enrollment> _enrollmentRepo = new();
        private readonly List<string> _operationLog = new();

        public IReadOnlyList<string> OperationLog => _operationLog.AsReadOnly();

        private void LogOperation(string message) =>
            _operationLog.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");

        public Course CreateCourse(string title, int hours, int maxStudents)
        {
            string cleanTitle = ValidationHelper.RequireNonEmpty(title, "Назва курсу");
            if (_courseRepo.Any(c => c.Title.Equals(cleanTitle, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Курс із назвою \"{cleanTitle}\" вже існує.");

            var course = new Course(cleanTitle, hours, maxStudents);
            _courseRepo.Add(course);
            LogOperation($"Створено курс #{course.Id}: \"{course.Title}\"");
            return course;
        }

        public Student RegisterStudent(string fullName, int age, string groupName, double averageGrade)
        {
            var student = new Student(fullName, age, groupName, averageGrade);
            _participantRepo.Add(student);
            LogOperation($"Зареєстровано студента #{student.Id}: {student.FullName}");
            return student;
        }

        public Instructor RegisterInstructor(string fullName, int age, string specialization, decimal hourlyRate)
        {
            var instructor = new Instructor(fullName, age, specialization, hourlyRate);
            _participantRepo.Add(instructor);
            LogOperation($"Зареєстровано викладача #{instructor.Id}: {instructor.FullName}");
            return instructor;
        }

        public Course GetCourseById(int id) =>
            _courseRepo.GetById(id) ?? throw new KeyNotFoundException($"Курс із ID={id} не знайдено.");

        public Person GetPersonById(int id) =>
            _participantRepo.GetById(id) ?? throw new KeyNotFoundException($"Учасника з ID={id} не знайдено.");

        public IReadOnlyList<Course> GetAllCourses() => _courseRepo.GetAll();
        public IReadOnlyList<Person> GetAllParticipants() => _participantRepo.GetAll();
        public IReadOnlyList<Enrollment> GetAllEnrollments() => _enrollmentRepo.GetAll();

        public void AssignInstructorToCourse(int courseId, int instructorId)
        {
            var course = GetCourseById(courseId);
            var person = GetPersonById(instructorId);

            if (person is not Instructor)
                throw new InvalidOperationException("Призначити на курс можна лише викладача.");

            course.InstructorId = instructorId;
            LogOperation($"Викладача {person.FullName} призначено на курс \"{course.Title}\"");
        }

        public Enrollment EnrollStudentToCourse(int courseId, int studentId)
        {
            var course = GetCourseById(courseId);
            var person = GetPersonById(studentId);

            if (person is not Student student)
                throw new InvalidOperationException("Записати на курс можна лише студента.");
            if (course.InstructorId is null)
                throw new InvalidOperationException($"На курс \"{course.Title}\" не призначено викладача.");
            if (_enrollmentRepo.Any(e => e.CourseId == courseId && e.StudentId == studentId))
                throw new InvalidOperationException($"Студент {student.FullName} вже записаний на цей курс.");
            if (_enrollmentRepo.Find(e => e.CourseId == courseId).Count >= course.MaxStudents)
                throw new InvalidOperationException($"На курсі \"{course.Title}\" немає вільних місць.");

            var enrollment = new Enrollment(courseId, studentId);
            _enrollmentRepo.Add(enrollment);
            LogOperation($"Студента {student.FullName} записано на курс \"{course.Title}\"");
            return enrollment;
        }
    }

    // ==================== МОДЕЛІ ЗВІТУ (DTO ДЛЯ ЕКСПОРТУ) ====================

    public class SystemStateReport
    {
        public DateTime GeneratedAt { get; set; }
        public int TotalCourses { get; set; }
        public int TotalStudents { get; set; }
        public int TotalInstructors { get; set; }
        public int TotalEnrollments { get; set; }
        public double AverageOccupancyPercent { get; set; }
        public List<CourseReportItem> Courses { get; set; } = new();
        public List<string> RecentOperations { get; set; } = new();
    }

    public class CourseReportItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int Hours { get; set; }
        public string InstructorName { get; set; } = string.Empty;
        public int EnrolledCount { get; set; }
        public int MaxStudents { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    // ==================== МОДУЛЬ ЗВІТНОСТІ ТА ФАЙЛОВОГО ЕКСПОРТУ ====================

    public class ReportingService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Коректне збереження кирилиці
        };

        // 1. Формування структурованого звіту про стан системи
        public SystemStateReport BuildReport(EducationService educationService)
        {
            var courses = educationService.GetAllCourses();
            var participants = educationService.GetAllParticipants();
            var enrollments = educationService.GetAllEnrollments();

            var courseItems = courses.Select(c =>
            {
                int enrolled = enrollments.Count(e => e.CourseId == c.Id);
                string instructorName = c.InstructorId.HasValue
                    ? educationService.GetPersonById(c.InstructorId.Value).FullName
                    : "Не призначено";

                string status = c.InstructorId is null
                    ? "Очікує викладача"
                    : (enrolled >= c.MaxStudents ? "Група укомплектована" : "Відкрито набір");

                return new CourseReportItem
                {
                    Id = c.Id,
                    Title = c.Title,
                    Hours = c.Hours,
                    InstructorName = instructorName,
                    EnrolledCount = enrolled,
                    MaxStudents = c.MaxStudents,
                    Status = status
                };
            }).ToList();

            double avgOccupancy = courseItems.Count > 0
                ? Math.Round(courseItems.Average(c => (double)c.EnrolledCount / c.MaxStudents * 100), 1)
                : 0.0;

            return new SystemStateReport
            {
                GeneratedAt = DateTime.Now,
                TotalCourses = courses.Count,
                TotalStudents = participants.OfType<Student>().Count(),
                TotalInstructors = participants.OfType<Instructor>().Count(),
                TotalEnrollments = enrollments.Count,
                AverageOccupancyPercent = avgOccupancy,
                Courses = courseItems,
                RecentOperations = educationService.OperationLog.ToList()
            };
        }

        // 2. Експорт повного звіту в JSON
        public void ExportToJson(SystemStateReport report, string filePath)
        {
            EnsureValidPath(filePath);
            EnsureDirectoryExists(filePath);

            string json = JsonSerializer.Serialize(report, JsonOptions);
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }

        // 3. Експорт таблиці курсів у CSV
        public void ExportCoursesToCsv(SystemStateReport report, string filePath)
        {
            EnsureValidPath(filePath);
            EnsureDirectoryExists(filePath);

            var sb = new StringBuilder();
            sb.AppendLine("Id;Title;Hours;InstructorName;EnrolledCount;MaxStudents;Status");

            foreach (var c in report.Courses)
            {
                string safeTitle = EscapeCsv(c.Title);
                string safeInstructor = EscapeCsv(c.InstructorName);
                string safeStatus = EscapeCsv(c.Status);
                sb.AppendLine($"{c.Id};{safeTitle};{c.Hours};{safeInstructor};{c.EnrolledCount};{c.MaxStudents};{safeStatus}");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        // 4. Повторне зчитування та перевірка коректності JSON-файлу
        public SystemStateReport LoadAndVerifyJsonReport(string filePath)
        {
            EnsureValidPath(filePath);

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Файл звіту не знайдено за шляхом: {filePath}", filePath);

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
                throw new InvalidDataException("Файл звіту порожній (0 байт).");

            string rawJson = File.ReadAllText(filePath, Encoding.UTF8);
            SystemStateReport? restoredReport = JsonSerializer.Deserialize<SystemStateReport>(rawJson, JsonOptions);

            if (restoredReport is null)
                throw new InvalidDataException("Не вдалося десеріалізувати структуру звіту.");

            if (restoredReport.TotalCourses != restoredReport.Courses.Count)
                throw new InvalidDataException("Порушено цілісність звіту: кількість курсів у підсумку не збігається зі списком.");

            return restoredReport;
        }

        // 5. Повторне зчитування та верифікація CSV-файлу
        public List<CourseReportItem> LoadAndVerifyCsvReport(string filePath)
        {
            EnsureValidPath(filePath);

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"CSV-файл не знайдено: {filePath}", filePath);

            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
            if (lines.Length < 2)
                throw new InvalidDataException("CSV-файл не містить рядків даних.");

            var result = new List<CourseReportItem>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                string[] parts = lines[i].Split(';');
                if (parts.Length != 7)
                    throw new FormatException($"Помилка формату CSV у рядку {i + 1}: очікувалося 7 колонок, отримано {parts.Length}.");

                result.Add(new CourseReportItem
                {
                    Id = int.Parse(parts[0], CultureInfo.InvariantCulture),
                    Title = parts[1].Trim('"'),
                    Hours = int.Parse(parts[2], CultureInfo.InvariantCulture),
                    InstructorName = parts[3].Trim('"'),
                    EnrolledCount = int.Parse(parts[4], CultureInfo.InvariantCulture),
                    MaxStudents = int.Parse(parts[5], CultureInfo.InvariantCulture),
                    Status = parts[6].Trim('"')
                });
            }

            return result;
        }

        private static void EnsureValidPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Шлях до файлу не може бути порожнім.", nameof(filePath));
        }

        private static void EnsureDirectoryExists(string filePath)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        private static string EscapeCsv(string value) =>
            value.Contains(';') || value.Contains('"')
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;
    }

    // ==================== ТОЧКА ВХОДУ ТА ДЕМОНСТРАЦІЯ I/O ====================

    internal class Program
    {
        static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            var educationService = new EducationService();
            var reportingService = new ReportingService();

            // 1. Наповнення системи даними
            var instructor = educationService.RegisterInstructor("Іван Петренко", 38, "Архітектура ПЗ та .NET", 850m);
            var s1 = educationService.RegisterStudent("Олександр Ковальчук", 19, "ІПЗ-21", 92.5);
            var s2 = educationService.RegisterStudent("Марія Ткаченко", 20, "КН-22", 88.0);

            var course1 = educationService.CreateCourse("Об'єктно-орієнтоване програмування в C#", 60, 2);
            var course2 = educationService.CreateCourse("Бази даних та SQL", 45, 15);

            educationService.AssignInstructorToCourse(course1.Id, instructor.Id);
            educationService.EnrollStudentToCourse(course1.Id, s1.Id);
            educationService.EnrollStudentToCourse(course1.Id, s2.Id);

            // 2. Формування та експорт звіту у JSON і CSV
            SystemStateReport report = reportingService.BuildReport(educationService);
            string jsonPath = Path.Combine("Reports", "system_report.json");
            string csvPath = Path.Combine("Reports", "courses_summary.csv");

            Console.WriteLine("=== 1. Експорт звітності у файли ===");
            RunIoSafely("Експорт у JSON", () =>
            {
                reportingService.ExportToJson(report, jsonPath);
                Console.WriteLine($"JSON-звіт успішно збережено: {Path.GetFullPath(jsonPath)}");
            });

            RunIoSafely("Експорт у CSV", () =>
            {
                reportingService.ExportCoursesToCsv(report, csvPath);
                Console.WriteLine($"CSV-звіт успішно збережено: {Path.GetFullPath(csvPath)}");
            });

            // 3. Повторне зчитування та верифікація збережених файлів
            Console.WriteLine("\n=== 2. Повторне зчитування та верифікація файлів ===");
            RunIoSafely("Верифікація JSON", () =>
            {
                var verifiedReport = reportingService.LoadAndVerifyJsonReport(jsonPath);
                Console.WriteLine($"JSON перевірено: дата={verifiedReport.GeneratedAt:g}, курсів={verifiedReport.TotalCourses}, " +
                                  $"студентів={verifiedReport.TotalStudents}, заповненість={verifiedReport.AverageOccupancyPercent}%, " +
                                  $"записів у журналі={verifiedReport.RecentOperations.Count}");
            });

            RunIoSafely("Верифікація CSV", () =>
            {
                var verifiedRows = reportingService.LoadAndVerifyCsvReport(csvPath);
                Console.WriteLine($"CSV перевірено: зчитано {verifiedRows.Count} рядків курсів:");
                foreach (var row in verifiedRows)
                    Console.WriteLine($"  - [{row.Id}] {row.Title}: {row.EnrolledCount}/{row.MaxStudents} ({row.Status})");
            });

            // 4. Демонстрація обробки помилкових ситуацій файлового I/O
            Console.WriteLine("\n=== 3. Демонстрація реакції на помилки файлового I/O ===");

            // Помилка А: звернення до неіснуючого файлу
            RunIoSafely("Читання неіснуючого файлу", () =>
                reportingService.LoadAndVerifyJsonReport(Path.Combine("Reports", "missing_file.json")));

            // Помилка Б: некоректний / пошкоджений JSON-файл
            string brokenJsonPath = Path.Combine("Reports", "corrupted.json");
            File.WriteAllText(brokenJsonPath, "{ broken_json: [1, 2, ");
            RunIoSafely("Читання пошкодженого JSON", () =>
                reportingService.LoadAndVerifyJsonReport(brokenJsonPath));

            // Помилка В: порожній файл
            string emptyFilePath = Path.Combine("Reports", "empty.json");
            File.WriteAllText(emptyFilePath, string.Empty);
            RunIoSafely("Читання порожнього файлу", () =>
                reportingService.LoadAndVerifyJsonReport(emptyFilePath));

            // Помилка Г: некоректний шлях із забороненими символами або неіснуючим диском
            RunIoSafely("Запис за недоступним шляхом", () =>
                reportingService.ExportToJson(report, @"Z:\NonExistentDrive\Reports\fail.json"));
        }

        private static void RunIoSafely(string operationName, Action ioAction)
        {
            try
            {
                ioAction();
            }
            catch