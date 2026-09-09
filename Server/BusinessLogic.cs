using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FitnessServer
{
    public static class BusinessLogic
    {
        private static List<Client> Clients = new();
        private static List<Trainer> Trainers = new();
        private static List<Membership> Memberships = new();
        private static List<Training> Trainings = new();
        private static List<Payment> Payments = new();

        private static int privateClientId = 0;
        private static int privateTrainerId = 0;
        private static int privateMembershipId = 0;
        private static int privateTrainingId = 0;
        private static int privatePaymentId = 0;

        public static void Seed()
        {

            Trainers.Add(new Trainer { Id = ++privateTrainerId, Name = "Алексей", Specialization = "Йога" });
            Trainers.Add(new Trainer { Id = ++privateTrainerId, Name = "Мария", Specialization = "Фитнес" });

            Clients.Add(new Client { Id = ++privateClientId, Name = "Иван", Email = "ivan@test.ru", Phone = "+375292223344" });
            Clients.Add(new Client { Id = ++privateClientId, Name = "Петр", Email = "petr@test.ru", Phone = "+375292333445" });
        }

        public class Client
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
            public string Phone { get; set; }
        }

        public class Trainer
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Specialization { get; set; }
        }

        public class Membership
        {
            public int Id { get; set; }
            public int ClientId { get; set; }
            public string Type { get; set; }
            public DateTime StartDate { get; set; }
            public DateTime EndDate { get; set; }
        }

        public class Training
        {
            public int Id { get; set; }
            public int ClientId { get; set; }
            public int TrainerId { get; set; }
            public DateTime DateTime { get; set; }
            public int Duration { get; set; }
            public string Status { get; set; }
        }

        public class Payment
        {
            public int Id { get; set; }
            public int ClientId { get; set; }
            public decimal Amount { get; set; }
            public DateTime Date { get; set; }
            public string Type { get; set; }
        }

        public static Response GetClients()
        {
            return new Response
            {
                Status = "success",
                Message = "Список клиентов получен.",
                Data = Clients.Select(c => new { c.Id, c.Name, c.Email, c.Phone }).ToList()
            };
        }

        public static Response GetTrainers()
        {
            return new Response
            {
                Status = "success",
                Message = "Список тренеров получен.",
                Data = Trainers.Select(t => new { t.Id, t.Name, t.Specialization }).ToList()
            };
        }
        public static Response RegisterClient(JsonElement data)
        {
            var name = data.GetProperty("Name").GetString();
            var email = data.GetProperty("Email").GetString();
            var phone = data.GetProperty("Phone").GetString();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email))
                return new Response { Status = "error", Message = "Имя и Email обязательны." };

            var client = new Client
            {
                Id = ++privateClientId,
                Name = name,
                Email = email,
                Phone = phone
            };
            Clients.Add(client);
            return new Response
            {
                Status = "success",
                Message = "Клиент зарегистрирован.",
                Data = new { client.Id, client.Name, client.Email }
            };
        }

        public static Response BookTraining(JsonElement data)
        {
            
            //System.Threading.Thread.Sleep(30000);
            
            var clientId = data.GetProperty("ClientId").GetInt32();
            var trainerId = data.GetProperty("TrainerId").GetInt32();
            var dateTime = data.GetProperty("DateTime").GetDateTime();
            var duration = data.GetProperty("Duration").GetInt32();

            var client = Clients.FirstOrDefault(c => c.Id == clientId);
            if (client == null)
                return new Response { Status = "error", Message = "Клиент не найден." };

            var trainer = Trainers.FirstOrDefault(t => t.Id == trainerId);
            if (trainer == null) 
                return new Response { Status = "error", Message = "Тренер не найден." };

            if (duration <= 0) 
                return new Response { Status = "error", Message = "Длительность должна быть > 0." };

            var training = new Training
            {
                Id = ++privateTrainingId,
                ClientId = clientId,
                TrainerId = trainerId,
                DateTime = dateTime,
                Duration = duration,
                Status = "Booked"
            };
            Trainings.Add(training);

            Console.WriteLine($"[Событие] TrainingBooked: {training.Id}");
            return new Response
            {
                Status = "success",
                Message = "Тренировка забронирована.",
                Data = new { training.Id, clientId, trainerId, DateTime = dateTime }
            };
        }

        public static Response CancelTraining(JsonElement data)
        {
            var trainingId = data.GetProperty("TrainingId").GetInt32();
            var training = Trainings.FirstOrDefault(t => t.Id == trainingId);
            if (training == null)
                return new Response { Status = "error", Message = "Тренировка не найдена." };
            if (training.Status == "Cancelled")
                return new Response { Status = "error", Message = "Тренировка уже отменена." };

            training.Status = "Cancelled";
            Console.WriteLine($"[Событие] TrainingCancelled: {trainingId}");
            return new Response
            {
                Status = "success",
                Message = "Тренировка отменена.",
                Data = new { training.Id, Status = training.Status }
            };
        }

        public static Response BuyMembership(JsonElement data)
        {
            var clientId = data.GetProperty("ClientId").GetInt32();
            var type = data.GetProperty("Type").GetString();
            var durationMonths = data.GetProperty("DurationMonths").GetInt32();

            var client = Clients.FirstOrDefault(c => c.Id == clientId);
            if (client == null)
                return new Response { Status = "error", Message = "Клиент не найден." };

            if (type != "Monthly" && type != "Yearly")
                return new Response { Status = "error", Message = "Тип должен быть 'Monthly' или 'Yearly'." };
            if (durationMonths <= 0)
                return new Response { Status = "error", Message = "Количество месяцев должно быть > 0." };

            var membership = new Membership
            {
                Id = ++privateMembershipId,
                ClientId = clientId,
                Type = type,
                StartDate = DateTime.Now,
                EndDate = DateTime.Now.AddMonths(durationMonths)
            };
            Memberships.Add(membership);

            var amount = type == "Monthly" ? 1000m * durationMonths : 5000m * durationMonths;
            var payment = new Payment
            {
                Id = ++privatePaymentId,
                ClientId = clientId,
                Amount = amount,
                Date = DateTime.Now,
                Type = "Membership"
            };
            Payments.Add(payment);

            Console.WriteLine($"[Событие] MembershipPurchased: {membership.Id}");
            return new Response
            {
                Status = "success",
                Message = "Абонемент приобретён.",
                Data = new { membership.Id, Amount = payment.Amount, membership.EndDate }
            };
        }
        public static void Reset()
        {
            Clients.Clear();
            Trainers.Clear();
            Memberships.Clear();
            Trainings.Clear();
            Payments.Clear();
            privateClientId = 0;
            privateTrainerId = 0;
            privateMembershipId = 0;
            privateTrainingId = 0;
            privatePaymentId = 0;
        }
    }
}