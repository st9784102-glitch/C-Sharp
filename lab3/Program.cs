using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace DomainModelDemo
{
    // ==================== КОМПОНЕНТ ЛОГУВАННЯ ====================

    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Critical
    }

    public class FileLogger
    {
        private readonly string _logFilePath;
        private readonly object _syncLock = new();

        public string LogFilePath => Path.GetFullPath(_logFilePath);

        public FileLogger(string logFilePath = "Logs/application.log")
        {
            _logFilePath = logFilePath;
            string? dir = Path.GetDirectoryName(Path.GetFullPath(_logFilePath));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        public void LogInfo(string operationName, string message) =>
            WriteEntry(LogLevel.Info, operationName, message);

        public void LogWarning(string operationName, string message, Exception? ex = null) =>
            WriteEntry(LogLevel.Warning, operationName, message, ex);

        public void LogError(string operationName, string message, Exception? ex = null) =>
            WriteEntry(LogLevel.Error, operationName, message, ex);

        private void WriteEntry(LogLevel level, string operationName, string message, Exception? ex = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpper()}] Операція: {operationName}");

            if (ex is not null)
            {
                sb.AppendLine($"  Тип помилки : {ex.GetType().FullName}");
                sb.AppendLine($"  Повідомлення: {message}");
                sb.AppendLine($"  Діагностика : {ex}"); // Містить Message, InnerException та StackTrace
            }
            else
            {
                sb.AppendLine($"  Повідомлення: {message}");
            }

            sb.AppendLine(new string('-', 80));

            try
            {
                lock (_syncLock)
                {
                    File.AppendAllText(_logFilePath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception ioEx)
            {
                // Резервний вивід, якщо запис у лог-файл тимчасово недоступний
                Console.Error.WriteLine($"[LOGGER FALLBACK]: Не вдалося записати лог у файл: {ioEx.Message}");
            }
        }

        public string ReadAllLogs() =>
            File.Exists(_logFilePath) ? File.ReadAllText(_logFilePath, Encoding.UTF8) : "Лог-файл порожній.";
    }

    // ==================== КОНТЕЙНЕР РЕЗУЛЬТАТУ ВИКОНАННЯ ====================

    public class Result<T>
    {
        public bool IsSuccess { get; }
        public T? Value { get; }
        public string ErrorMessage { get; }
        public string ErrorType { get; }

        private Result(bool isSuccess, T? value, string errorMessage, string errorType)
        {
            IsSuccess = isSuccess;
            Value = value;
            ErrorMessage = errorMessage;
            ErrorType = errorType;
        }

        public static Result<T> Success(T value) =>
            new(true, value, string.Empty, string.Empty);

        public static Result<T> Failure(string errorMessage, string errorType) =>
            new(false, default, errorMessage, errorType);
    }

    // ==================== ЦЕНТРАЛІЗОВАНИЙ ВИКОНАВЕЦЬ (SAFE EXECUTION) ====================

    public class SafeExecutionRunner
    {
        private readonly FileLogger _logger;

        public SafeExecutionRunner(FileLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Безпечне виконання операції, що повертає значення
        public Result<T> Execute<T>(string operationName, Func<T> operation)
        {
            try
            {
                T result = operation();
                _logger.LogInfo(operationName, "Операцію виконано успішно.");
                return Result<T>.Success(result);
            }
            catch (EducationDomainException ex)
            {
                // Очікувані порушення бізнес-правил фіксуємо як Warning із повною діагностикою
                _logger.LogWarning(operationName, ex.Message, ex);
                return Result<T>.Failure(ex.Message, ex.GetType().Name);
            }
            catch (ArgumentException ex)
            {
                // Помилки вхідних параметрів валідації
                _logger.LogWarning(operationName, ex.Message, ex);
                return Result<T>.Failure(ex.Message, ex.GetType().Name);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(operationName, ex.Message, ex);
                return Result<T>.Failure(ex.Message, ex.GetType().Name);
            }
            catch (Exception ex)
            {
                // Непередбачені системні помилки фіксуємо як Error
                _logger.LogError(operationName, $"Критичний збій під час виконання: {ex.Message}", ex);
                return Result<T>.Failure("Виникла внутрішня помилка системи. Деталі записано в журнал.", ex.GetType().Name);
            }
        }

        // Перевантаження для операцій без повернення значення (Action)
        public Result<bool> Execute(string operationName, Action operation) =>
            Execute(operationName, () =>
            {
                operation();
                return true;
            });
    }

    // ==================== ДОМЕННІ ВИНЯТКИ ====================

    public class EducationDomainException : Exception
    {
        public EducationDomainException(string message) : base(message) { }
        public EducationDomainException(string message, Exception inner) : base(message, inner) { }
    }

    public class CourseCapacityExceededException : EducationDomainException
    {
        public CourseCapacityExceededException(string courseTitle, int maxCapacity)
            : base($"На курсі \"{courseTitle}\" досягнуто ліміту місць ({maxCapacity}).") { }
    }

    public class DuplicateEnrollmentException : EducationDomainException
    {
        public DuplicateEnrollmentException(string studentName, string courseTitle)
            : base($"Студент {studentName} вже записаний на курс \"{courseTitle}\".") { }
    }

    public class CoursePreconditionException : EducationDomainException
    {
        public CoursePreconditionException(string message) : base(message) { }
    }

    // ==================== СУТНОСТІ, РЕПОЗИТОРІЙ ТА СЕРВІС ====================

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
                throw new ArgumentException($"Поле \"{fieldName}\" не може бути порожнім.", fieldName);
            return value.Trim();
        }

        public static int RequirePositive(int value, string fieldName)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(fieldName, $"Поле \"{fieldName}\" має бути більшим за 0.");
            return value;
        }

        public static int RequireInRange(int value, int min, int max, string fieldName)
        {
            if (value < min || value > max)
                throw new ArgumentOutOfRangeException(fieldName, $"Поле \"{fieldName}\" має бути в межах {min}..{max}.");
            return value;
        }
    }

    public abstract class BaseEntity
    {
        public readonly int Id = IdGenerator.NextId();
    }

    public class Course : BaseEntity
    {
        public string Title { get; set; }
        public int Hours { get; set; }
        public int MaxStudents { get; set; }
        public int? InstructorId { get; set; }

        public Course(string title, int hours, int maxStudents)
        {
            Title = ValidationHelper.RequireNonEmpty(title, "Назва курсу");
            Hours = ValidationHelper.RequirePositive(hours, "Години");
            MaxStudents = ValidationHelper.RequirePositive(maxStudents, "Ліміт місць");
        }
    }

    public class Person : BaseEntity
    {
        public string FullName { get; set; }
        public int Age { get; set; }

        public Person(string fullName, int age)
        {
            FullName = ValidationHelper.RequireNonEmpty(fullName, "ПІБ");
            Age = ValidationHelper.RequireInRange(age, 16, 100, "Вік");
        }
    }

    public class Student : Person
    {
        public string GroupName { get; set; }

        public Student(string fullName, int age, string groupName) : base(fullName, age)
        {
            GroupName = ValidationHelper.RequireNonEmpty(groupName, "Група");
        }
    }

    public class Instructor : Person
    {
        public string Specialization { get; set; }

        public Instructor(string fullName, int age, string specialization) : base(fullName, age)
        {
            Specialization = ValidationHelper.RequireNonEmpty(specialization, "Спеціалізація");
        }
    }

    public class Enrollment : BaseEntity
    {
        public int CourseId { get; }
        public int StudentId { get; }

        public Enrollment(int courseId, int studentId)
        {
            CourseId = courseId;
            StudentId = studentId;
        }
    }

    public class Repository<T> where T : BaseEntity
    {
        private readonly List<T> _items = new();
        public void Add(T entity) => _items.Add(entity);
        public T? GetById(int id) => _items.FirstOrDefault(x => x.Id == id);
        public IReadOnlyList<T> Find(Func<T, bool> predicate) => _items.Where(predicate).ToList();
        public bool Any(Func<T, bool> predicate) => _items.Any(predicate);
        public bool Remove(int id)
        {
            var entity = GetById(id);
            return entity is not null && _items.Remove(entity);
        }
    }

    public class EducationService
    {
        private readonly Repository<Course> _courses = new();
        private readonly Repository<Person> _participants = new();
        private readonly Repository<Enrollment> _enrollments = new();

        public Course CreateCourse(string title, int hours, int maxStudents)
        {
            string cleanTitle = ValidationHelper.RequireNonEmpty(title, "Назва курсу");
            if (_courses.Any(c => c.Title.Equals(cleanTitle, StringComparison.OrdinalIgnoreCase)))
                throw new CoursePreconditionException($"Курс \"{cleanTitle}\" вже існує.");

            var course = new Course(cleanTitle, hours, maxStudents);
            _courses.Add(course);
            return course;
        }

        public Student RegisterStudent(string fullName, int age, string groupName)
        {
            var student = new Student(fullName, age, groupName);
            _participants.Add(student);
            return student;
        }

        public Instructor RegisterInstructor(string fullName, int age, string specialization)
        {
            var instructor = new Instructor(fullName, age, specialization);
            _participants.Add(instructor);
            return instructor;
        }

        public void AssignInstructorToCourse(int courseId, int instructorId)
        {
            var course = _courses.GetById(courseId) ?? throw new KeyNotFoundException($"Курс ID={courseId} не знайдено.");
            var person = _participants.GetById(instructorId) ?? throw new KeyNotFoundException($"Учасника ID={instructorId} не знайдено.");

            if (person is not Instructor)
                throw new CoursePreconditionException("Призначити на курс можна лише викладача.");

            course.InstructorId = instructorId;
        }

        public Enrollment EnrollStudentToCourse(int courseId, int studentId)
        {
            var course = _courses.GetById(courseId) ?? throw new KeyNotFoundException($"Курс ID={courseId} не знайдено.");
            var person = _participants.GetById(studentId) ?? throw new KeyNotFoundException($"Учасника ID={studentId} не знайдено.");

            if (person is not Student student)
                throw new CoursePreconditionException("Записати на курс можна лише студента.");
            if (course.InstructorId is null)
                throw new CoursePreconditionException($"На курс \"{course.Title}\" не призначено викладача.");
            if (_enrollments.Any(e => e.CourseId == courseId && e.StudentId == studentId))
                throw new DuplicateEnrollmentException(student.FullName, course.Title);
            if (_enrollments.Find(e => e.CourseId == courseId).Count >= course.MaxStudents)
                throw new CourseCapacityExceededException(course.Title, course.MaxStudents);

            var enrollment = new Enrollment(courseId, studentId);
            _enrollments.Add(enrollment);
            return enrollment;
        }

        public void DeleteCourse(int courseId)
        {
            var course = _courses.GetById(courseId) ?? throw new KeyNotFoundException($"Курс ID={courseId} не знайдено.");
            if (_enrollments.Any(e => e.CourseId == courseId))
                throw new CoursePreconditionException($"Неможливо видалити курс \"{course.Title}\", оскільки на ньому є студенти.");

            _courses.Remove(courseId);
        }
    }

    // ==================== ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ ====================

    internal class Program
    {
        static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            string logPath = Path.Combine("Logs", $"session_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            var logger = new FileLogger(logPath);
            var safeRunner = new SafeExecutionRunner(logger);
            var service = new EducationService();

            Console.WriteLine("=== Запуск демонстрації централізованої обробки помилок та логування ===\n");

            // 1. Успішне створення базових сутностей
            var instructorRes = safeRunner.Execute("Реєстрація викладача", () =>
                service.RegisterInstructor("Іван Петренко", 38, "Архітектура ПЗ"));

            var s1Res = safeRunner.Execute("Реєстрація студента 1", () =>
                service.RegisterStudent("Олександр Ковальчук", 19, "ІПЗ-21"));

            var s2Res = safeRunner.Execute("Реєстрація студента 2", () =>
                service.RegisterStudent("Марія Ткаченко", 20, "КН-22"));

            var courseRes = safeRunner.Execute("Створення курсу C#", () =>
                service.CreateCourse("Архітектура та надійність ПЗ", 60, maxStudents: 1));

            // 2. Моделювання типових помилок предметної області (система не падає, а пише в лог)
            var badStudentRes = safeRunner.Execute("Реєстрація студента з некоректним віком", () =>
                service.RegisterStudent("Юрій Малий", 10, "ІПЗ-21"));
            PrintOutcome("Реєстрація студента (вік=10)", badStudentRes);

            var noInstructorEnroll = safeRunner.Execute("Запис студента на курс без викладача", () =>
                service.EnrollStudentToCourse(courseRes.Value!.Id, s1Res.Value!.Id));
            PrintOutcome("Запис на курс без викладача", noInstructorEnroll);

            // 3. Продовження нормальної роботи: призначення викладача та запис першого студента
            var assignRes = safeRunner.Execute("Призначення викладача на курс", () =>
                service.AssignInstructorToCourse(courseRes.Value!.Id, instructorRes.Value!.Id));
            PrintOutcome("Призначення викладача", assignRes);

            var validEnroll = safeRunner.Execute("Запис першого студента на курс", () =>
                service.EnrollStudentToCourse(courseRes.Value!.Id, s1Res.Value!.Id));
            PrintOutcome("Запис першого студента", validEnroll);

            // 4. Моделювання повторного запису, перевищення ліміту місць та видалення активного курсу
            var duplicateEnroll = safeRunner.Execute("Повторний запис того ж студента", () =>
                service.EnrollStudentToCourse(courseRes.Value!.Id, s1Res.Value!.Id));
            PrintOutcome("Повторний запис студента", duplicateEnroll);

            var capacityExceeded = safeRunner.Execute("Запис другого студента понад ліміт", () =>
                service.EnrollStudentToCourse(courseRes.Value!.Id, s2Res.Value!.Id));
            PrintOutcome("Перевищення ліміту місць", capacityExceeded);

            var deleteActiveCourse = safeRunner.Execute("Видалення курсу зі студентами", () =>
                service.DeleteCourse(courseRes.Value!.Id));
            PrintOutcome("Видалення активного курсу", deleteActiveCourse);

            // 5. Підтвердження коректного формування лог-файлу
            Console.WriteLine($"\n=== Вміст сформованого лог-файлу ({logger.LogFilePath}) ===\n");
            Console.WriteLine(logger.ReadAllLogs());
        }

        private static void PrintOutcome<T>(string stepName, Result<T> result)
        {
            if (result.IsSuccess)
                Console.WriteLine($"[OK]   {stepName}: успішно виконано.");
            else
                Console.WriteLine($"[FAIL] {stepName} -> ({result.ErrorType}): {result.ErrorMessage}");
        }
    }
}