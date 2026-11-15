using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace DomainModelDemo
{
    // ==================== БАЗОВІ СУТНОСТІ ТА СТАТИЧНІ УТИЛІТИ ====================

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
                throw new ArgumentOutOfRangeException(fieldName, $"Поле \"{fieldName}\" має бути більшим за 0.");
            return value;
        }
    }

    public abstract class BaseEntity
    {
        public readonly int Id = IdGenerator.NextId();
    }

    public enum CourseStatus
    {
        AwaitingInstructor, // Очікує викладача
        OpenForEnrollment,  // Відкрито набір
        Full                // Група укомплектована
    }

    public class Course : BaseEntity
    {
        public string Title { get; set; }
        public int Hours { get; set; }
        public int MaxStudents { get; set; }
        public int? InstructorId { get; set; }
        public CourseStatus Status { get; set; } = CourseStatus.AwaitingInstructor;

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
            Age = age;
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
        public DateTime EnrolledAt { get; } = DateTime.Now;

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
    }

    // ==================== ПОДІЄВА ПІДСИСТЕМА (EVENT ARGS) ====================

    public enum DomainEventCategory
    {
        EntityCreated,
        StatusChanged,
        OperationCompleted,
        RuleViolated
    }

    public class EducationEventArgs : EventArgs
    {
        public DomainEventCategory Category { get; }
        public string EntityType { get; }
        public int? EntityId { get; }
        public string Message { get; }
        public DateTime Timestamp { get; }

        public EducationEventArgs(
            DomainEventCategory category,
            string entityType,
            int? entityId,
            string message)
        {
            Category = category;
            EntityType = entityType;
            EntityId = entityId;
            Message = message;
            Timestamp = DateTime.Now;
        }
    }

    // ==================== ВИДАВЕЦЬ ПОДІЙ (PUBLISHER) ====================

    public class EducationService
    {
        private readonly Repository<Course> _courses = new();
        private readonly Repository<Person> _participants = new();
        private readonly Repository<Enrollment> _enrollments = new();

        // 1. Подія створення сутності
        public event EventHandler<EducationEventArgs>? EntityCreated;

        // 2. Подія зміни статусу курсу
        public event EventHandler<EducationEventArgs>? CourseStatusChanged;

        // 3. Подія успішного виконання операції (запис на курс)
        public event EventHandler<EducationEventArgs>? EnrollmentCompleted;

        // 4. Подія порушення бізнес-правила
        public event EventHandler<EducationEventArgs>? BusinessRuleViolated;

        // Безпечне сповіщення всіх підписників із ізоляцією помилок кожного обробника
        private void RaiseEvent(EventHandler<EducationEventArgs>? handler, EducationEventArgs args)
        {
            if (handler is null) return;

            foreach (Delegate subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((EventHandler<EducationEventArgs>)subscriber).Invoke(this, args);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[EventDispatcher Error]: Підписник {subscriber.Method.Name} завершився з помилкою: {ex.Message}");
                }
            }
        }

        private void ReportRuleViolation(string entityType, int? entityId, string reason)
        {
            RaiseEvent(BusinessRuleViolated, new EducationEventArgs(
                DomainEventCategory.RuleViolated,
                entityType,
                entityId,
                reason));

            throw new InvalidOperationException(reason);
        }

        private void UpdateCourseStatus(Course course, CourseStatus newStatus)
        {
            if (course.Status == newStatus) return;

            CourseStatus oldStatus = course.Status;
            course.Status = newStatus;

            RaiseEvent(CourseStatusChanged, new EducationEventArgs(
                DomainEventCategory.StatusChanged,
                nameof(Course),
                course.Id,
                $"Статус курсу \"{course.Title}\" змінено: {oldStatus} -> {newStatus}"));
        }

        // --- РЕАЛЬНІ ОПЕРАЦІЇ ПРЕДМЕТНОЇ ОБЛАСТІ ---

        public Course CreateCourse(string title, int hours, int maxStudents)
        {
            string cleanTitle = ValidationHelper.RequireNonEmpty(title, "Назва курсу");
            if (_courses.Any(c => c.Title.Equals(cleanTitle, StringComparison.OrdinalIgnoreCase)))
                ReportRuleViolation(nameof(Course), null, $"Курс із назвою \"{cleanTitle}\" вже існує.");

            var course = new Course(cleanTitle, hours, maxStudents);
            _courses.Add(course);

            RaiseEvent(EntityCreated, new EducationEventArgs(
                DomainEventCategory.EntityCreated,
                nameof(Course),
                course.Id,
                $"Створено курс \"{course.Title}\" (ID={course.Id}, ліміт місць: {course.MaxStudents})"));

            return course;
        }

        public Student RegisterStudent(string fullName, int age, string groupName)
        {
            var student = new Student(fullName, age, groupName);
            _participants.Add(student);

            RaiseEvent(EntityCreated, new EducationEventArgs(
                DomainEventCategory.EntityCreated,
                nameof(Student),
                student.Id,
                $"Зареєстровано студента {student.FullName} (ID={student.Id}, група {student.GroupName})"));

            return student;
        }

        public Instructor RegisterInstructor(string fullName, int age, string specialization)
        {
            var instructor = new Instructor(fullName, age, specialization);
            _participants.Add(instructor);

            RaiseEvent(EntityCreated, new EducationEventArgs(
                DomainEventCategory.EntityCreated,
                nameof(Instructor),
                instructor.Id,
                $"Зареєстровано викладача {instructor.FullName} (ID={instructor.Id}, {instructor.Specialization})"));

            return instructor;
        }

        public void AssignInstructorToCourse(int courseId, int instructorId)
        {
            var course = _courses.GetById(courseId) ?? throw new KeyNotFoundException($"Курс ID={courseId} не знайдено.");
            var person = _participants.GetById(instructorId) ?? throw new KeyNotFoundException($"Учасника ID={instructorId} не знайдено.");

            if (person is not Instructor instructor)
                ReportRuleViolation(nameof(Course), courseId, $"Користувач {person.FullName} не є викладачем.");

            course.InstructorId = instructorId;

            // Автоматична зміна статусу курсу після призначення викладача
            UpdateCourseStatus(course, CourseStatus.OpenForEnrollment);
        }

        public Enrollment EnrollStudentToCourse(int courseId, int studentId)
        {
            var course = _courses.GetById(courseId) ?? throw new KeyNotFoundException($"Курс ID={courseId} не знайдено.");
            var person = _participants.GetById(studentId) ?? throw new KeyNotFoundException($"Учасника ID={studentId} не знайдено.");

            if (person is not Student student)
                ReportRuleViolation(nameof(Enrollment), courseId, $"Користувач {person.FullName} не є студентом.");

            if (course.InstructorId is null)
                ReportRuleViolation(nameof(Course), courseId, $"Запис на курс \"{course.Title}\" неможливий: не призначено викладача.");

            if (_enrollments.Any(e => e.CourseId == courseId && e.StudentId == studentId))
                ReportRuleViolation(nameof(Enrollment), courseId, $"Студент {((Student)person).FullName} вже записаний на курс \"{course.Title}\".");

            int enrolledCount = _enrollments.Find(e => e.CourseId == courseId).Count;
            if (enrolledCount >= course.MaxStudents)
                ReportRuleViolation(nameof(Course), courseId, $"На курсі \"{course.Title}\" досягнуто ліміту місць ({course.MaxStudents}).");

            var enrollment = new Enrollment(courseId, studentId);
            _enrollments.Add(enrollment);

            RaiseEvent(EnrollmentCompleted, new EducationEventArgs(
                DomainEventCategory.OperationCompleted,
                nameof(Enrollment),
                enrollment.Id,
                $"Студента {((Student)person).FullName} зараховано на курс \"{course.Title}\""));

            // Якщо всі місця зайнято — оновлюємо статус курсу
            if (enrolledCount + 1 >= course.MaxStudents)
                UpdateCourseStatus(course, CourseStatus.Full);

            return enrollment;
        }
    }

    // ==================== НЕЗАЛЕЖНІ ПІДПИСНИКИ (SUBSCRIBERS) ====================

    // Підписник №1: Формує інтерактивні повідомлення користувачу
    public class UserNotificationSubscriber
    {
        public void HandleNotification(object? sender, EducationEventArgs e)
        {
            string prefix = e.Category switch
            {
                DomainEventCategory.EntityCreated => "[СТВОРЕНО]",
                DomainEventCategory.StatusChanged => "[СТАТУС]",
                DomainEventCategory.OperationCompleted => "[ОПЕРАЦІЯ]",
                DomainEventCategory.RuleViolated => "[УВАГА! ПОРУШЕННЯ ПРАВИЛА]",
                _ => "[ПОДІЯ]"
            };

            Console.WriteLine($"{prefix} {e.Message}");
        }
    }

    // Підписник №2: Записує журнал подій у файл
    public class FileAuditLoggerSubscriber
    {
        private readonly string _filePath;

        public FileAuditLoggerSubscriber(string filePath = "Logs/events_audit.log")
        {
            _filePath = filePath;
            string? dir = Path.GetDirectoryName(Path.GetFullPath(_filePath));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        public void WriteToLog(object? sender, EducationEventArgs e)
        {
            string line = $"[{e.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{e.Category}] ({e.EntityType}#{e.EntityId?.ToString() ?? "-"}) {e.Message}{Environment.NewLine}";
            File.AppendAllText(_filePath, line, Encoding.UTF8);
        }

        public string ReadLog() =>
            File.Exists(_filePath) ? File.ReadAllText(_filePath, Encoding.UTF8) : string.Empty;
    }

    // Підписник №3: Веде статистичний підрахунок активності та помилок
    public class DomainAnalyticsSubscriber
    {
        public int CreatedEntities { get; private set; }
        public int StatusTransitions { get; private set; }
        public int CompletedEnrollments { get; private set; }
        public int ViolatedRules { get; private set; }

        public void TrackMetrics(object? sender, EducationEventArgs e)
        {
            switch (e.Category)
            {
                case DomainEventCategory.EntityCreated:
                    CreatedEntities++;
                    break;
                case DomainEventCategory.StatusChanged:
                    StatusTransitions++;
                    break;
                case DomainEventCategory.OperationCompleted:
                    CompletedEnrollments++;
                    break;
                case DomainEventCategory.RuleViolated:
                    ViolatedRules++;
                    break;
            }
        }

        public void PrintStatistics()
        {
            Console.WriteLine("=== Зведена статистика (DomainAnalyticsSubscriber) ===");
            Console.WriteLine($"  Створено сутностей        : {CreatedEntities}");
            Console.WriteLine($"  Змін статусів курсів      : {StatusTransitions}");
            Console.WriteLine($"  Успішних записів на курси : {CompletedEnrollments}");
            Console.WriteLine($"  Зафіксовано порушень      : {ViolatedRules}");
        }
    }

    // ==================== ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ ====================

    internal class Program
    {
        static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            var service = new EducationService();

            // Ініціалізація 3 незалежних підписників
            var notifier = new UserNotificationSubscriber();
            string auditPath = Path.Combine("Logs", $"audit_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            var fileLogger = new FileAuditLoggerSubscriber(auditPath);
            var analytics = new DomainAnalyticsSubscriber();

            // Підписка на всі події сервісу (Multicast)
            void SubscribeAll(EventHandler<EducationEventArgs> handler)
            {
                service.EntityCreated += handler;
                service.CourseStatusChanged += handler;
                service.EnrollmentCompleted += handler;
                service.BusinessRuleViolated += handler;
            }

            SubscribeAll(notifier.HandleNotification);
            SubscribeAll(fileLogger.WriteToLog);
            SubscribeAll(analytics.TrackMetrics);

            Console.WriteLine("=== Виконання реальних операцій проєкту ===\n");

            // 1. Створення сутностей (генерується EntityCreated)
            var instructor = service.RegisterInstructor("Іван Петренко", 38, "Архітектура .NET");
            var s1 = service.RegisterStudent("Олександр Ковальчук", 19, "ІПЗ-21");
            var s2 = service.RegisterStudent("Марія Ткаченко", 20, "КН-22");
            var s3 = service.RegisterStudent("Дмитро Бондаренко", 19, "ІПЗ-21");
            var course = service.CreateCourse("Подієво-орієнтована архітектура в C#", 45, maxStudents: 2);

            Console.WriteLine();

            // 2. Порушення правила: спроба запису до призначення викладача (генерується BusinessRuleViolated)
            TryRun(() => service.EnrollStudentToCourse(course.Id, s1.Id));

            // 3. Призначення викладача (генерується CourseStatusChanged: AwaitingInstructor -> OpenForEnrollment)
            service.AssignInstructorToCourse(course.Id, instructor.Id);

            // 4. Запис студентів (генерується EnrollmentCompleted, а після 2-го студента — CourseStatusChanged -> Full)
            service.EnrollStudentToCourse(course.Id, s1.Id);
            service.EnrollStudentToCourse(course.Id, s2.Id);

            // 5. Порушення правила: перевищення ліміту місць (генерується BusinessRuleViolated)
            TryRun(() => service.EnrollStudentToCourse(course.Id, s3.Id));

            Console.WriteLine();
            analytics.PrintStatistics();

            Console.WriteLine($"\n=== Вміст файлового журналу ({Path.GetFullPath(auditPath)}) ===\n");
            Console.WriteLine(fileLogger.ReadLog());
        }

        private static void TryRun(Action action)
        {
            try
            {
                action();
            }
            catch (InvalidOperationException)
            {
                // Повідомлення вже оброблено підписниками події BusinessRuleViolated
            }
        }
    }
}