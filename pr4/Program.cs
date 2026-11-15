using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DomainModelDemo
{
    // ==================== СТАТИЧНІ УТИЛІТИ ТА МОДЕЛІ ====================

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

        public static double RequireInRange(double value, double min, double max, string fieldName)
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
            Age = age;
        }
    }

    public class Student : Person
    {
        private double _averageGrade;
        public string GroupName { get; set; }

        public double AverageGrade
        {
            get => _averageGrade;
            set => _averageGrade = ValidationHelper.RequireInRange(value, 0, 100, "Середній бал");
        }

        public Student(string fullName, int age, string groupName, double averageGrade)
            : base(fullName, age)
        {
            GroupName = ValidationHelper.RequireNonEmpty(groupName, "Група");
            AverageGrade = averageGrade;
        }
    }

    public class Instructor : Person
    {
        public string Specialization { get; set; }

        public Instructor(string fullName, int age, string specialization)
            : base(fullName, age)
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
        public IReadOnlyList<T> GetAll() => _items.AsReadOnly();
        public IReadOnlyList<T> Find(Func<T, bool> predicate) => _items.Where(predicate).ToList().AsReadOnly();
        public bool Any(Func<T, bool> predicate) => _items.Any(predicate);
    }

    // ==================== АРГУМЕНТИ ПОДІЙ (EVENT ARGS) ====================

    public class EducationEventArgs : EventArgs
    {
        public string EventType { get; }
        public string Description { get; }
        public string RecipientInfo { get; }
        public DateTime OccurredAt { get; }

        public EducationEventArgs(string eventType, string description, string recipientInfo = "")
        {
            EventType = eventType;
            Description = description;
            RecipientInfo = recipientInfo;
            OccurredAt = DateTime.Now;
        }
    }

    // ==================== ВИДАВЕЦЬ ПОДІЙ ТА СЕРВІС (PUBLISHER) ====================

    public class EducationService
    {
        private readonly Repository<Course> _courses = new();
        private readonly Repository<Person> _participants = new();
        private readonly Repository<Enrollment> _enrollments = new();

        // Оголошення подій через стандартний узагальнений делегат EventHandler<TEventArgs>
        public event EventHandler<EducationEventArgs>? CourseCreated;
        public event EventHandler<EducationEventArgs>? InstructorAssigned;
        public event EventHandler<EducationEventArgs>? StudentEnrolled;

        // Захищені методи безпечного виклику подій із ізоляцією помилок підписників
        protected virtual void OnCourseCreated(EducationEventArgs e) =>
            NotifySubscribersSafely(CourseCreated, e);

        protected virtual void OnInstructorAssigned(EducationEventArgs e) =>
            NotifySubscribersSafely(InstructorAssigned, e);

        protected virtual void OnStudentEnrolled(EducationEventArgs e) =>
            NotifySubscribersSafely(StudentEnrolled, e);

        private void NotifySubscribersSafely(EventHandler<EducationEventArgs>? eventHandler, EducationEventArgs args)
        {
            if (eventHandler is null) return;

            // Перебір ланцюжка викликів (Multicast Delegate), щоб збій одного підписника не зупинив інших
            foreach (Delegate handler in eventHandler.GetInvocationList())
            {
                try
                {
                    ((EventHandler<EducationEventArgs>)handler).Invoke(this, args);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Попередження диспетчера подій]: Підписник {handler.Method.Name} викликав помилку: {ex.Message}");
                }
            }
        }

        // --- ОСНОВНІ ОПЕРАЦІЇ З ГЕНЕРАЦІЄЮ ПОДІЙ ---

        public Course CreateCourse(string title, int hours, int maxStudents)
        {
            string cleanTitle = ValidationHelper.RequireNonEmpty(title, "Назва курсу");
            if (_courses.Any(c => c.Title.Equals(cleanTitle, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Курс \"{cleanTitle}\" вже існує.");

            var course = new Course(cleanTitle, hours, maxStudents);
            _courses.Add(course);

            // Генерація події про створення нового курсу
            OnCourseCreated(new EducationEventArgs(
                "COURSE_CREATED",
                $"Створено курс \"{course.Title}\" ({course.Hours} год., місць: {course.MaxStudents})"));

            return course;
        }

        public Student RegisterStudent(string fullName, int age, string groupName, double averageGrade)
        {
            var student = new Student(fullName, age, groupName, averageGrade);
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

            if (person is not Instructor instructor)
                throw new InvalidOperationException("Призначити на курс можна лише викладача.");

            course.InstructorId = instructorId;

            // Генерація події про призначення викладача
            OnInstructorAssigned(new EducationEventArgs(
                "INSTRUCTOR_ASSIGNED",
                $"Викладача {instructor.FullName} призначено на курс \"{course.Title}\"",
                instructor.FullName));
        }

        public Enrollment EnrollStudentToCourse(int courseId, int studentId)
        {
            var course = _courses.GetById(courseId) ?? throw new KeyNotFoundException($"Курс ID={courseId} не знайдено.");
            var person = _participants.GetById(studentId) ?? throw new KeyNotFoundException($"Учасника ID={studentId} не знайдено.");

            if (person is not Student student)
                throw new InvalidOperationException("Записати на курс можна лише студента.");
            if (course.InstructorId is null)
                throw new InvalidOperationException($"На курс \"{course.Title}\" ще не призначено викладача.");
            if (_enrollments.Any(e => e.CourseId == courseId && e.StudentId == studentId))
                throw new InvalidOperationException($"Студент {student.FullName} вже записаний на курс.");
            if (_enrollments.Find(e => e.CourseId == courseId).Count >= course.MaxStudents)
                throw new InvalidOperationException($"На курсі \"{course.Title}\" немає вільних місць.");

            var enrollment = new Enrollment(courseId, studentId);
            _enrollments.Add(enrollment);

            // Генерація події про запис студента на курс
            OnStudentEnrolled(new EducationEventArgs(
                "STUDENT_ENROLLED",
                $"Студента {student.FullName} (група {student.GroupName}) зараховано на курс \"{course.Title}\"",
                student.FullName));

            return enrollment;
        }

        public int GetEnrolledCount(int courseId) =>
            _enrollments.Find(e => e.CourseId == courseId).Count;

        // --- СЦЕНАРІЇ ДЕЛЕГУВАННЯ ПОВЕДІНКИ (Predicate, Func, Action) ---

        // Сценарій 1 (Predicate<T>): Динамічна фільтрація курсів за користувацьким критерієм
        public IReadOnlyList<Course> FilterCourses(Predicate<Course> criteria)
        {
            if (criteria is null) throw new ArgumentNullException(nameof(criteria));
            return _courses.GetAll().Where(c => criteria(c)).ToList().AsReadOnly();
        }

        // Сценарій 2 (Func<T, TKey> та Func<T, TResult>): Сортування курсів за довільною ознакою
        public IReadOnlyList<Course> GetSortedCourses<TKey>(Func<Course, TKey> keySelector, bool descending = false)
        {
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));

            var ordered = descending
                ? _courses.GetAll().OrderByDescending(keySelector)
                : _courses.GetAll().OrderBy(keySelector);

            return ordered.ToList().AsReadOnly();
        }

        // Сценарій 3 (Predicate<T> + Action<T>): Пакетна обробка відібраних студентів
        public int ProcessFilteredStudents(Predicate<Student> filter, Action<Student> action)
        {
            if (filter is null) throw new ArgumentNullException(nameof(filter));
            if (action is null) throw new ArgumentNullException(nameof(action));

            var matchedStudents = _participants.GetAll()
                .OfType<Student>()
                .Where(s => filter(s))
                .ToList();

            foreach (var student in matchedStudents)
            {
                action(student);
            }

            return matchedStudents.Count;
        }
    }

    // ==================== НЕЗАЛЕЖНІ ПІДПИСНИКИ (SUBSCRIBERS) ====================

    // Підписник №1: Журнал аудиту подій системи
    public class AuditLogSubscriber
    {
        private readonly List<string> _auditEntries = new();

        public void OnSystemEvent(object? sender, EducationEventArgs e)
        {
            string entry = $"[AUDIT {e.OccurredAt:HH:mm:ss}] [{e.EventType}] {e.Description}";
            _auditEntries.Add(entry);
            Console.WriteLine(entry);
        }

        public IReadOnlyList<string> Entries => _auditEntries.AsReadOnly();
    }

    // Підписник №2: Служба сповіщень учасників
    public class NotificationSubscriber
    {
        public void SendWelcomeNotification(object? sender, EducationEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.RecipientInfo))
            {
                Console.WriteLine($"  -> [SMS/Email для {e.RecipientInfo}]: Підтверджено