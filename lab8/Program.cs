using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DomainModelDemo
{
    // ==================== МОДЕЛІ ПРЕДМЕТНОЇ ОБЛАСТІ ====================

    public record StudentSubmission(int StudentId, string FullName, string CourseTitle, double AverageScore);

    public record CertificateResult(
        string CertificateNumber,
        int StudentId,
        string FullName,
        string GradeCategory,
        DateTime IssuedAt);

    // ==================== СЕРВІС ПАКЕТНОЇ ПАРАЛЕЛЬНОЇ ОБРОБКИ (TPL) ====================

    public class BatchGradingService
    {
        private readonly ConcurrentBag<CertificateResult> _issuedCertificates = new();
        private int _processedCount;

        public int ProcessedCount => Volatile.Read(ref _processedCount);
        public IReadOnlyCollection<CertificateResult> IssuedCertificates => _issuedCertificates.ToArray();

        // Пакетна паралельна обробка списку заявок через Task.Run + Task.WhenAll
        public async Task<List<CertificateResult>> ProcessCertificatesBatchAsync(
            IReadOnlyList<StudentSubmission> submissions,
            CancellationToken cancellationToken,
            Action<string>? progressCallback = null)
        {
            _issuedCertificates.Clear();
            Interlocked.Exchange(ref _processedCount, 0);

            // Формуємо набір паралельних задач Task<CertificateResult>
            List<Task<CertificateResult>> tasks = submissions
                .Select(submission => Task.Run(
                    () => ProcessSingleStudent(submission, cancellationToken, progressCallback),
                    cancellationToken))
                .ToList();

            // Асинхронно очікуємо завершення всіх запущених задач
            CertificateResult[] results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        private CertificateResult ProcessSingleStudent(
            StudentSubmission submission,
            CancellationToken token,
            Action<string>? progressCallback)
        {
            // Точка перевірки №1: чи не надійшов сигнал скасування ще до старту роботи
            token.ThrowIfCancellationRequested();

            progressCallback?.Invoke(
                $"[Старт | Потік #{Environment.CurrentManagedThreadId}] Перевірка балів: {submission.FullName}...");

            // Імітація багатоетапної ресурсомісткої обробки з періодичною перевіркою токена
            for (int step = 1; step <= 3; step++)
            {
                token.ThrowIfCancellationRequested();
                Thread.Sleep(60); // Імітація обчислень / формування документа
            }

            string category = submission.AverageScore switch
            {
                >= 90.0 => "Відмінно (A)",
                >= 75.0 => "Добре (B/C)",
                >= 60.0 => "Задовільно (D/E)",
                _ => "Не атестовано (F)"
            };

            var certificate = new CertificateResult(
                CertificateNumber: $"CERT-2026-{submission.StudentId:D4}",
                StudentId: submission.StudentId,
                FullName: submission.FullName,
                GradeCategory: category,
                IssuedAt: DateTime.Now);

            _issuedCertificates.Add(certificate);
            int currentTotal = Interlocked.Increment(ref _processedCount);

            progressCallback?.Invoke(
                $"[Готово #{currentTotal} | Потік #{Environment.CurrentManagedThreadId}] Видано {certificate.CertificateNumber} для {submission.FullName} ({category})");

            return certificate;
        }
    }

    // ==================== ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ ====================

    internal class Program
    {
        static async Task Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var batchService = new BatchGradingService();
            var consoleLock = new object();

            void SafePrint(string message)
            {
                lock (consoleLock)
                {
                    Console.WriteLine(message);
                }
            }

            // 1. Демонстрація повного паралельного виконання невеликого пакета (4 студенти)
            Console.WriteLine("=== 1. ПАРАЛЕЛЬНА ОБРОБКА ПАКЕТА БЕЗ СКАСУВАННЯ (4 СТУДЕНТИ) ===\n");
            var smallBatch = Enumerable.Range(1, 4)
                .Select(i => new StudentSubmission(i, $"Студент #{i}", "Програмування на C#", 72.0 + i * 6))
                .ToList();

            using (var normalCts = new CancellationTokenSource())
            {
                List<CertificateResult> completed = await batchService.ProcessCertificatesBatchAsync(
                    smallBatch,
                    normalCts.Token,
                    SafePrint);

                Console.WriteLine($"\n[Підсумок етапу 1]: Успішно сформовано {completed.Count} із {smallBatch.Count} сертифікатів.\n");
            }

            // 2. Демонстрація контрольованого скасування великого пакета через CancellationToken
            Console.WriteLine("=== 2. ПАКЕТНА ОБРОБКА З КОНТРОЛЬОВАНИМ СКАСУВАННЯМ (24 СТУДЕНТИ) ===\n");
            var largeBatch = Enumerable.Range(101, 24)
                .Select(i => new StudentSubmission(i, $"Слухач #{i}", "Архітектура та TPL у .NET", 65.0 + (i % 35)))
                .ToList();

            using var cts = new CancellationTokenSource();

            // Плануємо автоматичне скасування операції через 230 мс (або можна викликати cts.Cancel() вручну)
            cts.CancelAfter(230);

            try
            {
                await batchService.ProcessCertificatesBatchAsync(
                    largeBatch,
                    cts.Token,
                    SafePrint);

                Console.WriteLine("\nУсі задачі пакета встигли завершитися до скасування.");
            }
            catch (OperationCanceledException)
            {
                // Коректне перехоплення скасування без аварійного падіння програми
                Console.WriteLine("\n[Стан: Canceled] Отримано сигнал CancellationToken. Усі активні задачі коректно зупинено.");
            }
            finally
            {
                // Перевірка проміжних результатів, які встигли сформуватися до моменту скасування
                Console.WriteLine($"[Проміжний підсумок]: Встигло коректно завершитися {batchService.ProcessedCount} із {largeBatch.Count} задач.");
                Console.WriteLine($"Збережено валідних сертифікатів у пам'яті: {batchService.IssuedCertificates.Count}");
            }
        }
    }
}