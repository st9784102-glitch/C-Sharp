using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DomainModelDemo
{
    // ==================== СУТНОСТІ ПРЕДМЕТНОЇ ОБЛАСТІ ====================

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
        public string Category { get; set; }
        public int Hours { get; set; }
        public int MaxStudents { get; set; }
        public int? InstructorId { get; set; }

        public Course(string title, string category, int hours, int maxStudents, int? instructorId = null)
        {
            Title = title;
            Category = category;
            Hours = hours;
            MaxStudents = maxStudents;
            InstructorId = instructorId;
        }
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
    }

    public class Instructor : Person
    {
        public string Specialization { get; set; }
        public decimal HourlyRate { get; set; }

        public Instructor(string fullName, int age, string specialization, decimal hourlyRate)
            : base(fullName, age)
        {
            Specialization = specialization;
            HourlyRate = hourlyRate;
        }
    }

    public class Enrollment : BaseEntity
    {
        public int CourseId { get; }
        public int StudentId { get; }
        public int? FinalScore { get; }

        public Enrollment(int courseId, int studentId, int? finalScore = null)
        {
            CourseId = courseId;
            StudentId = studentId;
            FinalScore = finalScore;
        }
    }

    // ==================== DTO-МОДЕЛІ ДЛЯ АНАЛІТИЧНИХ ПРОЄКЦІЙ ====================

    public record CourseAnalyticsDto(
        int CourseId,
        string Title,
        string Category,
        string InstructorName,
        int EnrolledCount,
        int MaxStudents,
        double OccupancyPercent);

    public record GroupSummaryDto(
        string GroupName,
        int StudentsCount,
        double AverageGrade,
        double MaxGrade,
        int HonorsCount);

    public record InstructorWorkloadDto(
        string InstructorName,
        string Specialization,
        int CoursesCount,
        int TotalHours,
        decimal EstimatedCourseBudget);

    public record PlatformKpiDto(
        int TotalCourses,
        int TotalHoursOffered,
        int TotalStudents,
        double OverallAverageGrade,
        double MinGrade,
        double MaxGrade,
        int TotalEnrollments);

    // ==================== ПІДСИСТЕМА АНАЛІТИЧНИХ ЗАПИТІВ (LINQ) ====================

    public class AnalyticsService
    {
        private readonly List<Course> _courses;
        private readonly List<Person> _participants;
        private readonly List<Enrollment> _enrollments;

        public AnalyticsService(
            List<Course> courses,
            List<Person> participants,
            List<Enrollment> enrollments)
        {
            _courses = courses;
            _participants = participants;
            _enrollments = enrollments;
        }

        // ЗАПИТ 1: Фільтрація (Where) + багаторівневе сортування (OrderBy + ThenByDescending)
        public List<Student> GetHonorsStudentsSorted(double minGrade = 85.0)
        {
            return _participants
                .OfType<Student>()
                .Where(s => s.AverageGrade >= minGrade)
                .OrderBy(s => s.GroupName)
                .ThenByDescending(s => s.AverageGrade)
                .ToList();
        }

        // ЗАПИТ 2: З'єднання колекцій (Join) + Проєкція (Select) у DTO звіту
        public List<CourseAnalyticsDto> GetCourseOccupancyOverview()
        {
            var instructors = _participants.OfType<Instructor>();

            return _courses
                .Join(
                    instructors,
                    course => course.InstructorId,
                    instructor => instructor.Id,
                    (course, instructor) => new { course, instructor })
                .Select(item =>
                {
                    int enrolled = _enrollments.Count(e => e.CourseId == item.course.Id);
                    double occupancy = Math.Round((double)enrolled / item.course.MaxStudents * 100.0, 1);

                    return new CourseAnalyticsDto(
                        item.course.Id,
                        item.course.Title,
                        item.course.Category,
                        item.instructor.FullName,
                        enrolled,
                        item.course.MaxStudents,
                        occupancy);
                })
                .OrderByDescending(dto => dto.OccupancyPercent)
                .ToList();
        }

        // ЗАПИТ 3: Агрегатні обчислення (Count, Sum, Average, Min, Max)
        public PlatformKpiDto GetPlatformMetricsSummary()
        {
            var students = _participants.OfType<Student>().ToList();

            return new PlatformKpiDto(
                TotalCourses: _courses.Count,
                TotalHoursOffered: _courses.Sum(c => c.Hours),
                TotalStudents: students.Count,
                OverallAverageGrade: students.Any() ? Math.Round(students.Average(s => s.AverageGrade), 2) : 0,
                MinGrade: students.Any() ? students.Min(s => s.AverageGrade) : 0,
                MaxGrade: students.Any() ? students.Max(s => s.AverageGrade) : 0,
                TotalEnrollments: _enrollments.Count);
        }

        // ЗАПИТ 4 (Обов'язковий): Групування (GroupBy) студентів за групами з підсумковою статистикою
        public List<GroupSummaryDto> GetGroupPerformanceStatistics()
        {
            return _participants
                .OfType<Student>()
                .GroupBy(s => s.GroupName)
                .Select(group => new GroupSummaryDto(
                    GroupName: group.Key,
                    StudentsCount: group.Count(),
                    AverageGrade: Math.Round(group.Average(s => s.AverageGrade), 2),
                    MaxGrade: group.Max(s => s.AverageGrade),
                    HonorsCount: group.Count(s => s.AverageGrade >= 90.0)))
                .OrderByDescending(stat => stat.AverageGrade)
                .ToList();
        }

        // ЗАПИТ 5: Групування та агрегування навантаження викладачів по курсах
        public List<InstructorWorkloadDto> GetInstructorWorkloadStatistics()
        {
            var instructors = _participants.OfType<Instructor>();

            return _courses
                .Where(c => c.InstructorId.HasValue)
                .Join(
                    instructors,
                    c => c.InstructorId!.Value,
                    i => i.Id,
                    (course, instructor) => new { course, instructor })
                .GroupBy(x => new { x.instructor.Id, x.instructor.FullName, x.instructor.Specialization, x.instructor.HourlyRate })
                .Select(g =>
                {
                    int totalHours = g.Sum(x => x.course.Hours);
                    return new InstructorWorkloadDto(
                        InstructorName: g.Key.FullName,
                        Specialization: g.Key.Specialization,
                        CoursesCount: g.Count(),
                        TotalHours: totalHours,
                        EstimatedCourseBudget: totalHours * g.Key.HourlyRate);
                })
                .OrderByDescending(w => w.TotalHours)
                .ToList();
        }

        // ЗАПИТ 6: Відкладений запит (Deferred Execution) — повертає IEnumerable<Course> без матеріалізації
        public IEnumerable<Course> BuildOpenCoursesDeferredQuery(string category)
        {
            return _courses
                .Where(c => c.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .Where(c => _enrollments.Count(e => e.CourseId == c.Id) < c.MaxStudents)
                .OrderBy(c => c.Title);
        }
    }

    // ==================== ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ ====================

    internal class Program
    {
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // 1. Підготовка доменних даних
            var inst1 = new Instructor("Іван Петренко", 38, ".NET та Архітектура", 850m);
            var inst2 = new Instructor("Олена Сидоренко", 34, "Бази даних та Аналітика", 780m);

            var participants = new List<Person>
            {
                inst1,
                inst2,
                new Student("Олександр Ковальчук", 19, "ІПЗ-21", 94.5),
                new Student("Марія Ткаченко", 20, "КН-22", 91.0),
                new Student("Дмитро Бондаренко", 19, "ІПЗ-21", 78.0),
                new Student("Анна Мельник", 20, "КН-22", 86.5),
                new Student("Максим Шевченко", 21, "ІПЗ-21", 89.0),
                new Student("Софія Бойко", 19, "ПМ-23", 96.0),
                new Student("Артем Лисенко", 20, "ПМ-23", 74.5)
            };

            var students = participants.OfType<Student>().ToList();

            var courses = new List<Course>
            {
                new("Об'єктно-орієнтоване програмування в C#", "Programming", 60, 4, inst1.Id),
                new("LINQ та обробка даних у .NET", "Programming", 45, 3, inst1.Id),
                new("Проєктування реляційних баз даних", "Databases", 50, 5, inst2.Id),
                new("Оптимізація SQL-запитів", "Databases", 30, 3, inst2.Id)
            };

            var enrollments = new List<Enrollment>
            {
                new(courses[0].Id, students[0].Id, 95),
                new(courses[0].Id, students[1].Id, 90),
                new(courses[0].Id, students[4].Id, 88),
                new(courses[1].Id, students[0].Id, 96),
                new(courses[1].Id, students[3].Id, 87),
                new(courses[1].Id, students[5].Id, 98), // Курс LINQ заповнений повністю (3/3)
                new(courses[2].Id, students[2].Id, 79),
                new(courses[2].Id, students[6].Id, 75),
                new(courses[3].Id, students[1].Id, 92)
            };

            var analytics = new AnalyticsService(courses, participants, enrollments);

            // 2. Виконання аналітичних запитів
            Console.WriteLine("=== 1. Загальні агреговані показники платформи (KPI) ===");
            var kpi = analytics.GetPlatformMetricsSummary();
            Console.WriteLine($"Курсів: {kpi.TotalCourses} (усього {kpi.TotalHoursOffered} год.) | " +
                              $"Студентів: {kpi.TotalStudents} | Записів: {kpi.TotalEnrollments}");
            Console.WriteLine($"Успішність: середній бал = {kpi.OverallAverageGrade}, " +
                              $"мін = {kpi.MinGrade}, макс = {kpi.MaxGrade}\n");

            Console.WriteLine("=== 2. Фільтрація та багаторівневе сортування студентів (бал >= 85) ===");
            foreach (var st in analytics.GetHonorsStudentsSorted(85.0))
                Console.WriteLine($"  [{st.GroupName}] {st.FullName} — {st.AverageGrade} балів");

            Console.WriteLine("\n=== 3. З'єднання (Join) та проєкція заповненості курсів ===");
            foreach (var c in analytics.GetCourseOccupancyOverview())
                Console.WriteLine($"  \"{c.Title}\" (Викладач: {c.InstructorName}) -> {c.EnrolledCount}/{c.MaxStudents} ({c.OccupancyPercent}%)");

            Console.WriteLine("\n=== 4. Групування (GroupBy) та статистика по академічних групах ===");
            foreach (var g in analytics.GetGroupPerformanceStatistics())
                Console.WriteLine($"  Група {g.GroupName}: студентів = {g.StudentsCount}, " +
                                  $"середній бал = {g.AverageGrade}, макс = {g.MaxGrade}, відмінників (>=90) = {g.HonorsCount}");

            Console.WriteLine("\n=== 5. Групування навантаження викладачів ===");
            foreach (var w in analytics.GetInstructorWorkloadStatistics())
                Console.WriteLine($"  {w.InstructorName} ({w.Specialization}): курсів = {w.CoursesCount}, " +
                                  $"годин = {w.TotalHours}, бюджет = {w.EstimatedCourseBudget} грн");

            // 3. Демонстрація різниці між Deferred Execution та матеріалізацією (ToList)
            Console.WriteLine("\n=== 6. Відкладене виконання (Deferred Execution) vs Матеріалізація (ToList) ===");

            // Формуємо відкладений запит (на цьому етапі запит ще НЕ виконується)
            IEnumerable<Course> deferredQuery = analytics.BuildOpenCoursesDeferredQuery("Programming");

            // Фіксуємо знімок стану в пам'яті через матеріалізацію ToList() ДО додавання нового курсу
            List<Course> materializedSnapshot = deferredQuery.ToList();

            // Додаємо новий курс у вихідну колекцію вже ПІСЛЯ створення deferredQuery та materializedSnapshot
            courses.Add(new Course("Асинхронне програмування в C#", "Programming", 40, 10, inst1.Id));

            Console.WriteLine($"Матеріалізований знімок (ToList до додавання курсу): знайдено {materializedSnapshot.Count} курс(ів):");
            foreach (var c in materializedSnapshot)
                Console.WriteLine($"  - {c.Title}");

            // При ітерації по deferredQuery запит виконується заново над актуальним станом колекції courses
            Console.WriteLine($"Відкладений запит (foreach після додавання курсу): знайдено {deferredQuery.Count()} курс(ів):");
            foreach (var c in deferredQuery)
                Console.WriteLine($"  - {c.Title}");
        }
    }
}