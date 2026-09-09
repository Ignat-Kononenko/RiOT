using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

namespace FitnessClient
{
    public static class RequestBuilder
    {
        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrEmpty(email)) return false;
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsValidPhone(string phone)
        {
            if (string.IsNullOrEmpty(phone)) return false;
            var cleaned = Regex.Replace(phone, @"[^\d+]", "");
            return cleaned.Length >= 5 && cleaned.Length <= 15;
        }

        // Обобщённый метод для выбора ID из списка с отображением имени
        private static int ReadIdWithList<T>(string prompt, List<T> items)
        {
            Console.WriteLine(prompt);
            if (items != null && items.Count > 0)
            {
                Console.WriteLine("Доступные варианты:");
                foreach (var item in items)
                {
                    var id = item.GetType().GetProperty("Id")?.GetValue(item, null);
                    var name = item.GetType().GetProperty("Name")?.GetValue(item, null);
                    Console.WriteLine($"  ID {id} - {name}");
                }
            }
            else
            {
                Console.WriteLine("(список пуст)");
            }

            while (true)
            {
                Console.Write("Введите ID: ");
                if (int.TryParse(Console.ReadLine(), out int id) && id > 0)
                    return id;
                Console.WriteLine("Введите положительное целое число.");
            }
        }

        public static Request BuildRegisterRequest()
        {
            Console.Write("Имя: ");
            var name = Console.ReadLine();

            string email;
            while (true)
            {
                Console.Write("Email: ");
                email = Console.ReadLine();
                if (IsValidEmail(email))
                    break;
                Console.WriteLine("Некорректный email. Попробуйте снова.");
            }

            string phone;
            while (true)
            {
                Console.Write("Телефон: ");
                phone = Console.ReadLine();
                if (IsValidPhone(phone))
                    break;
                Console.WriteLine("Некорректный телефон. Введите номер (например, +1234567890).");
            }

            var data = new { Name = name, Email = email, Phone = phone };
            return new Request
            {
                Operation = "RegisterClient",
                Data = JsonSerializer.SerializeToElement(data)
            };
        }

        public static Request BuildBookTrainingRequest(List<ClientInfo> clients, List<TrainerInfo> trainers)
        {
            int clientId = ReadIdWithList("Выберите клиента:", clients);
            int trainerId = ReadIdWithList("Выберите тренера:", trainers);

            DateTime dateTime;
            while (true)
            {
                Console.Write("Дата и время (форматы: yyyy-MM-dd HH:mm, yyyy-MM-ddTHH:mm или yyyy-MM-dd): ");
                var input = Console.ReadLine();
                if (TryParseDateTime(input, out dateTime))
                {
                    if (dateTime > DateTime.Now)
                        break;
                    Console.WriteLine("Дата должна быть в будущем. Попробуйте снова.");
                }
                else
                {
                    Console.WriteLine("Некорректный формат даты. Попробуйте снова.");
                }
            }

            int duration;
            while (true)
            {
                Console.Write("Длительность (мин): ");
                if (int.TryParse(Console.ReadLine(), out duration) && duration > 0)
                    break;
                Console.WriteLine("Введите положительное число.");
            }

            var data = new { ClientId = clientId, TrainerId = trainerId, DateTime = dateTime, Duration = duration };
            return new Request
            {
                Operation = "BookTraining",
                Data = JsonSerializer.SerializeToElement(data)
            };
        }

        public static Request BuildCancelTrainingRequest()
        {
            int trainingId;
            while (true)
            {
                Console.Write("ID тренировки (вы получили его при бронировании): ");
                if (int.TryParse(Console.ReadLine(), out trainingId) && trainingId > 0)
                    break;
                Console.WriteLine("Введите положительное целое число.");
            }

            var data = new { TrainingId = trainingId };
            return new Request
            {
                Operation = "CancelTraining",
                Data = JsonSerializer.SerializeToElement(data)
            };
        }

        public static Request BuildBuyMembershipRequest(List<ClientInfo> clients)
        {
            int clientId = ReadIdWithList("Выберите клиента для покупки абонемента:", clients);

            string type;
            while (true)
            {
                Console.Write("Тип (Monthly/Yearly): ");
                type = Console.ReadLine();
                if (type == "Monthly" || type == "Yearly")
                    break;
                Console.WriteLine("Введите 'Monthly' или 'Yearly'.");
            }

            int months;
            while (true)
            {
                Console.Write("Количество месяцев: ");
                if (int.TryParse(Console.ReadLine(), out months) && months > 0)
                    break;
                Console.WriteLine("Введите положительное целое число.");
            }

            var data = new { ClientId = clientId, Type = type, DurationMonths = months };
            return new Request
            {
                Operation = "BuyMembership",
                Data = JsonSerializer.SerializeToElement(data)
            };
        }

        private static bool TryParseDateTime(string input, out DateTime result)
        {
            result = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            string[] formats = { "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd", "dd.MM.yyyy HH:mm", "dd.MM.yyyy" };
            if (DateTime.TryParseExact(input, formats, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out result))
            {
                return true;
            }
            if (DateTime.TryParse(input, out result))
                return true;
            return false;
        }
    }
}