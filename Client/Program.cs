using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace FitnessClient
{
    class Program
    {
        private static async Task<List<ClientInfo>> LoadClientsAsync(Client client)
        {
            try
            {
                return await client.GetClientsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки клиентов: {ex.Message}");
                return new List<ClientInfo>();
            }
        }

        private static async Task<List<TrainerInfo>> LoadTrainersAsync(Client client)
        {
            try
            {
                return await client.GetTrainersAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки тренеров: {ex.Message}");
                return new List<TrainerInfo>();
            }
        }

        static async Task Main(string[] args)
        {
            var client = new Client();
            Console.WriteLine("=== Клиент фитнес-центра (HTTP) ===");

            var clients = await LoadClientsAsync(client);
            var trainers = await LoadTrainersAsync(client);
            Console.WriteLine($"Загружено клиентов: {clients.Count}, тренеров: {trainers.Count}\n");

            while (true)
            {
                Console.WriteLine("\nДоступные операции:");
                Console.WriteLine("1. Зарегистрировать клиента");
                Console.WriteLine("2. Забронировать тренировку");
                Console.WriteLine("3. Отменить тренировку");
                Console.WriteLine("4. Купить абонемент");
                Console.WriteLine("5. Выход");
                Console.Write("Выберите операцию: ");
                var choice = Console.ReadLine();

                if (choice == "5") break;

                Request request = null;
                switch (choice)
                {
                    case "1":
                        request = RequestBuilder.BuildRegisterRequest();
                        break;
                    case "2":
                        clients = await LoadClientsAsync(client);
                        trainers = await LoadTrainersAsync(client);
                        request = RequestBuilder.BuildBookTrainingRequest(clients, trainers);
                        break;
                    case "3":
                        request = RequestBuilder.BuildCancelTrainingRequest();
                        break;
                    case "4":
                        clients = await LoadClientsAsync(client);
                        request = RequestBuilder.BuildBuyMembershipRequest(clients);
                        break;
                    default:
                        Console.WriteLine("Неверный выбор.");
                        continue;
                }

                if (request != null)
                {
                    var response = await client.SendRequestAsync(request);
                    Console.WriteLine($"Статус: {response.Status}");
                    Console.WriteLine($"Сообщение: {response.Message}");
                    if (response.Data != null)
                    {
                        var options = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };
                        Console.WriteLine($"Данные: {JsonSerializer.Serialize(response.Data, options)}");
                    }

                    if ((choice == "1" || choice == "4") && response.Status == "success")
                    {
                        clients = await LoadClientsAsync(client);
                        Console.WriteLine($"Список клиентов обновлён. Клиентов: {clients.Count}");
                    }
                }
            }
        }
    }
}