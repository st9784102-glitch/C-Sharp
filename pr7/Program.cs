using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
        private readonly object _gradeLock = new();
        private double _averageGrade;

        public int Id { get; }
        public string FullName { get; }

        public double AverageGrade
        {
            get { lock (_gradeLock) return _averageGrade; }
        }

        public Student(int id, string fullName, double averageGrade)
        {
            Id = id;
            FullName = fullName;
            _averageGrade = averageGrade;
        }

        // Потокобезпечне оновлення балу студента
        public void AddBonusPointsSafe(double delta)
        {
            lock (_gradeLock)
            {
                _averageGrade = Math.Min(100.0, _averageGrade + delta);
            }
        }
    }

    public class Enrollment
    {
        public int CourseId { get; }
        public int StudentId { get; }
        public DateTime CreatedAt { get; } = DateTime.UtcNow;

        public Enrollment(int courseId, int studentId)
        {
            CourseId = courseId;
            StudentId = studentId;
        }
    }

    // ==================== 1. НЕСИНХРОНІЗОВАНИЙ СЕРВІС (ДЕМОНСТРАЦІЯ RACE CONDITION) ====================

    public class UnsafePlatformService
    {
        private readonly Course _targetCourse;
        private readonly List<Enrollment> _enrollments = new();
        private int _acceptedCount;
        private int _rejectedCount;
        private int _collectionCorruptions;

        public UnsafePlatformService(Course targetCourse) => _targetCourse = targetCourse;

        public void EnrollStudentUnsafe(int studentId)
        {
            // RACE CONDITION: кілька Task одночасно читають _acceptedCount < MaxStudents
            if (_acceptedCount < _targetCourse.MaxStudents)
            {
                Thread.SpinWait(400); // Імітація обчислень, що провокує перемикання потоків

                _acceptedCount++; // Неатомарний інкремент (Read-Modify-Write)

                try
                {
                    _enrollments.Add(new Enrollment(_targetCourse.Id, studentId));
                }
                catch
                {
                    _collectionCorruptions++;
                }
            }
            else
            {
                _rejectedCount++;
            }
        }

        public void PrintSummary(int totalAttempts)
        {
            Console.WriteLine("[ДО СИНХРОНІЗАЦІЇ — UnsafePlatformService]");
            Console.WriteLine($"  Спроб запису            : {totalAttempts}");
            Console.WriteLine($"  Ліміт місць на курсі    : {_targetCourse.MaxStudents}");
            Console.WriteLine($"  Лічильник _acceptedCount: {_acceptedCount}");
            Console.WriteLine($"  Записів у List<T>       : {_enrollments.Count}");
            Console.WriteLine($"  Відхилено               : {_rejectedCount}");
            Console.WriteLine($"  Баланс (прийнято+відх.) : {_acceptedCount + _rejectedCount} (очікувалося {totalAttempts})");
            Console.WriteLine($"  Помилок List<T>.Add     : {_collectionCorruptions}\n");
        }
    }

    // ==================== 2. ПОТОКОБЕЗПЕЧНИЙ СЕРВІС (TPL + LOCK + CONCURRENTBAG) ====================

    public class SynchronizedPlatformService
    {
        private readonly object _coursesLock = new();
        private readonly object _enrollmentLock = new();

        private readonly List<Course> _courses = new();
        private readonly List<Enrollment> _enrollments = new();
        private readonly HashSet<(int CourseId, int StudentId)> _enrollmentIndex = new();
        private readonly ConcurrentBag<string> _concurrentAuditLog = new();

        private int _totalEnrollmentsCreated;
        private int _totalRejections;
        private int _nextCourseId = 100;

        public void RegisterInitialCourse(Course course)
        {
            lock (_coursesLock)
            {
                _courses.Add(course);
            }
        }

        // Паралельне створення нових курсів із атомарною генерацією ID
        public Course CreateCourseSafe(string title, int maxStudents)
        {
            int id = Interlocked.Increment(ref _nextCourseId);
            var course = new Course(id, title, maxStudents);

            lock (_coursesLock)
            {
                _courses.Add(course);
            }

            _concurrentAuditLog.Add($"[Thread #{Environment.CurrentManagedThreadId}] Створено курс #{id}: {title}");
            return course;
        }

        // Потокобезпечний запис студента на курс із контролем ліміту та дублікатів
        public bool TryEnrollStudentSafe(int courseId, int studentId)
        {
            Course? course;
            lock (_coursesLock)
            {
                course = _courses.FirstOrDefault(c => c.Id == courseId);
            }

            if (course is null)
                return false;

            bool enrolled = false;

            // Критична секція перевірки інваріантів курсу та зміни колекції записів
            lock (_enrollmentLock)
            {
                if (!_enrollmentIndex.Contains((courseId, studentId)))
                {
                    int currentEnrolled = _enrollments.Count(e => e.CourseId == courseId);
                    if (currentEnrolled < course.MaxStudents)
                    {
                        Thread.SpinWait(400);
                        _enrollments.Add(new Enrollment(courseId, studentId));
                        _enrollmentIndex.Add((courseId, studentId));
                        enrolled = true;
                    }
                }
            }

            if (enrolled)
            {
                Interlocked.Increment(ref _totalEnrollmentsCreated);
                _concurrentAuditLog.Add($"[Thread #{Environment.CurrentManagedThreadId}] Студент #{studentId} зарахований на курс #{courseId}");
                return true;
            }

            Interlocked.Increment(ref _totalRejections);
            return false;
        }

        // Пакетна асинхронна обробка задач із підтримкою скасування через CancellationToken
        public async Task<int> ProcessBatchAuditsWithCancellationAsync(
            IEnumerable<Student> students,
            CancellationToken cancellationToken)
        {
            int processedCount = 0;

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(students, parallelOptions, async (student, ct) =>
            {
                // Кооперативна перевірка запиту на скасування перед важкою операцією
                ct.ThrowIfCancellationRequested();

                // Імітація асинхронної перевірки профілю студента (I/O або зовнішній сервіс)
                await Task.Delay(25, ct);

                student.AddBonusPointsSafe(0.5);
                Interlocked.Increment(ref processedCount);
            });

            return processedCount;
        }

        public void PrintSummary(int targetCourseId, int totalAttempts)
        {
            int coursesCount;
            int courseEnrolledCount;
            int maxCapacity;

            lock (_coursesLock)
            {
                coursesCount = _courses.Count;
                maxCapacity = _courses.First(c => c.Id == targetCourseId).MaxStudents;
            }

            lock (_enrollmentLock)
            {
                courseEnrolledCount = _enrollments.Count(e => e.CourseId == targetCourseId);
            }

            Console.WriteLine("[ПІСЛЯ СИНХРОНІЗАЦІЇ — SynchronizedPlatformService]");
            Console.WriteLine($"  Спроб запису            : {totalAttempts}");
            Console.WriteLine($"  Ліміт місць на курсі    : {maxCapacity}");
            Console.WriteLine($"  Зараховано студентів    : {courseEnrolledCount}");
            Console.WriteLine($"  Відхилено заявок        : {_totalRejections}");
            Console.WriteLine($"  Баланс (прийнято+відх.) : {_totalEnrollmentsCreated + _totalRejections} (очікувалося {totalAttempts})");
            Console.WriteLine($"  Усього курсів у системі : {coursesCount}");
            Console.WriteLine($"  Записів у ConcurrentBag : {_concurrentAuditLog.Count}\n");
        }
    }

    // ==================== ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ ====================

    internal class Program
    {
        static async Task Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            const int courseCapacity = 40;
            const int totalStudents = 200;

            var mainCourse = new Course(1, "Паралельні обчислення та TPL у C#", courseCapacity);
            var students = Enumerable.Range(1, totalStudents)
                .Select(i => new Student(i, $"Студент #{i}", 80.0))
                .ToList();

            Console.WriteLine("=== 1. ДЕМОНСТРАЦІЯ RACE CONDITION (TPL БЕЗ СИНХРОНІЗАЦІЇ) ===\n");
            var unsafeService = new UnsafePlatformService(mainCourse);

            // Запускаємо 200 паралельних задач через Task.Run
            Task[] unsafeTasks = students
                .Select(st => Task.Run(() => unsafeService.EnrollStudentUnsafe(st.Id)))
                .ToArray();

            await Task.WhenAll(unsafeTasks);
            unsafeService.PrintSummary(totalStudents);

            Console.WriteLine("=== 2. КОНКУРЕНТНЕ ВИКОНАННЯ ІЗ СИНХРОНІЗАЦІЄЮ (TPL + LOCK + INTERLOCKED) ===\n");
            var safeService = new SynchronizedPlatformService();
            safeService.RegisterInitialCourse(mainCourse);

            // Одночасно запускаємо 3 типи конкурентних операцій над спільними даними:
            // А) 200 паралельних спроб запису на курс
            var enrollmentTasks = students
                .Select(st => Task.Run(() => safeService.TryEnrollStudentSafe(mainCourse.Id, st.Id)));

            // Б) 10 паралельних задач створення нових курсів
            var createCourseTasks = Enumerable.Range(1, 10)
                .Select(idx => Task.Run(() => safeService.CreateCourseSafe($"Спецкурс #{idx}", 30)));

            // В) 200 паралельних оновлень рейтингу студентів
            var updateGradeTasks = students
                .Select(st => Task.Run(() => st.AddBonusPointsSafe(1.5)));

            await Task.WhenAll(enrollmentTasks.Cast<Task>()
                .Concat(createCourseTasks)
                .Concat(updateGradeTasks));

            safeService.PrintSummary(mainCourse.Id, totalStudents);
            Console.WriteLine($"  Перевірка оновлення балу Студента #1: {students[0].AverageGrade} (очікувалося 81.5)\n");

            Console.WriteLine("=== 3. КОНТРОЛЬОВАНЕ СКАСУВАННЯ ЗАДАЧ ЧЕРЕЗ CANCELLATIONTOKEN ===\n");
            using var cts = new CancellationTokenSource();

            // Автоматично ініціюємо скасування через 120 мс під час обробки 200 студентів
            cts.CancelAfter(120);

            try
            {
                Console.WriteLine("Запуск пакетної перевірки студентів (ліміт часу 120 мс)...");
                int processed = await safeService.ProcessBatchAuditsWithCancellationAsync(students, cts.Token);
                Console.WriteLine($"Усі {processed} студентів оброблено повністю.");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[Скасовано]: Отримано сигнал CancellationToken. Виконання паралельних задач коректно зупинено без втрати цілісності даних.");
            }
        }
    }
}