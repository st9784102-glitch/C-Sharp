using System;
using System.Collections.Generic;

namespace DomainModelDemo
{
    // 1. Базовий клас сутності з унікальним ідентифікатором
    public abstract class BaseEntity
    {
        private readonly Guid _id;

        public Guid Id => _id;

        protected BaseEntity()
        {
            _id = Guid.NewGuid();
        }
    }

    // 2. Клас предметної області №1: Навчальний курс
    public class Course : BaseEntity
    {
        private string _title;
        private int _hours;

        public string Title
        {
            get => _title;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Назва курсу не може бути порожньою.");
                _title = value;
            }
        }

        public int Hours
        {
            get => _hours;
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(Hours), "Тривалість має бути більшою за 0 годин.");
                _hours = value;
            }
        }

        public Course(string title, int hours) : base()
        {
            Title = title;
            Hours = hours;
        }

        public string GetCourseSummary() => $"Курс: \"{Title}\" ({Hours} год.) [ID: {Id.ToString()[..8]}]";
    }

    // 3. Клас предметної області №2 (Базовий клас ієрархії): Учасник освітнього процесу
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
                _fullName = value;
            }
        }

        public int Age
        {
            get => _age;
            set
            {
                if (value < 16 || value > 100)
                    throw new ArgumentOutOfRangeException(nameof(Age), "Вік має бути в межах від 16 до 100 років.");
                _age = value;
            }
        }

        public Person(string fullName, int age) : base()
        {
            FullName = fullName;
            Age = age;
        }

        // Віртуальний метод для демонстрації динамічного поліморфізму
        public virtual string GetInfo()
        {
            return $"[ID: {Id.ToString()[..8]}] Учасник: {FullName}, вік: {Age}";
        }
    }

    // 4. Похідний клас №1: Студент
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

        // Перевизначення віртуального методу
        public override string GetInfo()
        {
            return $"{base.GetInfo()} | Роль: Студент (Група: {GroupName}, Середній бал: {AverageGrade})";
        }
    }

    // 5. Похідний клас №2: Викладач
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

        // Перевизначення віртуального методу
        public override string GetInfo()
        {
            return $"{base.GetInfo()} | Роль: Викладач (Спеціалізація: {Specialization}, Ставка: {HourlyRate} грн/год)";
        }
    }

    // Точка входу та демонстрація роботи
    internal class Program
    {
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // Створення об'єкта курсу
            Course oopCourse = new Course("Об'єктно-орієнтоване програмування в C#", 60);
            Console.WriteLine(oopCourse.GetCourseSummary());
            Console.WriteLine(new string('-', 75));

            // Колекція базового типу Person для демонстрації поліморфізму
            List<Person> participants = new List<Person>
            {
                new Student("Олександр Ковальчук", 19, "ІПЗ-21", 92.5),
                new Student("Марія Ткаченко", 20, "КН-22", 88.0),
                new Instructor("Іван Петренко", 38, "Архітектура ПЗ та .NET", 850m)
            };

            // Поліморфний виклик методу GetInfo()
            foreach (Person person in participants)
            {
                // Викликається перевизначена версія методу відповідно до реального типу об'єкта в пам'яті
                Console.WriteLine(person.GetInfo());
            }
        }
    }
}