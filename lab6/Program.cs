using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace DomainModelDemo
{
    // ==================== МОДЕЛІ ПРЕДМЕТНОЇ ОБЛАСТІ ====================

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
        public int? Score { get; }

        public Enrollment(int courseId, int studentId, int? score = null)
        {
            CourseId = courseId;
            StudentId = studentId;
            Score = score;
        }
    }

    // ==================== МОДЕЛІ АНАЛІТИЧНОГО ЗВІТУ (DTO) ====================

    public record CategoryAnalyticsItem(
        string Category,
        int CoursesCount,
        int TotalHours,
        double AverageHours,
        int TotalEnrolledStudents,
        string TopCourseTitle);

    public record AcademicGroupAnalyticsItem(
        string GroupName,
        int StudentsCount,
        double AverageGrade,
        double MinGrade,
        double MaxGrade,
        int HonorsStudentsCount);

    public record CourseRankingItem(
        string CourseTitle,
        string Category,
        string InstructorName,
        int EnrolledCount,
        int MaxStudents,
        double OccupancyPercent,
        double? AverageCourseScore);

    public class ComprehensiveAnalyticalReport
    {
        public DateTime GeneratedAt { get; init; } = DateTime.Now;

        // Показник 1: Загальні агреговані метрики платформи
        public int TotalCourses { get; init; }
        public int TotalHours { get; init; }
        public int TotalStudents { get; init; }
        public int TotalInstructors { get; init; }
        public int TotalEnrollments { get; init; }
        public double PlatformAverageStudentGrade { get; init; }
        public double PlatformMinGrade { get; init; }
        public double PlatformMaxGrade { get; init; }
        public double AverageCourseOccupancyPercent { get; init; }

        // Показник 2: Групування курсів за напрямами підготовки
        public List<CategoryAnalyticsItem> CategoryBreakdown { get; init; } = new();

        // Показник 3: Групування студентів за академічними групами
        public List<AcademicGroupAnalyticsItem> GroupPerformanceBreakdown { get; init; } = new();

        // Показник 4: Рейтинг завантаженості та результативності курсів
        public List<CourseRankingItem> CourseRankings { get; init; } = new();
    }

    // ==================== ПОБУДОВНИК АНАЛІТИЧНОГО ЗВІТУ (LINQ) ====================

    public class ComprehensiveReportBuilder
    {
        private readonly IReadOnlyList<Course> _courses;
        private readonly IReadOnlyList<Person> _participants;
        private readonly IReadOnlyList<Enrollment> _enrollments;

        public ComprehensiveReportBuilder(
            IReadOnlyList<Course> courses,
            IReadOnlyList<Person> participants,
            IReadOnlyList<Enrollment> enrollments)
        {
            _courses = courses;
            _participants = participants;
            _enrollments = enrollments;
        }

        public ComprehensiveAnalyticalReport GenerateReport()
        {
            // Матеріалізуємо вибірку студентів для уникнення повторних переборів
            var students = _participants.OfType<Student>().ToList();
            var instructors = _participants.OfType<Instructor>().ToList();

            // 1. LINQ-запит: Групування курсів за категоріями (GroupBy + Count + Sum + Average)
            var categoryStats = _courses
                .GroupBy(c => c.Category)
                .Select(group =>
                {
                    var coursesWithCounts = group
                        .Select(c => new
                        {
                            Course = c,
                            Enrolled = _enrollments.Count(e => e.CourseId == c.Id)
                        })
                        .ToList();

                    string topCourse = coursesWithCounts
                        .OrderByDescending(x => x.Enrolled)
                        .Select(x => x.Course.Title)
                        .FirstOrDefault() ?? "—";

                    return new CategoryAnalyticsItem(
                        Category: group.Key,
                        CoursesCount: group.Count(),
                        TotalHours: group.Sum(c => c.Hours),
                        AverageHours: Math.Round(group.Average(c => c.Hours), 1),
                        TotalEnrolledStudents: coursesWithCounts.Sum(x => x.Enrolled),
                        TopCourseTitle: topCourse);
                })
                .OrderByDescending(item => item.TotalEnrolledStudents)
                .ToList();

            // 2. LINQ-запит: Групування студентів за академічними групами (GroupBy + Count + Average + Min + Max)
            var groupStats = students
                .GroupBy(s => s.GroupName)
                .Select(group => new AcademicGroupAnalyticsItem(
                    GroupName: group.Key,
                    StudentsCount: group.Count(),
                    AverageGrade: Math.Round(group.Average(s => s.AverageGrade), 2),
                    MinGrade: group.Min(s => s.AverageGrade),
                    MaxGrade: group.Max(s => s.AverageGrade),
                    HonorsStudentsCount: group.Count(s => s.AverageGrade >= 90.0)))
                .OrderByDescending(g => g.AverageGrade)
                .ToList();

            // 3. LINQ-запит: Рейтинг курсів із підрахунком заповненості та середнього балу за курс
            var courseRankings = _courses
                .Select(course =>
                {
                    string instructorName = course.InstructorId.HasValue
                        ? instructors.FirstOrDefault(i => i.Id == course.InstructorId.Value)?.FullName ?? "Невідомий"
                        : "Не призначено";

                    var courseEnrollments = _enrollments.Where(e => e.CourseId == course.Id).ToList();
                    int enrolledCount = courseEnrollments.Count;
                    double occupancy = Math.Round((double)enrolledCount / course.MaxStudents * 100.0, 1);

                    var gradedEnrollments = courseEnrollments.Where(e => e.Score.HasValue).ToList();
                    double? avgScore = gradedEnrollments.Any()
                        ? Math.Round(gradedEnrollments.Average(e => e.Score!.Value), 1)
                        : null;

                    return new CourseRankingItem(
                        CourseTitle: course.Title,
                        Category: course.Category,
                        InstructorName: instructorName,
                        EnrolledCount: enrolledCount,
                        MaxStudents: course.MaxStudents,
                        OccupancyPercent: occupancy,
                        AverageCourseScore: avgScore);
                })
                .OrderByDescending(r => r.OccupancyPercent)
                .ThenBy(r => r.CourseTitle)
                .ToList();

            // 4. Агреговані зведені показники всієї платформи
            double avgOccupancy = courseRankings.Any()
                ? Math.Round(courseRankings.Average(c => c.OccupancyPercent), 1)
                : 0.0;

            return new ComprehensiveAnalyticalReport
            {
                GeneratedAt = DateTime.Now,
                TotalCourses = _courses.Count,
                TotalHours = _courses.Sum(c => c.Hours),
                TotalStudents = students.Count,
                TotalInstructors = instructors.Count,
                TotalEnrollments = _enrollments.Count,
                PlatformAverageStudentGrade = students.Any() ? Math.Round(students.Average(s => s.AverageGrade), 2) : 0,
                PlatformMinGrade = students.Any() ? students.Min(s => s.AverageGrade) : 0,
                PlatformMaxGrade = students.Any() ? students.Max(s => s.AverageGrade) : 0,
                AverageCourseOccupancyPercent = avgOccupancy,
                CategoryBreakdown = categoryStats,
                GroupPerformanceBreakdown = groupStats,
                CourseRankings = courseRankings
            };
        }

        // Форматування звіту у читабельний текстовий вигляд
        public string FormatReportAsText(ComprehensiveAnalyticalReport report)
        {
            var sb = new StringBuilder();
            string separator = new('=', 82);
            string subSeparator = new('-', 82);

            sb.AppendLine(separator);
            sb.AppendLine($"ПІДСУМКОВИЙ АНАЛІТИЧНИЙ ЗВІТ НАВЧАЛЬНОЇ ПЛАТФОРМИ");
            sb.AppendLine($"Дата та час формування: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(separator);

            sb.AppendLine("\n1. ЗАГАЛЬНІ КЛЮЧОВІ ПОКАЗНИКИ (KPI)");
            sb.AppendLine(subSeparator);
            sb.AppendLine($"  * Кількість курсів / навчальних годин : {report.TotalCourses} курсів ({report.TotalHours} год.)");
            sb.AppendLine($"  * Учасники (студенти / викладачі)     : {report.TotalStudents} / {report.TotalInstructors}");
            sb.AppendLine($"  * Загальна кількість записів на курси : {report.TotalEnrollments}");
            sb.AppendLine($"  * Середня заповненість груп           : {report.AverageCourseOccupancyPercent}%");
            sb.AppendLine($"  * Успішність студентів (Середній/Мін/Макс): {report.PlatformAverageStudentGrade} / {report.PlatformMinGrade} / {report.PlatformMaxGrade}");

            sb.AppendLine("\n2. АНАЛІТИКА ЗА НАПРЯМАМИ ПІДГОТОВКИ (GROUP BY CATEGORY)");
            sb.AppendLine(subSeparator);
            foreach (var cat in report.CategoryBreakdown)
            {
                sb.AppendLine($"  [{cat.Category}] Курсів: {cat.CoursesCount} | Усього годин: {cat.TotalHours} (сер. {cat.AverageHours} год.) | " +
                              $"Слухачів: {cat.TotalEnrolledStudents} | Лідер: \"{cat.TopCourseTitle}\"");
            }

            sb.AppendLine("\n3. УСПІШНІСТЬ АКАДЕМІЧНИХ ГРУП (GROUP BY GROUPNAME)");
            sb.AppendLine(subSeparator);
            foreach (var grp in report.GroupPerformanceBreakdown)
            {
                sb.AppendLine($"  Група {grp.GroupName,-8} | Студентів: {grp.StudentsCount} | Сер. бал: {grp.AverageGrade,-6:F2} | " +
                              $"Мін/Макс: {grp.MinGrade:F1} / {grp.MaxGrade:F1} | Відмінників (>=90): {grp.HonorsStudentsCount}");
            }

            sb.AppendLine("\n4. РЕЙТИНГ ЗАВАНТАЖЕНОСТІ ТА РЕЗУЛЬТАТИВНОСТІ КУРСІВ");
            sb.AppendLine(subSeparator);
            foreach (var c in report.CourseRankings)
            {
                string scoreText = c.AverageCourseScore.HasValue ? $"{c.AverageCourseScore.Value:F1}" : "немає оцінок";
                sb.AppendLine($"  * \"{c.CourseTitle}\" ({c.Category}) — Викладач: {c.InstructorName}");
                sb.AppendLine($"    Місць: {c.EnrolledCount}/{c.MaxStudents} ({c.OccupancyPercent}%) | Середній бал за курс: {scoreText}");
            }

            sb.AppendLine($"\n{separator}");
            return sb.ToString();
        }

        // Збереження звіту у текстовий файл із обробкою I/O
        public string SaveReportToFile(ComprehensiveAnalyticalReport report, string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string content = FormatReportAsText(report);
            File.WriteAllText(fullPath, content, Encoding.UTF8);
            return fullPath;
        }
    }

    // ==================== ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ ====================

    internal class Program
    {
        static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            // 1. Ініціалізація даних предметної області
            var inst1 = new Instructor("Іван Петренко", 38, "Software Engineering", 850m);
            var inst2 = new Instructor("Олена Сидоренко", 34, "Data Engineering", 800m);
            var inst3 = new Instructor("Тарас Мельник", 42, "Cybersecurity", 900m);

            var participants = new List<Person>
            {
                inst1, inst2, inst3,
                new Student("Олександр Ковальчук", 19, "ІПЗ-21", 94.5),
                new Student("Марія Ткаченко", 20, "КН-22", 91.0),
                new Student("Дмитро Бондаренко", 19, "ІПЗ-21", 78.0),
                new Student("Анна Савченко", 20, "КН-22", 86.5),
                new Student("Максим Шевченко", 21, "ІПЗ-21", 90.5),
                new Student("Софія Бойко", 19, "ПМ-23", 96.0),
                new Student("Артем Лисенко", 20, "ПМ-23", 74.5),
                new Student("Вікторія Романенко", 19, "ПМ-23", 88.0)
            };

            var students = participants.OfType<Student>().ToList();

            var courses = new List<Course>
            {
                new("Об'єктно-орієнтоване програмування в C#", "Programming", 60, 5, inst1.Id),
                new("LINQ та функціональні прийоми у .NET", "Programming", 45, 4, inst1.Id),
                new("Проєктування баз даних та SQL", "Databases", 50, 5, inst2.Id),
                new("Аналітика великих даних", "Databases", 40, 4, inst2.Id),
                new("Безпека вебзастосунків", "Security", 35, 3, inst3.Id)
            };

            var enrollments = new List<Enrollment>
            {
                new(courses[0].Id, students[0].Id, 95),
                new(courses[0].Id, students[1].Id, 91),
                new(courses[0].Id, students[2].Id, 80),
                new(courses[0].Id, students[4].Id, 92),

                new(courses[1].Id, students[0].Id, 97),
                new(courses[1].Id, students[3].Id, 88),
                new(courses[1].Id, students[5].Id, 96),

                new(courses[2].Id, students[1].Id, 90),
                new(courses[2].Id, students[6].Id, 76),
                new(courses[2].Id, students[7].Id, 89),

                new(courses[3].Id, students[5].Id, 94),

                new(courses[4].Id, students[0].Id, 93),
                new(courses[4].Id, students[4].Id, 89),
                new(courses[4].Id, students[7].Id, 91)
            };

            // 2. Побудова комплексного аналітичного звіту
            var reportBuilder = new ComprehensiveReportBuilder(courses, participants, enrollments);
            ComprehensiveAnalyticalReport report = reportBuilder.GenerateReport();

            // 3. Виведення звіту в консоль
            string formattedText = reportBuilder.FormatReportAsText(report);
            Console.WriteLine(formattedText);

            // 4. Збереження звіту у файл
            try
            {
                string savedPath = reportBuilder.SaveReportToFile(report, Path.Combine("Reports", "analytical_report.txt"));
                Console.WriteLine($"Звіт успішно збережено у файл: {savedPath}");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Помилка збереження файлу звіту: {ex.Message}");
            }
        }
    }
}