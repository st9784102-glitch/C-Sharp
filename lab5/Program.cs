using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace DomainModelDemo
{
    // ==================== БАЗОВІ СУТНОСТІ ПРЕДМЕТНОЇ ОБЛАСТІ ====================

    public static class IdGenerator
    {
        private static int _lastId = 0;
        public static int NextId() => Interlocked.Increment(ref _lastId);
    }

    public abstract class BaseEntity
    {
        public int Id { get; } = IdGenerator.NextId();
    }

    public class Course : BaseEntity
    {
        public string Title { get; set; }
        public int Hours { get; set; }
        public int MaxStudents { get; set; }
        public int? InstructorId { get; set; }

        public Course(string title, int hours, int maxStudents)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Назва курсу не може бути порожньою.");
            if (hours <= 0 || maxStudents <= 0)
                throw new ArgumentOutOfRangeException("Години та ліміт місць мають бути більшими за 0.");

            Title = title.Trim();
            Hours = hours;
            MaxStudents = maxStudents;
        }

        public override string ToString() =>
            $"[ID: {Id}] Курс \"{Title}\" ({Hours} год., ліміт: {MaxStudents} місць)";
    }

    public class Person : BaseEntity
    {
        public string FullName { get; set; }
        public int Age { get; set; }

        public Person(string fullName, int age)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                throw new ArgumentException("ПІБ не може бути порожнім.");

            FullName = fullName.Trim();
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
            $"[ID: {Id}] {FullName} | Група: {GroupName} | Середній бал: {AverageGrade:F1}";
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
        public DateTime EnrolledAt { get; } = DateTime.Now;

        public Enrollment(int courseId, int studentId)
        {
            CourseId = courseId;
            StudentId = studentId;
        }
    }

    // ==================== ВЛАСНИЙ КОНТЕЙНЕР ПРЕДМЕТНОЇ ОБЛАСТІ ====================

    public class EntityBag<T> : IEnumerable<T> where T : BaseEntity
    {
        private T[] _items;
        private int _count;
        private int _version; // Лічильник змін для захисту ітератора від модифікації під час foreach

        public int Count => _count;

        public EntityBag(int initialCapacity = 4)
        {
            if (initialCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity), "Початкова місткість має бути > 0.");

            _items = new T[initialCapacity];
            _count = 0;
            _version = 0;
        }

        // Індексатор для доступу за позицією
        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                    throw new IndexOutOfRangeException("Індекс виходить за межі колекції.");
                return _items[index];
            }
        }

        // 1. Додавання елемента з перевіркою унікальності ID та динамічним розширенням масиву
        public void Add(T entity)
        {
            if (entity is null)
                throw new ArgumentNullException(nameof(entity));

            for (int i = 0; i < _count; i++)
            {
                if (_items[i].Id == entity.Id)
                    throw new InvalidOperationException($"Об'єкт із ID={entity.Id} вже існує в контейнері.");
            }

            if (_count == _items.Length)
            {
                T[] expanded = new T[_items.Length * 2];
                Array.Copy(_items, expanded, _count);
                _items = expanded;
            }

            _items[_count++] = entity;
            _version++;
        }

        // 2. Базовий пошук за унікальним ідентифікатором
        public T? FindById(int id)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_items[i].Id == id)
                    return _items[i];
            }
            return null;
        }

        // 3. Пошук першого елемента за довільною умовою
        public T? FindFirst(Predicate<T> predicate)
        {
            if (predicate is null)
                throw new ArgumentNullException(nameof(predicate));

            for (int i = 0; i < _count; i++)
            {
                if (predicate(_items[i]))
                    return _items[i];
            }
            return null;
        }

        // Перевірка наявності елемента за умовою
        public bool Exists(Predicate<T> predicate) => FindFirst(predicate) is not null;

        // 4. Фільтрація елементів (повертає новий власний контейнер EntityBag<T>)
        public EntityBag<T> Filter(Predicate<T> predicate)
        {
            if (predicate is null)
                throw new ArgumentNullException(nameof(predicate));

            var filteredBag = new EntityBag<T>();
            for (int i = 0; i < _count; i++)
            {
                if (predicate(_items[i]))
                    filteredBag.Add(_items[i]);
            }
            return filteredBag;
        }

        // 5. Лінивий ітератор фільтрації через yield return (без виділення додаткової пам'яті)
        public IEnumerable<T> WhereLazy(Predicate<T> predicate)
        {
            if (predicate is null)
                throw new ArgumentNullException(nameof(predicate));

            int snapshotVersion = _version;
            for (int i = 0; i < _count; i++)
            {
                if (snapshotVersion != _version)
                    throw new InvalidOperationException("Колекцію було змінено під час перебору.");

                if (predicate(_items[i]))
                    yield return _items[i];
            }
        }

        // Реалізація IEnumerable<T> через yield return для підтримки циклу foreach
        public IEnumerator<T> GetEnumerator()
        {
            int snapshotVersion = _version;
            for (int i = 0; i < _count; i++)
            {
                if (snapshotVersion != _version)
                    throw new InvalidOperationException("Колекцію було змінено під час виконання foreach.");

                yield return _items[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // ==================== БІЗНЕС-СЕРВІС, ЩО ВИКОРИСТОВУЄ ENTITYBAG<T> ====================

    public class EducationService
    {
        // Основні сховища на базі власної колекції EntityBag<T>
        private readonly EntityBag<Course> _courses = new();
        private readonly EntityBag<Person> _participants = new();
        private readonly EntityBag<Enrollment> _enrollments = new();

        public Course CreateCourse(string title, int hours, int maxStudents)
        {
            if (_courses.Exists(c => c.Title.Equals(title.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Курс \"{title}\" вже існує.");

            var course = new Course(title, hours, maxStudents);
            _courses.Add(course);
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
            var course = _courses.FindById(courseId) ?? throw new KeyNotFoundException($"Курс ID={courseId} не знайдено.");
            var person = _participants.FindById(instructorId) ?? throw new KeyNotFoundException($"Учасника ID={instructorId} не знайдено.");

            if (person is not Instructor)
                throw new InvalidOperationException("Призначити на курс можна лише викладача.");

            course.InstructorId = instructorId;
        }

        public Enrollment EnrollStudentToCourse(int courseId, int studentId)
        {
            var course = _courses.FindById(courseId) ?? throw new KeyNotFoundException($"Курс ID={courseId} не знайдено.");
            var person = _participants.FindById(studentId) ?? throw new KeyNotFoundException($"Учасника ID={studentId} не знайдено.");

            if (person is not Student student)
                throw new InvalidOperationException("Записати на курс можна лише студента.");
            if (course.InstructorId is null)
                throw new InvalidOperationException($"На курс \"{course.Title}\" ще не призначено викладача.");
            if (_enrollments.Exists(e => e.CourseId == courseId && e.StudentId == studentId))
                throw new InvalidOperationException($"Студент {student.FullName} вже записаний на курс.");

            // Використання методу Filter() власної колекції для підрахунку зайнятих місць
            EntityBag<Enrollment> courseEnrollments = _enrollments.Filter(e => e.CourseId == courseId);
            if (courseEnrollments.Count >= course.MaxStudents)
                throw new InvalidOperationException($"На курсі \"{course.Title}\" немає вільних місць.");

            var enrollment = new Enrollment(courseId, studentId);
            _enrollments.Add(enrollment);
            return enrollment;
        }

        // Бізнес-сценарій: формування проміжного контейнера EntityBag<Student> для обраного курсу
        public EntityBag<Student> GetCourseGroupRoster(int courseId)
        {
            if (_courses.FindById(courseId) is null)
                throw new KeyNotFoundException($"Курс ID={courseId} не знайдено.");

            var groupRoster = new EntityBag<Student>();

            // Перебір власної колекції _enrollments через foreach
            foreach (Enrollment enrollment in _enrollments.WhereLazy(e => e.CourseId == courseId))
            {
                if (_participants.FindById(enrollment.StudentId) is Student student)
                {
                    groupRoster.Add(student);
                }
            }

            return groupRoster;
        }

        public EntityBag<Course> GetAllCourses() => _courses;
    }

    // ==================== ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ ====================

    internal class Program
    {
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var service = new EducationService();

            // 1. Наповнення системи сутностями
            var instructor = service.RegisterInstructor("Іван Петренко", 38, "Архітектура ПЗ");

            var s1 = service.RegisterStudent("Олександр Ковальчук", 19, "ІПЗ-21", 94.5);
            var s2 = service.RegisterStudent("Марія Ткаченко", 20, "КН-22", 89.0);
            var s3 = service.RegisterStudent("Дмитро Бондаренко", 19, "ІПЗ-21", 76.5);
            var s4 = service.RegisterStudent("Анна Мельник", 20, "КН-22", 91.0);

            var csharpCourse = service.CreateCourse("Колекції та ітератори в C#", 60, maxStudents: 10);
            var dbCourse = service.CreateCourse("Реляційні бази даних", 45, maxStudents: 15);

            service.AssignInstructorToCourse(csharpCourse.Id, instructor.Id);
            service.AssignInstructorToCourse(dbCourse.Id, instructor.Id);

            // Записуємо трьох студентів на курс C# і одного на БД
            service.EnrollStudentToCourse(csharpCourse.Id, s1.Id);
            service.EnrollStudentToCourse(csharpCourse.Id, s2.Id);
            service.EnrollStudentToCourse(csharpCourse.Id, s3.Id);
            service.EnrollStudentToCourse(dbCourse.Id, s4.Id);

            // 2. Перебір основного сховища курсів через foreach
            Console.WriteLine("=== 1. Перебір усіх курсів у власному контейнері EntityBag<Course> ===\n");
            foreach (Course course in service.GetAllCourses())
            {
                Console.WriteLine($"  * {course}");
            }

            // 3. Бізнес-сценарій: отримання та перебір групової відомості курсу (EntityBag<Student>)
            Console.WriteLine($"\n=== 2. Відомість студентів курсу \"{csharpCourse.Title}\" (foreach) ===\n");
            EntityBag<Student> csharpRoster = service.GetCourseGroupRoster(csharpCourse.Id);
            Console.WriteLine($"Усього студентів у відомості: {csharpRoster.Count}");

            foreach (Student student in csharpRoster)
            {
                Console.WriteLine($"  - {student}");
            }

            // 4. Пошук та фільтрація у власному контейнері
            Console.WriteLine("\n=== 3. Фільтрація відмінників курсу (бал >= 85) через EntityBag.Filter() ===\n");
            EntityBag<Student> honorsStudents = csharpRoster.Filter(s => s.AverageGrade >= 85.0);
            foreach (Student topStudent in honorsStudents)
            {
                Console.WriteLine($"  + {topStudent.FullName} (бал: {topStudent.AverageGrade})");
            }
        }
    }
}