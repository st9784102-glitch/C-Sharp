using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DomainModelDemo
{
    // ==================== ІНТЕРФЕЙСИ-КОНТРАКТИ ====================

    // Контракт №1: Сутність з унікальним ідентифікатором
    public interface IEntity
    {
        int Id { get; }
    }

    // Контракт №2: Узагальнений репозиторій (розширює IEnumerable<T> для підтримки foreach)
    public interface IRepository<T> : IEnumerable<T> where T : IEntity
    {
        int Count { get; }
        void Add(T item);
        T? GetById(int id);
        bool Remove(int id);
        IEnumerable<T> WhereLazy(Func<T, bool> predicate);
    }

    // Контракт №3: Служба сповіщень учасників навчального процесу
    public interface INotificationService
    {
        void Notify(string recipient, string message);
    }

    // Контракт №4: Валідатор бізнес-правил сутності
    public interface IValidator<in T>
    {
        void Validate(T entity);
    }

    // ==================== ВЛАСНА КОЛЕКЦІЯ З IENUMERABLE<T> ТА YIELD RETURN ====================

    public class DomainCollection<T> : IRepository<T> where T : IEntity
    {
        private T[] _buffer;
        private int _count;

        public int Count => _count;

        public DomainCollection(int initialCapacity = 4)
        {
            if (initialCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));

            _buffer = new T[initialCapacity];
            _count = 0;
        }

        public void Add(T item)
        {
            if (item is null)
                throw new ArgumentNullException(nameof(item));

            for (int i = 0; i < _count; i++)
            {
                if (_buffer[i].Id == item.Id)
                    throw new InvalidOperationException($"Елемент із ID={item.Id} уже міститься у колекції.");
            }

            if (_count == _buffer.Length)
                Array.Resize(ref _buffer, _buffer.Length * 2);

            _buffer[_count++] = item;
        }

        public T? GetById(int id)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_buffer[i].Id == id)
                    return _buffer[i];
            }
            return default;
        }

        public bool Remove(int id)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_buffer[i].Id == id)
                {
                    for (int j = i; j < _count - 1; j++)
                        _buffer[j] = _buffer[j + 1];

                    _buffer[--_count] = default!;
                    return true;
                }
            }
            return false;
        }

        // Лінивий потоковий ітератор фільтрації через yield return (не виділяє пам'ять під новий список)
        public IEnumerable<T> WhereLazy(Func<T, bool> predicate)
        {
            if (predicate is null)
                throw new ArgumentNullException(nameof(predicate));

            for (int i = 0; i < _count; i++)
            {
                if (predicate(_buffer[i]))
                    yield return _buffer[i];
            }
        }

        // Реалізація IEnumerable<T> через yield return для підтримки циклу foreach
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
            {
                yield return _buffer[i];
            }
        }

        // Неузагальнена версія для сумісності з IEnumerable
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // ==================== МОДЕЛІ ПРЕДМЕТНОЇ ОБЛАСТІ ====================

    public static class IdGenerator
    {
        private static int _lastId = 0;
        public static int NextId() => Interlocked.Increment(ref _lastId);
    }

    public abstract class BaseEntity : IEntity
    {
        public int Id { get; } = IdGenerator.NextId();
    }

    public class Course : BaseEntity, IComparable<Course>
    {
        public string Title { get; set; }
        public int Hours { get; set; }
        public int MaxStudents { get; set; }
        public int? InstructorId { get; set; }

        public Course(string title, int hours, int maxStudents)
        {
            Title = title;
            Hours = hours;
            MaxStudents = maxStudents;
        }

        // Реалізація стандартного контракту IComparable<Course> для сортування
        public int CompareTo(Course? other) =>
            other is null ? 1 : Hours.CompareTo(other.Hours);

        public override string ToString() =>
            $"[ID: {Id}] \"{Title}\" ({Hours} год., місць: {MaxStudents})";
    }

    public class Person : BaseEntity
    {
        public string FullName { get; set; }
        public int Age { get; set; }

        public Person(string fullName, int age)
        {
            FullName = fullName;
            Age = age;
        }
    }

    public class Student : Person
    {
        public string GroupName { get; set; }
        public double AverageGrade { get; set; }

        public Student(string fullName, int age, string groupName, double averageGrade)
            : base(fullName, age)
        {
            GroupName = groupName;
            AverageGrade = averageGrade;
        }

        public override string ToString() =>
            $"[ID: {Id}] Студент: {FullName}, група {GroupName}, бал: {AverageGrade}";
    }

    public class Instructor : Person
    {
        public string Specialization { get; set; }

        public Instructor(string fullName, int age, string specialization)
            : base(fullName, age)
        {
            Specialization = specialization;
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

    // ==================== РЕАЛІЗАЦІЇ КОНТРАКТІВ (ВАЛІДАТОРИ ТА СПОВІЩЕННЯ) ====================

    public class CourseValidator : IValidator<Course>
    {
        public void Validate(Course course)
        {
            if (string.IsNullOrWhiteSpace(course.Title))
                throw new ArgumentException("Назва курсу не може бути порожньою.");
            if (course.Hours <= 0)
                throw new ArgumentOutOfRangeException(nameof(course.Hours), "Тривалість курсу має бути більшою за 0.");
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
            if (student.Age < 16 || student.Age > 100)
                throw new ArgumentOutOfRangeException(nameof(student.Age), "Вік студента має бути в межах 16–100 років.");
            if (student.AverageGrade < 0 || student.AverageGrade > 100)
                throw new ArgumentOutOfRangeException(nameof(student.AverageGrade), "Середній бал має бути від 0 до 100.");
        }
    }

    // Реалізація №1: Консольні сповіщення
    public class ConsoleNotificationService : INotificationService
    {
        public void Notify(string recipient, string message) =>
            Console.WriteLine($"[Console Notify -> {recipient}]: {message}");
    }

    // Реалізація №2: Накопичувальний сервіс сповіщень (зручний для історії або тестування)
    public class InMemoryNotificationService : INotificationService
    {
        private readonly List<string> _sentMessages = new();
        public IReadOnlyList<string> SentMessages => _sentMessages.AsReadOnly();

        public void Notify(string recipient, string message) =>
            _sentMessages.Add($"{recipient}: {message}");
    }

    // ==================== СЕРВІС, ЩО ЗАЛЕЖИТЬ ЛИШЕ ВІД ІНТЕРФЕЙСІВ ====================

    public class EducationService
    {
        private readonly IRepository<Course> _courseRepo;
        private readonly IRepository<Person> _participantRepo;
        private readonly IRepository<Enrollment> _enrollmentRepo;
        private readonly IValidator<Course> _courseValidator;
        private readonly IValidator<Student> _studentValidator;
        private readonly INotificationService _notificationService;

        // Впровадження залежностей через контракти (Dependency Injection)
        public EducationService(
            IRepository<Course> courseRepo,
            IRepository<Person> participantRepo,
            IRepository<Enrollment> enrollmentRepo,
            IValidator<Course> courseValidator,
            IValidator<Student> studentValidator,
            INotificationService notificationService)
        {
            _courseRepo = courseRepo;
            _participantRepo = participantRepo;
            _enrollmentRepo = enrollmentRepo;
            _courseValidator = courseValidator;
            _studentValidator = studentValidator;
            _notificationService = notificationService;
        }

        public Course CreateCourse(string title, int hours, int maxStudents)
        {
            var course = new Course(title.Trim(), hours, maxStudents);
            _courseValidator.Validate(course);

            if (_courseRepo.WhereLazy(c => c.Title.Equals(course.Title, StringComparison.OrdinalIgnoreCase)).Any())
                throw new InvalidOperationException($"Курс \"{course.Title}\" уже існує.");

            _courseRepo.Add(course);
            return course;
        }

        public Student RegisterStudent(string fullName, int age, string groupName, double averageGrade)
        {
            var student = new Student(fullName.Trim(), age, groupName.Trim(), averageGrade);
            _studentValidator.Validate(student);
            _participantRepo.Add(student);
            return student;
        }

        public Instructor RegisterInstructor(string fullName, int age, string specialization)
        {
            var instructor = new Instructor(fullName.Trim(), age, specialization.Trim());
            _participantRepo.Add(instructor);
            return instructor;
        }

        public void AssignInstructorToCourse(int courseId, int instructorId)
        {
            var course = _courseRepo.GetById(courseId) ?? throw new KeyNotFoundException($"Курс ID={courseId} не знайдено.");
            var person = _participantRepo.GetById(instructorId) ?? throw new KeyNotFoundException($"Учасника ID={instructorId} не знайдено.");

            if (person is not Instructor instructor)
                throw new InvalidOperationException("Призначити на курс можна лише викладача.");

            course.InstructorId = instructorId;
            _notificationService.Notify(instructor.FullName, $"Вас призначено викладачем курсу \"{course.Title}\".");
        }

        public Enrollment EnrollStudentToCourse(int courseId, int studentId)
        {
            var course = _courseRepo.GetById(courseId) ?? throw new KeyNotFoundException($"Курс ID={courseId} не знайдено.");
            var person = _participantRepo.GetById(studentId) ?? throw new KeyNotFoundException($"Учасника ID={studentId} не знайдено.");

            if (person is not Student student)
                throw new InvalidOperationException("Записати на курс можна лише студента.");
            if (course.InstructorId is null)
                throw new InvalidOperationException($"На курс \"{course.Title}\" не призначено викладача.");
            if (_enrollmentRepo.WhereLazy(e => e.CourseId == courseId && e.StudentId == studentId).Any())
                throw new InvalidOperationException($"Студент {student.FullName} вже записаний на курс.");
            if (_enrollmentRepo.WhereLazy(e => e.CourseId == courseId).Count() >= course.MaxStudents)
                throw new InvalidOperationException($"На курсі \"{course.Title}\" немає вільних місць.");

            var enrollment = new Enrollment(courseId, studentId);
            _enrollmentRepo.Add(enrollment);
            _notificationService.Notify(student.FullName, $"Ви успішно зараховані на курс \"{course.Title}\".");
            return enrollment;
        }
    }

    // ==================== ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ ====================

    internal class Program
    {
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // Створюємо власні колекції DomainCollection<T>
            IRepository<Course> courseCollection = new DomainCollection<Course>(initialCapacity: 2);
            IRepository<Person> participantCollection = new DomainCollection<Person>(initialCapacity: 2);
            IRepository<Enrollment> enrollmentCollection = new DomainCollection<Enrollment>(initialCapacity: 2);

            // Конфігуруємо сервіс через інтерфейси
            var service = new EducationService(
                courseCollection,
                participantCollection,
                enrollmentCollection,
                new CourseValidator(),
                new StudentValidator(),
                new ConsoleNotificationService());

            Console.WriteLine("=== 1. Виконання операцій через контракти інтерфейсів ===\n");

            var instructor = service.RegisterInstructor("Іван Петренко", 38, "Архітектура .NET");
            var s1 = service.RegisterStudent("Олександр Ковальчук", 19, "ІПЗ-21", 93.5);
            var s2 = service.RegisterStudent("Марія Ткаченко", 20, "КН-22", 89.0);
            var s3 = service.RegisterStudent("Дмитро Бондаренко", 19, "ІПЗ-21", 78.5);

            var c1 = service.CreateCourse("Інтерфейси та ітератори в C#", 45, 10);
            var c2 = service.CreateCourse("Бази даних", 60, 5);
            var c3 = service.CreateCourse("Алгоритми", 30, 8);

            service.AssignInstructorToCourse(c1.Id, instructor.Id);
            service.EnrollStudentToCourse(c1.Id, s1.Id);
            service.EnrollStudentToCourse(c1.Id, s2.Id);

            // 2. Демонстрація перебору власної колекції DomainCollection<T> через foreach (IEnumerable<T>)
            Console.WriteLine("\n=== 2. Перебір власної колекції курсів через foreach (IEnumerable<T>) ===\n");
            foreach (Course course in courseCollection)
            {
                Console.WriteLine($"  * {course}");
            }

            // 3. Демонстрація лінивого ітератора WhereLazy (yield return)
            Console.WriteLine("\n=== 3. Лінива фільтрація учасників через yield return (WhereLazy) ===\n");
            foreach (Person person in participantCollection.WhereLazy(p => p is Student st && st.AverageGrade >= 85.0))
            {
                Console.WriteLine($"  * Відмінник: {person}");
            }

            // 4. Демонстрація легкої заміни реалізації інтерфейсу INotificationService без зміни EducationService
            Console.WriteLine("\n=== 4. Підміна реалізації INotificationService на InMemoryNotificationService ===\n");
            var inMemoryNotifier = new InMemoryNotificationService();
            var isolatedService = new EducationService(
                courseCollection,
                participantCollection,
                enrollmentCollection,
                new CourseValidator(),
                new StudentValidator(),
                inMemoryNotifier);

            isolatedService.EnrollStudentToCourse(c1.Id, s3.Id);
            Console.WriteLine($"Повідомлень накопичено в пам'яті: {inMemoryNotifier.SentMessages.Count}");
            foreach (string msg in inMemoryNotifier.SentMessages)
                Console.WriteLine($"  -> {msg}");
        }
    }
}