using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DomainModelDemo
{
    // ==================== СТАТИЧНІ ІНФРАСТРУКТУРНІ МОДУЛІ ====================

    // 1. Статичний генератор послідовних ідентифікаторів
    public static class IdGenerator
    {
        private static int _lastId = 0;

        public static int NextId() => Interlocked.Increment(ref _lastId);

        public static void Reset() => Interlocked.Exchange(ref _lastId, 0);
    }

    // 2. Статичний модуль централізованої валідації
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
        // Readonly-ідентифікатор встановлюється один раз при створенні екземпляра
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

        public string GetCourseSummary() =>
            $"[ID: {Id}] Курс: \"{Title}\" ({Hours} год., ліміт: {MaxStudents} місць)";
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

        public virtual string GetInfo() =>
            $"[ID: {Id}] {FullName} ({Age} р.)";
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

        public override string GetInfo() =>
            $"{base.GetInfo()} | Студент, група: {GroupName}, бал: {AverageGrade}";
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

        public override string GetInfo() =>
            $"{base.GetInfo()} | Викладач ({Specialization}), ставка: {HourlyRate} грн/год";
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

    // Обмеження where T : BaseEntity гарантує наявність властивості/поля Id
    public class Repository<T> where T : BaseEntity
    {
        private readonly List<T> _items = new();

        public int Count => _items.Count;

        public void Add(T entity)
        {
            if (entity is null)
                throw new ArgumentNullException(nameof(entity));

            if (_items.Any(item => item.Id == entity.Id))
                throw new InvalidOperationException($"Сутність типу {typeof(T).Name} з ID={entity.Id} вже існує в репозиторії.");

            _items.Add(entity);
        }

        public T? GetById(int id) =>
            _items.FirstOrDefault(item => item.Id == id);

        public IReadOnlyList<T> GetAll() =>
            _items.AsReadOnly();

        public IReadOnlyList<T> Find(Func<T, bool> predicate) =>
            _items.Where(predicate).ToList().AsReadOnly();

        public bool Any(Func<T, bool> predicate) =>
            _items.Any(predicate);

        public bool Remove(int id)
        {
            var entity = GetById(id);
            if (entity is null)
                return false;

            return _items.Remove(entity);
        }
    }

    // ==================== СЕРВІСНИЙ ШАР ====================

    public class EducationService
    {
        // Використання одного узагальненого репозиторію для різних типів сутностей
        private readonly Repository<Course> _courseRepo = new();
        private readonly Repository<Person> _participantRepo = new();
        private readonly Repository<Enrollment> _enrollmentRepo = new();

        // --- CREATE ---

        public Course CreateCourse(string title, int hours, int maxStudents)
        {
            string cleanTitle = ValidationHelper.RequireNonEmpty(title, "Назва курсу");

            if (_courseRepo.Any(c => c.Title.Equals(cleanTitle, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Курс із назвою \"{cleanTitle}\" вже існує.");

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

        // --- READ ---

        public Course GetCourseById(int id) =>
            _courseRepo.GetById(id)
            ?? throw new KeyNotFoundException($"Курс із ID={id} не знайдено.");

        public Course? FindCourseByTitle(string titlePart)
        {
            string query = ValidationHelper.RequireNonEmpty(titlePart, "Пошуковий запит");
            return _courseRepo.Find(c => c.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        }

        public Person GetPersonById(int id) =>
            _participantRepo.GetById(id)
            ?? throw new KeyNotFoundException($"Учасника з ID={id} не знайдено.");

        public IReadOnlyList<Course> GetAllCourses() => _courseRepo.GetAll();
        public IReadOnlyList<Person> GetAllParticipants() => _participantRepo.GetAll();

        // --- UPDATE ---

        public void UpdateCourseDetails(int courseId, string newTitle, int newHours)
        {
            var course = GetCourseById(courseId);
            string cleanTitle = ValidationHelper.RequireNonEmpty(newTitle, "Нова назва курсу");

            if (_courseRepo.Any(c => c.Id != courseId && c.Title.Equals(cleanTitle, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Інший курс із назвою \"{cleanTitle}\" вже існує.");

            course.Title = cleanTitle;
            course.Hours = newHours;
        }

        // --- DELETE ---

        public void DeleteCourse(int courseId)
        {
            var course = GetCourseById(courseId);

            if (_enrollmentRepo.Any(e => e.CourseId == courseId))
                throw new InvalidOperationException("Неможливо видалити курс, на який вже записані студенти.");

            _courseRepo.Remove(course.Id);
        }

        // --- БІЗНЕС-СЦЕНАРІЙ ВЗАЄМОДІЇ СУТНОСТЕЙ ---

        public void AssignInstructorToCourse(int courseId, int instructorId)
        {
            var course = GetCourseById(courseId);
            var person = GetPersonById(instructorId);

            if (person is not Instructor)
                throw new InvalidOperationException("Призначити на курс можна лише користувача з роллю Викладач.");

            course.InstructorId = instructorId;
        }

        public Enrollment EnrollStudentToCourse(int courseId, int studentId)
        {
            var course = GetCourseById(courseId);
            var person = GetPersonById(studentId);

            if (person is not Student student)
                throw new InvalidOperationException("Записати на навчання можна лише користувача з роллю Студент.");

            if (course.InstructorId is null)
                throw new InvalidOperationException($"На курс \"{course.Title}\" ще не призначено викладача.");

            if (_enrollmentRepo.Any(e => e.CourseId == courseId && e.StudentId == studentId))
                throw new InvalidOperationException($"Студент {student.FullName} вже записаний на курс \"{course.Title}\".");

            int enrolledCount = _enrollmentRepo.Find(e => e.CourseId == courseId).Count;
            if (enrolledCount >= course.MaxStudents)
                throw new InvalidOperationException($"На курсі \"{course.Title}\" закінчилися вільні місця (ліміт: {course.MaxStudents}).");

            var enrollment = new Enrollment(courseId, studentId);
            _enrollmentRepo.Add(enrollment);
            return enrollment;
        }

        public string GetCourseRosterReport(int courseId)
        {
            var course = GetCourseById(courseId);
            string instructorName = course.InstructorId.HasValue
                ? GetPersonById(course.InstructorId.Value).FullName
                : "Не призначено";

            var enrolledStudents = _enrollmentRepo
                .Find(e => e.CourseId == courseId)
                .Select(e => GetPersonById(e.StudentId).FullName)
                .ToList();

            string studentsList = enrolledStudents.Count > 0
                ? string.Join(", ", enrolledStudents)
                : "поки немає";

            return $"{course.GetCourseSummary()}\n  Викладач: {instructorName}\n  Записані студенти ({enrolledStudents.Count}/{course.MaxStudents}): {studentsList}";
        }
    }

    // ==================== ТОЧКА ВХОДУ ====================

    internal class Program
    {
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var service = new EducationService();

            // 1. Створення сутностей (автоматична генерація послідовних ID та статична валідація)
            var instructor = service.RegisterInstructor("Іван Петренко", 38, "Архітектура ПЗ та .NET", 850m);
            var student1 = service.RegisterStudent("Олександр Ковальчук", 19, "ІПЗ-21", 92.5);
            var student2 = service.RegisterStudent("Марія Ткаченко", 20, "КН-22", 88.0);
            var student3 = service.RegisterStudent("Дмитро Бондаренко", 19, "ІПЗ-21", 81.0);

            var csharpCourse = service.CreateCourse("Об'єктно-орієнтоване програмування в C#", 60, maxStudents: 2);
            var tempCourse = service.CreateCourse("Тимчасовий курс", 15, maxStudents: 10);

            Console.WriteLine("=== 1. Зареєстровані учасники (Repository<Person>) ===");
            foreach (var p in service.GetAllParticipants())
                Console.WriteLine(p.GetInfo());

            // 2. Перевірка централізованої валідації та бізнес-обмежень
            Console.WriteLine("\n=== 2. Перевірка статичної валідації та бізнес-обмежень ===");
            ExecuteSafely(() => service.RegisterStudent("   ", 20, "ІПЗ-21", 85)); // Помилка валідації імені
            ExecuteSafely(() => service.EnrollStudentToCourse(csharpCourse.Id, student1.Id)); // Помилка: немає викладача

            // 3. Сценарій взаємодії сутностей
            Console.WriteLine("\n=== 3. Призначення викладача та запис студентів ===");
            service.AssignInstructorToCourse(csharpCourse.Id, instructor.Id);
            service.EnrollStudentToCourse(csharpCourse.Id, student1.Id);
            service.EnrollStudentToCourse(csharpCourse.Id, student2.Id);
            Console.WriteLine("Викладача призначено, двох студентів зараховано.");

            ExecuteSafely(() => service.EnrollStudentToCourse(csharpCourse.Id, student3.Id)); // Перевищення ліміту місць

            // 4. Оновлення та видалення через репозиторій
            Console.WriteLine("\n=== 4. Пошук, оновлення та видалення (Repository<Course>) ===");
            var found = service.FindCourseByTitle("C#");
            if (found != null)
            {
                service.UpdateCourseDetails(found.Id, "Узагальнення та архітектура в C#", 75);
                Console.WriteLine("Курс оновлено.");
            }

            ExecuteSafely(() => service.DeleteCourse(csharpCourse.Id)); // Заборонено: є записи студентів
            service.DeleteCourse(tempCourse.Id);                         // Успішно видалено
            Console.WriteLine("Порожній курс видалено.");

            // 5. Підсумковий звіт
            Console.WriteLine("\n=== 5. Підсумковий звіт ===");
            Console.WriteLine(service.GetCourseRosterReport(csharpCourse.Id));
        }

        private static void ExecuteSafely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Відхилено]: {ex.Message}");
            }
        }
    }
}