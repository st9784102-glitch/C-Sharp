using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainModelDemo
{
    // ==================== СУТНОСТІ ПРЕДМЕТНОЇ ОБЛАСТІ ====================

    public abstract class BaseEntity
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    public class Course : BaseEntity
    {
        private string _title;
        private int _hours;
        private int _maxStudents;

        public string Title
        {
            get => _title;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Назва курсу не може бути порожньою.");
                _title = value.Trim();
            }
        }

        public int Hours
        {
            get => _hours;
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(Hours), "Тривалість має бути > 0.");
                _hours = value;
            }
        }

        public int MaxStudents
        {
            get => _maxStudents;
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(MaxStudents), "Ліміт місць має бути > 0.");
                _maxStudents = value;
            }
        }

        // Посилання на призначеного викладача
        public Guid? InstructorId { get; set; }

        public Course(string title, int hours, int maxStudents)
        {
            Title = title;
            Hours = hours;
            MaxStudents = maxStudents;
        }

        public string GetCourseSummary() =>
            $"[{Id.ToString()[..8]}] Курс: \"{Title}\" ({Hours} год., ліміт: {MaxStudents} місць)";
    }

    public class Person : BaseEntity
    {
        private string _fullName;
        private int _age;

        public string FullName
        {
            get => _fullName;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Ім'я не може бути порожнім.");
                _fullName = value.Trim();
            }
        }

        public int Age
        {
            get => _age;
            set
            {
                if (value < 16 || value > 100)
                    throw new ArgumentOutOfRangeException(nameof(Age), "Вік має бути в межах 16–100 років.");
                _age = value;
            }
        }

        public Person(string fullName, int age)
        {
            FullName = fullName;
            Age = age;
        }

        public virtual string GetInfo() =>
            $"[{Id.ToString()[..8]}] {FullName} ({Age} р.)";
    }

    public class Student : Person
    {
        private double _averageGrade;

        public string GroupName { get; set; }

        public double AverageGrade
        {
            get => _averageGrade;
            set
            {
                if (value < 0 || value > 100)
                    throw new ArgumentOutOfRangeException(nameof(AverageGrade), "Оцінка має бути від 0 до 100.");
                _averageGrade = value;
            }
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
        private decimal _hourlyRate;

        public string Specialization { get; set; }

        public decimal HourlyRate
        {
            get => _hourlyRate;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(HourlyRate), "Ставка не може бути від'ємною.");
                _hourlyRate = value;
            }
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

    // Сутність, що зв'язує Студента, Курс та Викладача
    public class Enrollment : BaseEntity
    {
        public Guid CourseId { get; }
        public Guid StudentId { get; }
        public DateTime EnrolledAt { get; }

        public Enrollment(Guid courseId, Guid studentId)
        {
            CourseId = courseId;
            StudentId = studentId;
            EnrolledAt = DateTime.Now;
        }
    }

    // ==================== СЕРВІСНИЙ ШАР ====================

    public class EducationService
    {
        private readonly List<Course> _courses = new();
        private readonly List<Person> _participants = new();
        private readonly List<Enrollment> _enrollments = new();

        // --- CREATE (Створення сутностей з перевіркою бізнес-обмежень) ---

        public Course CreateCourse(string title, int hours, int maxStudents)
        {
            if (_courses.Any(c => c.Title.Equals(title.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Курс із назвою \"{title}\" вже існує.");

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

        public Instructor RegisterInstructor(string fullName, int age, string specialization, decimal hourlyRate)
        {
            var instructor = new Instructor(fullName, age, specialization, hourlyRate);
            _participants.Add(instructor);
            return instructor;
        }

        // --- READ (Пошук за ID, атрибутом та отримання списків) ---

        public Course GetCourseById(Guid id) =>
            _courses.FirstOrDefault(c => c.Id == id)
            ?? throw new KeyNotFoundException($"Курс із ID {id} не знайдено.");

        public Course? FindCourseByTitle(string titlePart) =>
            _courses.FirstOrDefault(c => c.Title.Contains(titlePart, StringComparison.OrdinalIgnoreCase));

        public Person GetPersonById(Guid id) =>
            _participants.FirstOrDefault(p => p.Id == id)
            ?? throw new KeyNotFoundException($"Учасника з ID {id} не знайдено.");

        public IReadOnlyList<Course> GetAllCourses() => _courses.AsReadOnly();
        public IReadOnlyList<Person> GetAllParticipants() => _participants.AsReadOnly();

        // --- UPDATE (Оновлення даних сутності) ---

        public void UpdateCourseDetails(Guid courseId, string newTitle, int newHours)
        {
            var course = GetCourseById(courseId);

            if (_courses.Any(c => c.Id != courseId && c.Title.Equals(newTitle.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Інший курс із назвою \"{newTitle}\" вже існує.");

            course.Title = newTitle;
            course.Hours = newHours;
        }

        // --- DELETE (Видалення з перевіркою цілісності) ---

        public void DeleteCourse(Guid courseId)
        {
            var course = GetCourseById(courseId);

            if (_enrollments.Any(e => e.CourseId == courseId))
                throw new InvalidOperationException("Неможливо видалити курс, на який вже записані студенти.");

            _courses.Remove(course);
        }

        // --- КОМПЛЕКСНИЙ БІЗНЕС-СЦЕНАРІЙ (Взаємодія 3+ сутностей) ---

        public void AssignInstructorToCourse(Guid courseId, Guid instructorId)
        {
            var course = GetCourseById(courseId);
            var person = GetPersonById(instructorId);

            if (person is not Instructor)
                throw new InvalidOperationException("Призначити на курс можна лише користувача з роллю Викладач.");

            course.InstructorId = instructorId;
        }

        public Enrollment EnrollStudentToCourse(Guid courseId, Guid studentId)
        {
            var course = GetCourseById(courseId);
            var person = GetPersonById(studentId);

            if (person is not Student student)
                throw new InvalidOperationException("Записати на навчання можна лише користувача з роллю Студент.");

            // Бізнес-обмеження 1: курс повинен мати викладача
            if (course.InstructorId is null)
                throw new InvalidOperationException($"На курс \"{course.Title}\" ще не призначено викладача.");

            // Бізнес-обмеження 2: перевірка повторного запису
            if (_enrollments.Any(e => e.CourseId == courseId && e.StudentId == studentId))
                throw new InvalidOperationException($"Студент {student.FullName} вже записаний на курс \"{course.Title}\".");

            // Бізнес-обмеження 3: перевірка ліміту місць
            int currentCount = _enrollments.Count(e => e.CourseId == courseId);
            if (currentCount >= course.MaxStudents)
                throw new InvalidOperationException($"На курсі \"{course.Title}\" закінчилися вільні місця (ліміт: {course.MaxStudents}).");

            var enrollment = new Enrollment(courseId, studentId);
            _enrollments.Add(enrollment);
            return enrollment;
        }

        public string GetCourseRosterReport(Guid courseId)
        {
            var course = GetCourseById(courseId);
            string instructorName = course.InstructorId.HasValue
                ? GetPersonById(course.InstructorId.Value).FullName
                : "Не призначено";

            var enrolledStudents = _enrollments
                .Where(e => e.CourseId == courseId)
                .Select(e => GetPersonById(e.StudentId).FullName)
                .ToList();

            string studentsList = enrolledStudents.Count > 0
                ? string.Join(", ", enrolledStudents)
                : "поки немає";

            return $"{course.GetCourseSummary()}\n  Викладач: {instructorName}\n  Записані студенти ({enrolledStudents.Count}/{course.MaxStudents}): {studentsList}";
        }
    }

    // ==================== РІВЕНЬ ПРЕДСТАВЛЕННЯ (UI / Main) ====================

    internal class Program
    {
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var service = new EducationService();

            // 1. Створення сутностей через сервіс
            var instructor = service.RegisterInstructor("Іван Петренко", 38, "Архітектура ПЗ та .NET", 850m);
            var student1 = service.RegisterStudent("Олександр Ковальчук", 19, "ІПЗ-21", 92.5);
            var student2 = service.RegisterStudent("Марія Ткаченко", 20, "КН-22", 88.0);
            var student3 = service.RegisterStudent("Дмитро Бондаренко", 19, "ІПЗ-21", 81.0);

            var csharpCourse = service.CreateCourse("Об'єктно-орієнтоване програмування в C#", 60, maxStudents: 2);
            var tempCourse = service.CreateCourse("Тимчасовий курс", 15, maxStudents: 10);

            Console.WriteLine("=== 1. Зареєстровані учасники (поліморфний вивід) ===");
            foreach (var p in service.GetAllParticipants())
                Console.WriteLine(p.GetInfo());

            // 2. Демонстрація перевірки бізнес-обмежень
            Console.WriteLine("\n=== 2. Перевірка бізнес-обмежень ===");
            ExecuteSafely(() => service.EnrollStudentToCourse(csharpCourse.Id, student1.Id)); // Помилка: немає викладача

            // 3. Повний сценарій взаємодії: Призначення викладача -> Запис студентів
            Console.WriteLine("\n=== 3. Сценарій призначення викладача та запису студентів ===");
            service.AssignInstructorToCourse(csharpCourse.Id, instructor.Id);
            service.EnrollStudentToCourse(csharpCourse.Id, student1.Id);
            service.EnrollStudentToCourse(csharpCourse.Id, student2.Id);
            Console.WriteLine("Успішно призначено викладача та зараховано двох студентів.");

            // Спроба порушити ліміт місць (макс. 2) та дублювання запису
            ExecuteSafely(() => service.EnrollStudentToCourse(csharpCourse.Id, student1.Id)); // Дублікат
            ExecuteSafely(() => service.EnrollStudentToCourse(csharpCourse.Id, student3.Id)); // Перевищення ліміту

            // 4. Оновлення та видалення сутностей
            Console.WriteLine("\n=== 4. Пошук, оновлення та видалення ===");
            var found = service.FindCourseByTitle("C#");
            if (found != null)
            {
                service.UpdateCourseDetails(found.Id, "Професійне ООП та архітектура в C#", 75);
                Console.WriteLine("Оновлено дані курсу.");
            }

            ExecuteSafely(() => service.DeleteCourse(csharpCourse.Id)); // Заборонено: є студенти
            service.DeleteCourse(tempCourse.Id);                         // Дозволено: курс порожній
            Console.WriteLine("Порожній курс успішно видалено.");

            // 5. Підсумковий звіт щодо взаємодії сутностей
            Console.WriteLine("\n=== 5. Підсумковий стан курсу ===");
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
                Console.WriteLine($"[Відхилено бізнес-правилом]: {ex.Message}");
            }
        }
    }
}
