using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DomainModelDemo
{
    // ==================== ВЛАСНІ ДОМЕННІ ВИНЯТКИ ====================

    // Базовий виняток предметної області
    public class EducationDomainException : Exception
    {
        public string ErrorCode { get; }

        public EducationDomainException()
            : base("Порушено правило предметної області.")
        {
            ErrorCode = "DOMAIN_ERROR";
        }

        public EducationDomainException(string message, string errorCode = "DOMAIN_ERROR")
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public EducationDomainException(string message, Exception innerException, string errorCode = "DOMAIN_ERROR")
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }

    // Виняток №1: Перевищення ліміту місць на курсі
    public class CourseCapacityExceededException : EducationDomainException
    {
        public int CourseId { get; }
        public string CourseTitle { get; }
        public int MaxCapacity { get; }

        public CourseCapacityExceededException()
            : base("На курсі закінчилися вільні місця.", "COURSE_CAPACITY_EXCEEDED") { }

        public CourseCapacityExceededException(string message)
            : base(message, "COURSE_CAPACITY_EXCEEDED") { }

        public CourseCapacityExceededException(string message, Exception inner)
            : base(message, inner, "COURSE_CAPACITY_EXCEEDED") { }

        public CourseCapacityExceededException(int courseId, string courseTitle, int maxCapacity)
            : base($"На курсі \"{courseTitle}\" (ID={courseId}) досягнуто максимального ліміту місць ({maxCapacity}).",
                   "COURSE_CAPACITY_EXCEEDED")
        {
            CourseId = courseId;
            CourseTitle = courseTitle;
            MaxCapacity = maxCapacity;
        }
    }

    // Виняток №2: Повторний запис студента на той самий курс
    public class DuplicateEnrollmentException : EducationDomainException
    {
        public int CourseId { get; }
        public int StudentId { get; }

        public DuplicateEnrollmentException()
            : base("Студент уже записаний на цей курс.", "DUPLICATE_ENROLLMENT") { }

        public DuplicateEnrollmentException(string message)
            : base(message, "DUPLICATE_ENROLLMENT") { }

        public DuplicateEnrollmentException(string message, Exception inner)
            : base(message, inner, "DUPLICATE_ENROLLMENT") { }

        public DuplicateEnrollmentException(int courseId, int studentId, string studentName, string courseTitle)
            : base($"Студент {studentName} (ID={studentId}) вже записаний на курс \"{courseTitle}\" (ID={courseId}).",
                   "DUPLICATE_ENROLLMENT")
        {
            CourseId = courseId;
            StudentId = studentId;
        }
    }

    // Виняток №3: Порушення передумов операції над курсом
    public class CoursePreconditionException : EducationDomainException
    {
        public CoursePreconditionException()
            : base("Не виконано обов'язкові передумови для операції з курсом.", "COURSE_PRECONDITION_FAILED") { }

        public CoursePreconditionException(string message)
            : base(message, "COURSE_PRECONDITION_FAILED") { }

        public CoursePreconditionException(string message, Exception inner)
            : base(message, inner, "COURSE_PRECONDITION_FAILED") { }
    }

    // ==================== FAULT-TOLERANT ПАТЕРН RESULT<T> ====================

    public class Result<T>
    {
        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;
        public T? Value { get; }
        public string ErrorMessage { get; }
        public string ErrorCode { get; }

        private Result(bool isSuccess, T? value, string errorMessage, string errorCode)
        {
            IsSuccess = isSuccess;
            Value = value;
            ErrorMessage = errorMessage;
            ErrorCode = errorCode;
        }

        public static Result<T> Success(T value) =>
            new(true, value, string.Empty, string.Empty);

        public static Result<T> Failure(string errorMessage, string errorCode = "OPERATION_FAILED") =>
            new(false, default, errorMessage, errorCode);
    }

    // ==================== СТАТИЧНІ МОДУЛІ ТА БАЗОВІ СУТНОСТІ ====================

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

    public abstract class BaseEntity
    {
        public readonly int Id;
        protected BaseEntity() => Id = IdGenerator.NextId();
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

        public virtual string GetInfo() => $"[ID: {Id}] {FullName} ({Age} р.)";
    }

    public class Student : Person
    {
        public string GroupName { get; set; }
        public double AverageGrade { get; set; }

        public Student(string fullName, int age, string groupName, double averageGrade)
            : base(fullName, age)
        {
            GroupName = ValidationHelper.RequireNonEmpty(groupName, "Група");
            AverageGrade = ValidationHelper.RequireInRange(averageGrade, 0, 100, "Середній бал");
        }

        public override string GetInfo() =>
            $"{base.GetInfo()} | Студент ({GroupName}, бал: {AverageGrade})";
    }

    public class Instructor : Person
    {
        public string Specialization { get; set; }
        public decimal HourlyRate { get; set; }

        public Instructor(string fullName, int age, string specialization, decimal hourlyRate)
            : base(fullName, age)
        {
            Specialization = ValidationHelper.RequireNonEmpty(specialization, "Спеціалізація");
            HourlyRate = ValidationHelper.RequireNonNegative(hourlyRate, "Ставка");
        }

        public override string GetInfo() =>
            $"{base.GetInfo()} | Викладач ({Specialization})";
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

        public void Add(T entity)
        {
            if (entity is null) throw new ArgumentNullException(nameof(entity));
            _items.Add(entity);
        }

        public T? GetById(int id) => _items.FirstOrDefault(x => x.Id == id);
        public IReadOnlyList<T> GetAll() => _items.AsReadOnly();
        public IReadOnlyList<T> Find(Func<T, bool> predicate) => _items.Where(predicate).ToList().AsReadOnly();
        public bool Any(Func<T, bool> predicate) => _items.Any(predicate);
        public bool Remove(int id)
        {
            var entity = GetById(id);
            return entity is not null && _items.Remove(entity);
        }
    }

    // ==================== СЕРВІСНИЙ ШАР (EXCEPTIONS + RESULT<T>) ====================

    public class EducationService
    {
        private readonly Repository<Course> _courseRepo = new();
        private readonly Repository<Person> _participantRepo = new();
        private readonly Repository<Enrollment> _enrollmentRepo = new();

        public Course CreateCourse(string title, int hours, int maxStudents)
        {
            string cleanTitle = ValidationHelper.RequireNonEmpty(title, "Назва курсу");

            if (_courseRepo.Any(c => c.Title.Equals(cleanTitle, StringComparison.OrdinalIgnoreCase)))
                throw new CoursePreconditionException($"Курс із назвою \"{cleanTitle}\" вже зареєстровано в системі.");

            var course = new Course(cleanTitle, hours, maxStudents);
            _courseRepo.Add(course);
            return course;
        }

        public Student RegisterStudent(string fullName, int age, string groupName, double averageGrade)
        {
            var student = new Student(fullName, age, groupName, averageGrade);
            _participantRepo.Add(student);
            return student;
        }

        public Instructor RegisterInstructor(string fullName, int age, string specialization, decimal hourlyRate)
        {
            var instructor = new Instructor(fullName, age, specialization, hourlyRate);
            _participantRepo.Add(instructor);
            return instructor;
        }

        public Course GetCourseById(int id) =>
            _courseRepo.GetById(id) ?? throw new KeyNotFoundException($"Курс із ID={id} не знайдено.");

        public Person GetPersonById(int id) =>
            _participantRepo.GetById(id) ?? throw new KeyNotFoundException($"Учасника з ID={id} не знайдено.");

        public void AssignInstructorToCourse(int courseId, int instructorId)
        {
            var course = GetCourseById(courseId);
            var person = GetPersonById(instructorId);

            if (person is not Instructor)
                throw new CoursePreconditionException("Призначити викладачем курсу можна лише користувача з роллю Instructor.");

            course.InstructorId = instructorId;
        }

        // 1. Класичний підхід: генерація власних доменних винятків при порушенні правил
        public Enrollment EnrollStudentToCourse(int courseId, int studentId)
        {
            var course = GetCourseById(courseId);
            var person = GetPersonById(studentId);

            if (person is not Student student)
                throw new CoursePreconditionException($"Користувач {person.FullName} не є студентом і не може бути зарахований на курс.");

            if (course.InstructorId is null)
                throw new CoursePreconditionException($"Неможливо відкрити запис на курс \"{course.Title}\", оскільки не призначено викладача.");

            if (_enrollmentRepo.Any(e => e.CourseId == courseId && e.StudentId == studentId))
                throw new DuplicateEnrollmentException(course.Id, student.Id, student.FullName, course.Title);

            int currentEnrolled = _enrollmentRepo.Find(e => e.CourseId == courseId).Count;
            if (currentEnrolled >= course.MaxStudents)
                throw new CourseCapacityExceededException(course.Id, course.Title, course.MaxStudents);

            var enrollment = new Enrollment(courseId, studentId);
            _enrollmentRepo.Add(enrollment);
            return enrollment;
        }

        public void DeleteCourse(int courseId)
        {
            var course = GetCourseById(courseId);

            if (_enrollmentRepo.Any(e => e.CourseId == courseId))
                throw new CoursePreconditionException($"Заборонено видаляти курс \"{course.Title}\", на який вже записані студенти.");

            _courseRepo.Remove(courseId);
        }

        // 2. Fault-tolerant підхід: безпечний метод, що повертає Result<T> для очікуваних відмов
        public Result<Enrollment> TryEnrollStudentToCourse(int courseId, int studentId)
        {
            try
            {
                var enrollment = EnrollStudentToCourse(courseId, studentId);
                return Result<Enrollment>.Success(enrollment);
            }
            catch (EducationDomainException ex)
            {
                // Очікуване порушення бізнес-правила повертаємо як контрольований результат
                return Result<Enrollment>.Failure(ex.Message, ex.ErrorCode);
            }
            catch (KeyNotFoundException ex)
            {
                return Result<Enrollment>.Failure(ex.Message, "ENTITY_NOT_FOUND");
            }
        }

        public Result<Course> TryCreateCourse(string title, int hours, int maxStudents)
        {
            try
            {
                var course = CreateCourse(title, hours, maxStudents);
                return Result<Course>.Success(course);
            }
            catch (EducationDomainException ex)
            {
                return Result<Course>.Failure(ex.Message, ex.ErrorCode);
            }
            catch (ArgumentException ex)
            {
                return Result<Course>.Failure(ex.Message, "VALIDATION_ERROR");
            }
        }
    }

    // ==================== ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ ====================

    internal class Program
    {
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var service = new EducationService();

            var instructor = service.RegisterInstructor("Іван Петренко", 38, "Архітектура .NET", 850m);
            var s1 = service.RegisterStudent("Олександр Ковальчук", 19, "ІПЗ-21", 92.5);
            var s2 = service.RegisterStudent("Марія Ткаченко", 20, "КН-22", 88.0);
            var s3 = service.RegisterStudent("Дмитро Бондаренко", 19, "ІПЗ-21", 81.0);

            var course = service.CreateCourse("Обробка винятків та надійність у C#", 45, maxStudents: 2);

            Console.WriteLine("=== ЧАСТИНА 1. Винятки як помилки виконання (try / catch / finally) ===");

            // Спроба 1: Помилка валідації вхідних даних (ArgumentOutOfRangeException)
            ExecuteWithDetailedCatch(() =>
                service.RegisterStudent("Некоректний Студент", 12, "ІПЗ-21", 75.0));

            // Спроба 2: Порушення передумови (CoursePreconditionException — немає викладача)
            ExecuteWithDetailedCatch(() =>
                service.EnrollStudentToCourse(course.Id, s1.Id));

            // Коректний сценарій: призначаємо викладача та записуємо двох студентів
            service.AssignInstructorToCourse(course.Id, instructor.Id);
            service.EnrollStudentToCourse(course.Id, s1.Id);
            service.EnrollStudentToCourse(course.Id, s2.Id);
            Console.WriteLine("[Успіх]: Викладача призначено, 2 студентів зараховано.");

            // Спроба 3: Дублювання запису (DuplicateEnrollmentException)
            ExecuteWithDetailedCatch(() =>
                service.EnrollStudentToCourse(course.Id, s1.Id));

            // Спроба 4: Перевищення ліміту місць (CourseCapacityExceededException)
            ExecuteWithDetailedCatch(() =>
                service.EnrollStudentToCourse(course.Id, s3.Id));

            // Спроба 5: Видалення активного курсу (CoursePreconditionException)
            ExecuteWithDetailedCatch(() =>
                service.DeleteCourse(course.Id));

            Console.WriteLine("\n=== ЧАСТИНА 2. Fault-tolerant підхід через Result<T> (очікувані ситуації) ===");

            // Безпечне створення курсу з некоректними даними — програма не генерує виняток назовні
            var invalidCourseResult = service.TryCreateCourse("   ", -10, 5);
            PrintResult(invalidCourseResult, c => $"Створено курс \"{c.Title}\"");

            // Безпечний масовий запис студентів (частина успішна, частина відхилена штатно через Result<T>)
            var newCourseResult = service.TryCreateCourse("Тестування ПЗ", 30, maxStudents: 1);
            if (newCourseResult.IsSuccess && newCourseResult.Value is not null)
            {
                var qaCourse = newCourseResult.Value;
                service.AssignInstructorToCourse(qaCourse.Id, instructor.Id);

                int[] candidateIds = { s1.Id, s1.Id, s2.Id, 9999 };
                foreach (int studentId in candidateIds)
                {
                    Result<Enrollment> enrollResult = service.TryEnrollStudentToCourse(qaCourse.Id, studentId);
                    PrintResult(enrollResult, e => $"Запис #{e.Id} успішно оформлено для студента ID={e.StudentId}");
                }
            }
        }

        // Демонстрація порядку блоків catch: від найбільш специфічних до загальних + блок finally
        private static void ExecuteWithDetailedCatch(Action action)
        {
            try
            {
                action();
            }
            catch (CourseCapacityExceededException ex)
            {
                Console.WriteLine($"[Доменна помилка — ліміт місць | {ex.ErrorCode}]: {ex.Message}");
            }
            catch (DuplicateEnrollmentException ex)
            {
                Console.WriteLine($"[Доменна помилка — дублікат | {ex.ErrorCode}]: {ex.Message}");
            }
            catch (CoursePreconditionException ex)
            {
                Console.WriteLine($"[Доменна помилка — передумова | {ex.ErrorCode}]: {ex.Message}");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"[Помилка валідації аргументу]: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Непередбачена системна помилка]: {ex.Message}");
            }
            finally
            {
                // Гарантовано виконується незалежно від того, чи виник виняток
                Console.WriteLine("  -> [finally]: Перевірку операції завершено, стан системи стабільний.");
            }
        }

        private static void PrintResult<T>(Result<T> result, Func<T, string> successFormatter)
        {
            if (result.IsSuccess && result.Value is not null)
                Console.WriteLine($"[Result.Success]: {successFormatter(result.Value)}");
            else
                Console.WriteLine($"[Result.Failure | {result.ErrorCode}]: {result.ErrorMessage}");
        }
    }
}