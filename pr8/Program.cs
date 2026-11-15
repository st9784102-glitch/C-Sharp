using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DomainModelDemo
{
    // ==================== МОДЕЛІ ПРЕДМЕТНОЇ ОБЛАСТІ ====================

    public record Course(int Id, string Title, int Hours, int MaxStudents);

    public record Student(int Id, string FullName, string GroupName, double AverageGrade);

    public record VerificationResult(int StudentId, string FullName, bool IsVerified, string RegistryMessage);

    public class AsyncPlatformReport
    {
        public DateTime GeneratedAt { get; set; }
        public int TotalCourses { get; set; }
        public int TotalStudents { get; set; }
        public double AverageGrade { get; set; }
        public List<Course> Courses { get; set; } = new();
    }

    // ==================== АСИНХРОННИЙ СЕРВІС ПРЕДМЕТНОЇ ОБЛАСТІ ====================

    public class AsyncEducationWorkflowService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // 1. Асинхронна імітація запиту до зовнішнього сервісу перевірки студентів
        public async Task<VerificationResult> VerifyStudentAccreditationAsync(
            Student student,
            int simulatedLatencyMs,
            CancellationToken cancellationToken = default)
        {
            if (student is null)
                throw new ArgumentNullException(nameof(student));

            // Перевіряємо токен перед початком мережевого запиту
            cancellationToken.ThrowIfCancellationRequested();

            // Неблокуюче асинхронне очікування відповіді зовнішнього API
            await Task.Delay(simulatedLatencyMs, cancellationToken);

            bool isValid = student.AverageGrade >= 60.0;
            string statusMessage = isValid
                ? "Дані підтверджено в ЄДЕБО"
                : "Академічна заборгованість (середній бал < 60)";

            return new VerificationResult(student.Id, student.FullName, isValid, statusMessage);
        }

        // 2. Асинхронне формування та збереження звіту у файл (File.WriteAllTextAsync)
        public async Task<string> GenerateAndExportReportAsync(
            IReadOnlyList<Course> courses,
            IReadOnlyList<Student> students,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Шлях до файлу не може бути порожнім.", nameof(filePath));

            cancellationToken.ThrowIfCancellationRequested();

            // Імітація підготовки великого аналітичного зрізу
            await Task.Delay(120, cancellationToken);

            var report = new AsyncPlatformReport
            {
                GeneratedAt = DateTime.Now,
                TotalCourses = courses.Count,
                TotalStudents = students.Count,
                AverageGrade = students.Any() ? Math.Round(students.Average(s => s.AverageGrade), 2) : 0,
                Courses = courses.ToList()
            };

            string fullPath = Path.GetFullPath(filePath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string jsonContent = JsonSerializer.Serialize(report, JsonOptions);

            // Асинхронний неблокуючий запис у файл із підтримкою скасування
            await File.WriteAllTextAsync(fullPath, jsonContent, Encoding.UTF8, cancellationToken);

            return fullPath;
        }

        // 3. Асинхронне зчитування та перевірка звіту з файлу (File.ReadAllTextAsync)
        public async Task<AsyncPlatformReport> LoadReportAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Файл звіту не знайдено: {filePath}", filePath);

            // Асинхронне читання вмісту файлу
            string json = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);

            var report = JsonSerializer.Deserialize<AsyncPlatformReport>(json, JsonOptions);
            if (report is null)
                throw new InvalidDataException("Файл звіту порожній або пошкоджений.");

            return report;
        }
    }

    // ==================== ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ ====================

    internal class Program
    {
        static async Task Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            var workflowService = new AsyncEducationWorkflowService();

            var courses = new List<Course>
            {
                new(1, "Асинхронне програмування в C# (async/await)", 60, 25),
                new(2, "Проєктування високонавантажених API", 45, 20)
            };

            var students = new List<Student>
            {
                new(101, "Олександр Ковальчук", "ІПЗ-21", 93.5),
                new(102, "Марія Ткаченко", "КН-22", 88.0),
                new(103, "Дмитро Бондаренко", "ІПЗ-21", 58.0)
            };

            // 1. Демонстрація неблокуючого конкурентного I/O (Task.WhenAll)
            Console.WriteLine("=== 1. НЕБЛОКУЮЧЕ ВИКОНАННЯ ЗОВНІШНІХ ЗАПИТІВ ТА ЕКСПОРТУ ЗВІТУ ===\n");
            var stopwatch = Stopwatch.StartNew();

            using (var normalCts = new CancellationTokenSource())
            {
                // Запускаємо одночасно 3 зовнішні запити (по 250 мс) + асинхронний запис звіту у файл
                var verificationTasks = students
                    .Select(s => workflowService.VerifyStudentAccreditationAsync(s, simulatedLatencyMs: 250, normalCts.Token))
                    .ToList();

                string reportPath = Path.Combine("Reports", "async_platform_report.json");
                Task<string> exportTask = workflowService.GenerateAndExportReportAsync(
                    courses,
                    students,
                    reportPath,
                    normalCts.Token);

                // Очікуємо завершення всіх I/O операцій асинхронно без блокування потоку
                await Task.WhenAll(verificationTasks.Cast<Task>().Append(exportTask));
                stopwatch.Stop();

                Console.WriteLine($"Усі 4 асинхронні операції виконано за {stopwatch.ElapsedMilliseconds} мс (замість ~870 мс послідовно):");
                foreach (var verifyTask in verificationTasks)
                {
                    VerificationResult res = await verifyTask;
                    Console.WriteLine($"  - [{res.StudentId}] {res.FullName}: {(res.IsVerified ? "OK" : "УВАГА")} — {res.RegistryMessage}");
                }

                Console.WriteLine($"  - Звіт збережено у файл: {await exportTask}\n");

                // Асинхронне зчитування щойно збереженого звіту
                AsyncPlatformReport loadedReport = await workflowService.LoadReportAsync(reportPath, normalCts.Token);
                Console.WriteLine($"Перевірка зчитаного файлу: курсів = {loadedReport.TotalCourses}, " +
                                  $"студентів = {loadedReport.TotalStudents}, середній бал = {loadedReport.AverageGrade}\n");
            }

            // 2. Демонстрація обробки помилок в асинхронних методах
            Console.WriteLine("=== 2. ОБРОБКА ВИНЯТКІВ У ASYNC-МЕТОДАХ ===\n");
            try
            {
                await workflowService.LoadReportAsync(Path.Combine("Reports", "non_existent_report.json"));
            }
            catch (FileNotFoundException ex)
            {
                Console.WriteLine($"[Оброблено FileNotFoundException]: {ex.Message}\n");
            }

            // 3. Демонстрація скасування асинхронної операції через CancellationToken
            Console.WriteLine("=== 3. СКАСУВАННЯ АСИНХРОННОЇ ОПЕРАЦІЇ ЧЕРЕЗ CANCELLATIONTOKEN ===\n");
            using var timeoutCts = new CancellationTokenSource();

            // Встановлюємо таймаут очікування зовнішнього сервісу в 150 мс (тоді як запит триває 2000 мс)
            timeoutCts.CancelAfter(150);

            try
            {
                Console.WriteLine("Надсилання запиту до повільного зовнішнього реєстру (таймаут 150 мс)...");
                await workflowService.VerifyStudentAccreditationAsync(
                    students[0],
                    simulatedLatencyMs: 2000,
                    timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[Скасовано]: Операцію перервано через перевищення таймауту CancellationToken. Програма продовжує стабільну роботу.");
            }
        }
    }
}