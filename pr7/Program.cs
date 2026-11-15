using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace DomainModelDemo
{
    // ==================== МОДЕЛІ ПРЕДМЕТНОЇ ОБЛАСТІ ====================

    public class Course
    {
        public int Id { get; }
        public string Title { get; }
        public int MaxStudents { get; }

        public Course(int id, string title, int maxStudents)
        {
            Id = id;
            Title = title;
            MaxStudents = maxStudents;
        }
    }

    public class Student
    {
        public int Id { get; }
        public string FullName { get; }

        public Student(int id, string fullName)
        {
            Id = id;
            FullName = fullName;
        }
    }

    public class Enrollment
    {
        public int CourseId { get; }
        public int StudentId { get; }
        public int ManagedThreadId { get; }

        public Enrollment(int courseId, int studentId, int threadId)
        {
            CourseId = courseId;
            StudentId = studentId;
            ManagedThreadId = threadId;
        }
    }

    // ==================== 1. НЕБЕЗПЕЧНИЙ СЕРВІС (БЕЗ СИНХРОНІЗАЦІЇ) ====================

    public class UnsafeEnrollmentService
    {
        private readonly Course _course;
        private readonly List<Enrollment> _enrollments = new();
        private int _enrolledCounter;
        private int _rejectedCounter;
        private int _internalListErrors;

        public UnsafeEnrollmentService(Course course) => _course = course;

        public bool TryEnroll(Student student)
        {
            // ПОМИЛКА: перевірка і зміна спільного стану не захищені критичною секцією
            if (_enrolledCounter < _course.MaxStudents)
            {
                // Імітуємо невелику затримку бізнес-обробки, що провокує перемикання контексту
                Thread.SpinWait(500);

                // Неатомарний інкремент: можлива втрата оновлень (Lost Update)
                _enrolledCounter++;

                try
                {
                    // List<T> не є потокобезпечним при одночасному записі
                    _enrollments.Add(new Enrollment(_course.Id, student.Id, Environment.CurrentManagedThreadId));
                }
                catch
                {
                    _internalListErrors++;
                }

                return true;
            }

            _rejectedCounter++;
            return false;
        }

        public string GetDiagnosticsReport(int totalRequests)
        {
            var sb = new StringBuilder();
            sb.AppendLine("--- Результат БЕЗ синхронізації (UnsafeEnrollmentService) ---");
            sb.AppendLine($"  Усього запитів на запис         : {totalRequests}");
            sb.AppendLine($"  Дозволений ліміт курсу          : {_course.MaxStudents}");
            sb.AppendLine($"  Значення лічильника _enrolled   : {_enrolledCounter}");
            sb.AppendLine($"  Фактично записів у списку       : {_enrollments.Count}");
            sb.AppendLine($"  Відхилено заявок                : {_rejectedCounter}");
            sb.AppendLine($"  Сума (лічильник + відхилені)    : {_enrolledCounter + _rejectedCounter} (очікувалося {totalRequests})");
            sb.AppendLine($"  Внутрішніх збоїв List<T>.Add    : {_internalListErrors}");
            return sb.ToString();
        }
    }

    // ==================== 2. ПОТОКОБЕЗПЕЧНИЙ СЕРВІС (LOCK + INTERLOCKED) ====================

    public class ThreadSafeEnrollmentService
    {
        private readonly Course _course;
        private readonly List<Enrollment> _enrollments = new();
        private readonly object _syncLock = new(); // Виділений приватний об'єкт блокування
        private int _enrolledCounter;
        private int _rejectedCounter;

        public ThreadSafeEnrollmentService(Course course) => _course = course;

        public bool TryEnroll(Student student)
        {
            // Критична секція: лише один потік одночасно перевіряє ліміт і додає запис
            lock (_syncLock)
            {
                if (_enrolledCounter < _course.MaxStudents)
                {
                    Thread.SpinWait(500);
                    _enrolledCounter++;
                    _enrollments.Add(new Enrollment(_course.Id, student.Id, Environment.CurrentManagedThreadId));
                    return true;
                }
            }

            // Атомарний підрахунок відхилених заявок поза блокуванням
            Interlocked.Increment(ref _rejectedCounter);
            return false;
        }

        public string GetDiagnosticsReport(int totalRequests)
        {
            lock (_syncLock)
            {
                var sb = new StringBuilder();
                sb.AppendLine("--- Результат ІЗ синхронізацією (ThreadSafeEnrollmentService) ---");
                sb.AppendLine($"  Усього запитів на запис         : {totalRequests}");
                sb.AppendLine($"  Дозволений ліміт курсу          : {_course.MaxStudents}");
                sb.AppendLine($"  Значення лічильника _enrolled   : {_enrolledCounter}");
                sb.AppendLine($"  Фактично записів у списку       : {_enrollments.Count}");
                sb.AppendLine($"  Відхилено заявок                : {_rejectedCounter}");
                sb.AppendLine($"  Сума (лічильник + відхилені)    : {_enrolledCounter + _rejectedCounter} (очікувалося {totalRequests})");
                return sb.ToString();
            }
        }
    }

    // ==================== ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ ====================

    internal class Program
    {
        static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            const int courseCapacity = 50;
            const int totalApplicants = 200;
            const int threadsCount = 8;

            var course = new Course(1, "Багатопотокове програмування в C#", courseCapacity);
            var students = new List<Student>(totalApplicants);
            for (int i = 1; i <= totalApplicants; i++)
                students.Add(new Student(i, $"Студент #{i}"));

            var reportBuilder = new StringBuilder();
            reportBuilder.AppendLine("=== ЗВІТ ДЕМОНСТРАЦІЇ КОНКУРЕНТНОГО ДОСТУПУ ДО СПІЛЬНИХ ДАНИХ ===");
            reportBuilder.AppendLine($"Параметри тесту: ліміт місць = {courseCapacity}, заявок = {totalApplicants}, потоків = {threadsCount}\n");

            // 1. Запуск небезпечного варіанту без синхронізації
            var unsafeService = new UnsafeEnrollmentService(course);
            RunConcurrentTest(students, threadsCount, st => unsafeService.TryEnroll(st));
            reportBuilder.AppendLine(unsafeService.GetDiagnosticsReport(totalApplicants));

            // 2. Запуск потокобезпечного варіанту з lock та Interlocked
            var safeService = new ThreadSafeEnrollmentService(course);
            RunConcurrentTest(students, threadsCount, st => safeService.TryEnroll(st));
            reportBuilder.AppendLine(safeService.GetDiagnosticsReport(totalApplicants));

            string finalReport = reportBuilder.ToString();
            Console.WriteLine(finalReport);

            // Фіксація результатів у файл звіту
            Directory.CreateDirectory("Reports");
            string reportPath = Path.Combine("Reports", "concurrency_report.txt");
            File.WriteAllText(reportPath, finalReport, Encoding.UTF8);
            Console.WriteLine($"Звіт збережено у файл: {Path.GetFullPath(reportPath)}");
        }

        private static void RunConcurrentTest(List<Student> students, int threadsCount, Action<Student> workItem)
        {
            int nextIndex = -1;
            using var startBarrier = new ManualResetEventSlim(false);
            var threads = new Thread[threadsCount];

            for (int t = 0; t < threadsCount; t++)
            {
                threads[t] = new Thread(() =>
                {
                    // Усі потоки стартують одночасно для максимальної конкуренції
                    startBarrier.Wait();

                    while (true)
                    {
                        int idx = Interlocked.Increment(ref nextIndex);
                        if (idx >= students.Count)
                            break;

                        workItem(students[idx]);
                    }
                });
                threads[t].Start();
            }

            startBarrier.Set(); // Одночасний старт усіх потоків

            foreach (var thread in threads)
                thread.Join(); // Очікування завершення всіх потоків
        }
    }
}