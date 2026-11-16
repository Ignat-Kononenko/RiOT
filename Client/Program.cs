﻿using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace FitnessClient
{
    class Program
    {
        private static async Task<List<ClientInfo>> LoadClientsAsync(Client client)
        {
            try { return await client.GetClientsAsync(); }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки клиентов: {ex.Message}");
                return new List<ClientInfo>();
            }
        }

        private static async Task<List<TrainerInfo>> LoadTrainersAsync(Client client)
        {
            try { return await client.GetTrainersAsync(); }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки тренеров: {ex.Message}");
                return new List<TrainerInfo>();
            }
        }

        // ВАЖНО: вынесено на уровень класса, а не внутрь Main/while
        private static async Task SendClusterAsync(
            Client client, int clientId, string operation, object? data = null)
        {
            var request = new Request
            {
                Operation = operation,
                ClientId = clientId,
                Data = JsonSerializer.SerializeToElement(data ?? new { })
            };

            var response = await client.SendRequestAsync(request);
            Console.WriteLine($"Статус: {response.Status}");
            Console.WriteLine($"Сообщение: {response.Message}");

            if (response.Data != null)
            {
                var opts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                Console.WriteLine(JsonSerializer.Serialize(response.Data, opts));
            }
        }

        static async Task Main(string[] args)
        {
            int clientId = args.Length > 0 && int.TryParse(args[0], out var id) ? id : 1;
            double offsetSec = args.Length > 1 && double.TryParse(args[1],
                System.Globalization.CultureInfo.InvariantCulture, out var off) ? off : 0;

            TimeSpan clockOffset = TimeSpan.FromSeconds(offsetSec);

            var client = new Client();
            Console.WriteLine($"Клиент фитнес-центра #{clientId}");
            Console.WriteLine($"Смещение локальных часов: {offsetSec:+0.0;-0.0;0} с");

            var clients = await LoadClientsAsync(client);
            var trainers = await LoadTrainersAsync(client);
            Console.WriteLine($"Загружено клиентов: {clients.Count}, тренеров: {trainers.Count}\n");

            while (true)
            {
                Console.WriteLine("\nДоступные операции:");
                Console.WriteLine("1.  Зарегистрировать клиента");
                Console.WriteLine("2.  Забронировать тренировку");
                Console.WriteLine("3.  Отменить тренировку");
                Console.WriteLine("4.  Купить абонемент");
                Console.WriteLine("5.  Выход");
                Console.WriteLine("6.  Эксперимент: длительная операция");
                Console.WriteLine("7.  Эксперимент: Race Condition (бронирование)");
                Console.WriteLine("8.  Показать локальное время");
                Console.WriteLine("9.  Синхронизировать время с сервером");
                Console.WriteLine("10. Установить режим синхронизации (none/lock/semaphore)");
                Console.WriteLine("11. Установить режим транзакции");
                Console.WriteLine("12. Показать состояние БД (слоты)");
                Console.WriteLine("13. Без транзакции + ошибка (частичное изменение)");
                Console.WriteLine("14. С транзакцией + COMMIT");
                Console.WriteLine("15. С транзакцией + ROLLBACK");
                Console.WriteLine("16. Сбросить БД (Reset)");
                Console.WriteLine("17. Смоделировать отказ Primary");
                Console.WriteLine("18. Восстановить Primary");
                Console.WriteLine("19. Синхронизировать Primary и Replica");
                Console.WriteLine("20. Показать состояние экземпляров");
                Console.WriteLine("21. Добавить AnalyticsService (новый Subscriber)");
                Console.WriteLine("22. Отключить ScheduleService (сбой Subscriber)");
                Console.WriteLine("23. Восстановить ScheduleService");
                Console.WriteLine("24. Показать состояние Pub/Sub");
                Console.WriteLine("25. Показать состояние кластера (5 узлов)");
                Console.WriteLine("26. Провести выборы лидера");
                Console.WriteLine("27. Смоделировать отказ узла");
                Console.WriteLine("28. Восстановить узел");
                Console.WriteLine("29. Heartbeat-проверка лидера");
                Console.WriteLine("30. Предложить значение и провести голосование");
                Console.WriteLine("31. Установить голос узла (YES/NO)");
                Console.WriteLine("32. Сбросить кластер");
                Console.WriteLine("33. Эксперимент: отказ лидера и повторные выборы");
                Console.WriteLine("34. Эксперимент: все доступны -> консенсус");
                Console.WriteLine("35. Эксперимент: нет большинства -> нет консенсуса");
                Console.Write("Выберите операцию: ");

                var choice = Console.ReadLine();
                if (choice == "5") break;

                if (choice == "8")
                {
                    Console.WriteLine($"Локальное время: {DateTime.UtcNow + clockOffset:O}");
                    Console.WriteLine($"Реальное UTC:    {DateTime.UtcNow:O}");
                    continue;
                }

                if (choice == "9")
                {
                    var result = await client.SyncTimeAsync(clockOffset);
                    if (result == null) { Console.WriteLine("Не удалось синхронизировать."); continue; }
                    Console.WriteLine($"Локальное (до sync):  {result.RequestLocalTime:HH:mm:ss.fff}");
                    Console.WriteLine($"Ответ получен в:      {result.ResponseLocalTime:HH:mm:ss.fff}");
                    Console.WriteLine($"Время сервера (UTC):  {result.ServerTimeUtc:HH:mm:ss.fff}");
                    Console.WriteLine($"RTT:                  {result.Rtt.TotalMilliseconds:F1} ms");
                    Console.WriteLine($"Смещение:             {result.Offset.TotalSeconds:+0.000;-0.000;0.000} s");
                    Console.WriteLine($"Скорректированное:    {result.CorrectedTime:HH:mm:ss.fff}");
                    continue;
                }

                if (choice == "10")
                {
                    Console.Write("Режим (none/lock/semaphore): ");
                    var mode = Console.ReadLine() ?? "none";
                    var r = await client.SendRequestAsync(new Request
                    {
                        Operation = "SetSyncMode",
                        Data = JsonSerializer.SerializeToElement(new { Mode = mode }),
                        ClientId = clientId
                    });
                    Console.WriteLine($"Статус: {r.Status} | {r.Message}");
                    continue;
                }

                if (choice == "11")
                {
                    Console.Write("Режим (none/none-error/transaction/transaction-error): ");
                    var mode = Console.ReadLine() ?? "none";
                    var r = await client.SendRequestAsync(new Request
                    {
                        Operation = "SetTransactionMode",
                        Data = JsonSerializer.SerializeToElement(new { Mode = mode }),
                        ClientId = clientId
                    });
                    Console.WriteLine($"Статус: {r.Status} | {r.Message}");
                    continue;
                }

                if (choice == "12")
                {
                    var r = await client.SendRequestAsync(new Request
                    {
                        Operation = "GetTrainingSlots",
                        Data = JsonSerializer.SerializeToElement(new { }),
                        ClientId = clientId
                    });
                    Console.WriteLine($"Статус: {r.Status}");
                    if (r.Data != null)
                    {
                        var opts = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };
                        Console.WriteLine(JsonSerializer.Serialize(r.Data, opts));
                    }
                    continue;
                }

                if (choice == "13" || choice == "14" || choice == "15")
                {
                    string txMode = choice switch
                    {
                        "13" => "none-error",
                        "14" => "transaction",
                        _    => "transaction-error"
                    };

                    await client.SendRequestAsync(new Request
                    {
                        Operation = "SetTransactionMode",
                        Data = JsonSerializer.SerializeToElement(new { Mode = txMode }),
                        ClientId = clientId
                    });

                    var request = RequestBuilder.BuildRaceConditionRequest(clientId);
                    request.ClientId = clientId;
                    var response = await client.SendRequestAsync(request);

                    Console.WriteLine($"--- Эксперимент ЛР4: {txMode} ---");
                    Console.WriteLine($"Статус: {response.Status}");
                    Console.WriteLine($"Сообщение: {response.Message}");
                    if (response.Data != null)
                    {
                        var opts = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };
                        Console.WriteLine($"Данные: {JsonSerializer.Serialize(response.Data, opts)}");
                    }
                    continue;
                }

                if (choice == "16")
                {
                    var r = await client.SendRequestAsync(new Request
                    {
                        Operation = "ResetDb",
                        Data = JsonSerializer.SerializeToElement(new { }),
                        ClientId = clientId
                    });
                    Console.WriteLine($"Статус: {r.Status} | {r.Message}");
                    continue;
                }

                if (choice == "17")
                {
                    var r = await client.SendRequestAsync(new Request
                    {
                        Operation = "SimulatePrimaryFailure",
                        Data = JsonSerializer.SerializeToElement(new { }),
                        ClientId = clientId
                    });
                    Console.WriteLine($"Статус: {r.Status} | {r.Message}");
                    continue;
                }

                if (choice == "18")
                {
                    var r = await client.SendRequestAsync(new Request
                    {
                        Operation = "RecoverPrimary",
                        Data = JsonSerializer.SerializeToElement(new { }),
                        ClientId = clientId
                    });
                    Console.WriteLine($"Статус: {r.Status} | {r.Message}");
                    continue;
                }

                if (choice == "19")
                {
                    var r = await client.SendRequestAsync(new Request
                    {
                        Operation = "SyncInstances",
                        Data = JsonSerializer.SerializeToElement(new { }),
                        ClientId = clientId
                    });
                    Console.WriteLine($"Статус: {r.Status} | {r.Message}");
                    continue;
                }

                if (choice == "20")
                {
                    var r = await client.SendRequestAsync(new Request
                    {
                        Operation = "GetInstanceState",
                        Data = JsonSerializer.SerializeToElement(new { }),
                        ClientId = clientId
                    });
                    Console.WriteLine($"Статус: {r.Status}");
                    if (r.Data != null)
                    {
                        var opts = new JsonSerializerOptions { WriteIndented = true };
                        Console.WriteLine(JsonSerializer.Serialize(r.Data, opts));
                    }
                    continue;
                }

                if (choice == "21" || choice == "22" || choice == "23" || choice == "24")
                {
                    string op = choice switch
                    {
                        "21" => "AddAnalyticsSubscriber",
                        "22" => "SimulateSubscriberFailure",
                        "23" => "RecoverSubscriber",
                        _    => "GetPubSubStatus"
                    };

                    var r = await client.SendRequestAsync(new Request
                    {
                        Operation = op,
                        Data = JsonSerializer.SerializeToElement(new { }),
                        ClientId = clientId
                    });

                    Console.WriteLine($"Статус: {r.Status} | {r.Message}");
                    if (r.Data != null)
                    {
                        var opts = new JsonSerializerOptions { WriteIndented = true };
                        Console.WriteLine(JsonSerializer.Serialize(r.Data, opts));
                    }
                    continue;
                }

                if (choice == "25")
                {
                    await SendClusterAsync(client, clientId, "GetClusterState");
                    continue;
                }

                if (choice == "26")
                {
                    await SendClusterAsync(client, clientId, "ElectLeader");
                    continue;
                }

                if (choice == "27")
                {
                    Console.Write("NodeId (1..5): ");
                    if (!int.TryParse(Console.ReadLine(), out var nodeId))
                    {
                        Console.WriteLine("Некорректный ID.");
                        continue;
                    }
                    await SendClusterAsync(client, clientId, "SimulateNodeFailure", new { NodeId = nodeId });
                    continue;
                }

                if (choice == "28")
                {
                    Console.Write("NodeId (1..5): ");
                    if (!int.TryParse(Console.ReadLine(), out var nodeId))
                    {
                        Console.WriteLine("Некорректный ID.");
                        continue;
                    }
                    await SendClusterAsync(client, clientId, "RecoverNode", new { NodeId = nodeId });
                    continue;
                }

                if (choice == "29")
                {
                    await SendClusterAsync(client, clientId, "HeartbeatCheck");
                    continue;
                }

                if (choice == "30")
                {
                    Console.Write("Key (Enter = TrainingStatus): ");
                    var key = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(key)) key = "TrainingStatus";

                    Console.Write("Value (Enter = Confirmed): ");
                    var value = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(value)) value = "Confirmed";

                    await SendClusterAsync(client, clientId, "ProposeConsensus",
                        new { Key = key, Value = value });
                    continue;
                }

                if (choice == "31")
                {
                    Console.Write("NodeId (1..5): ");
                    if (!int.TryParse(Console.ReadLine(), out var nodeId))
                    {
                        Console.WriteLine("Некорректный ID.");
                        continue;
                    }

                    Console.Write("Голос (yes/no): ");
                    var voteText = Console.ReadLine();
                    bool vote = string.Equals(voteText, "yes", StringComparison.OrdinalIgnoreCase);

                    await SendClusterAsync(client, clientId, "SetConsensusVote",
                        new { NodeId = nodeId, Vote = vote });
                    continue;
                }

                if (choice == "32")
                {
                    await SendClusterAsync(client, clientId, "ResetCluster");
                    continue;
                }

                if (choice == "33")
                {
                    await SendClusterAsync(client, clientId, "LeaderFailureExperiment");
                    continue;
                }

                if (choice == "34")
                {
                    await SendClusterAsync(client, clientId, "ConsensusAllAvailableExperiment");
                    continue;
                }

                if (choice == "35")
                {
                    await SendClusterAsync(client, clientId, "ConsensusNoMajorityExperiment");
                    continue;
                }

                Request? req = null;
                switch (choice)
                {
                    case "1": req = RequestBuilder.BuildRegisterRequest(); break;
                    case "2":
                        clients = await LoadClientsAsync(client);
                        trainers = await LoadTrainersAsync(client);
                        req = RequestBuilder.BuildBookTrainingRequest(clients, trainers);
                        break;
                    case "3": req = RequestBuilder.BuildCancelTrainingRequest(); break;
                    case "4":
                        clients = await LoadClientsAsync(client);
                        req = RequestBuilder.BuildBuyMembershipRequest(clients);
                        break;
                    case "6": req = RequestBuilder.BuildLongOperationRequest(); break;
                    case "7": req = RequestBuilder.BuildRaceConditionRequest(clientId); break;
                    default:
                        Console.WriteLine("Неверный выбор.");
                        continue;
                }

                if (req != null)
                {
                    req.ClientId = clientId;
                    var response = await client.SendRequestAsync(req);
                    Console.WriteLine($"Статус: {response.Status}");
                    Console.WriteLine($"Сообщение: {response.Message}");
                    if (response.Data != null)
                    {
                        var opts = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };
                        Console.WriteLine($"Данные: {JsonSerializer.Serialize(response.Data, opts)}");
                    }
                }
            }
        }
    }
}