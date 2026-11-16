using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using FitnessServer.PubSub;

namespace FitnessServer
{
    public static class BusinessLogic
    {
        private static readonly object _slotLock = new object();
        private static readonly SemaphoreSlim _slotSemaphore = new SemaphoreSlim(1, 1);

        public static string SyncMode { get; set; } = "none";
        public static string TransactionMode { get; set; } = "none";

        public static int BookingDelayMs { get; set; } = 0;
        public static int LongOperationDelayMs { get; set; } = 0;
        public static int TimeResponseDelayMs { get; set; } = 0;

        public static void Seed() => Database.Seed();

        public static void Reset()
        {
            Database.Reset();
            BookingDelayMs = 0;
            LongOperationDelayMs = 0;
            TimeResponseDelayMs = 0;
            SyncMode = "none";
            TransactionMode = "none";
        }

        public static Response GetClients()
        {
            using var conn = Database.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Email, Phone FROM Clients ORDER BY Id;";
            using var r = cmd.ExecuteReader();
            var list = new List<object>();
            while (r.Read())
                list.Add(new { Id = r.GetInt32(0), Name = r.GetString(1), Email = r.GetString(2), Phone = r.GetString(3) });
            return new Response { Status = "success", Message = "Список клиентов получен.", Data = list };
        }

        public static Response GetTrainers()
        {
            using var conn = Database.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Specialization FROM Trainers ORDER BY Id;";
            using var r = cmd.ExecuteReader();
            var list = new List<object>();
            while (r.Read())
                list.Add(new { Id = r.GetInt32(0), Name = r.GetString(1), Specialization = r.GetString(2) });
            return new Response { Status = "success", Message = "Список тренеров получен.", Data = list };
        }

        public static Response GetTrainingSlots()
        {
            using var conn = Database.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT s.Id, s.TrainerId, s.DateTime, s.AvailablePlaces,
                       (SELECT COUNT(*) FROM Trainings t WHERE t.TrainerId = s.TrainerId AND t.DateTime = s.DateTime) AS TrainingsCount
                FROM TrainingSlots s
                ORDER BY s.Id;";
            using var r = cmd.ExecuteReader();
            var list = new List<object>();
            while (r.Read())
                list.Add(new
                {
                    Id = r.GetInt32(0),
                    TrainerId = r.GetInt32(1),
                    DateTime = r.GetDateTime(2),
                    AvailablePlaces = r.GetInt32(3),
                    TrainingsCount = r.GetInt32(4)
                });
            return new Response { Status = "success", Message = "Слоты получены.", Data = list };
        }

        public static Response RegisterClient(JsonElement data)
        {
            var name = data.GetProperty("Name").GetString() ?? "";
            var email = data.GetProperty("Email").GetString() ?? "";
            var phone = data.GetProperty("Phone").GetString() ?? "";

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email))
                return new Response { Status = "error", Message = "Имя и Email обязательны." };

            using var conn = Database.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO Clients (Name, Email, Phone) VALUES (@n, @e, @p);
                                SELECT CAST(SCOPE_IDENTITY() AS INT);";
            cmd.Parameters.AddWithValue("@n", name);
            cmd.Parameters.AddWithValue("@e", email);
            cmd.Parameters.AddWithValue("@p", phone);
            var newId = Convert.ToInt32(cmd.ExecuteScalar());

            Database.ReplicateIfPrimary();

            return new Response
            {
                Status = "success",
                Message = "Клиент зарегистрирован.",
                Data = new { Id = newId, Name = name, Email = email }
            };
        }

        public static Response SetTransactionMode(JsonElement data)
        {
            var mode = data.GetProperty("Mode").GetString() ?? "none";
            if (mode != "none" && mode != "none-error" && mode != "transaction" && mode != "transaction-error")
                return new Response { Status = "error", Message = "Недопустимый режим транзакции." };
            TransactionMode = mode;
            Console.WriteLine($"[TX MODE] = {mode}");
            return new Response { Status = "success", Message = $"TX режим: {mode}" };
        }

        public static Response SetSyncMode(JsonElement data)
        {
            var mode = data.GetProperty("Mode").GetString() ?? "none";
            if (mode != "none" && mode != "lock" && mode != "semaphore")
                return new Response { Status = "error", Message = "Недопустимый режим синхронизации." };
            SyncMode = mode;
            Console.WriteLine($"[SYNC MODE] = {mode}");
            return new Response { Status = "success", Message = $"SYNC режим: {mode}" };
        }

        public static Response BookTraining(JsonElement data)
        {
            var clientId = data.GetProperty("ClientId").GetInt32();
            var trainerId = data.GetProperty("TrainerId").GetInt32();
            var dateTime = data.GetProperty("DateTime").GetDateTime();
            var duration = data.GetProperty("Duration").GetInt32();

            if (duration <= 0)
                return new Response { Status = "error", Message = "Длительность должна быть > 0." };

            using (var conn = Database.OpenConnection())
            {
                using var check = conn.CreateCommand();
                check.CommandText = @"SELECT
                    (SELECT COUNT(*) FROM Clients WHERE Id=@c) +
                    (SELECT COUNT(*) FROM Trainers WHERE Id=@t);";
                check.Parameters.AddWithValue("@c", clientId);
                check.Parameters.AddWithValue("@t", trainerId);
                if (Convert.ToInt32(check.ExecuteScalar()) < 2)
                    return new Response { Status = "error", Message = "Клиент или тренер не найден." };
            }

            int slotId = Database.GetSlotId(trainerId, dateTime);
            if (slotId < 0)
                return new Response { Status = "error", Message = "Слот не найден." };

            switch (SyncMode)
            {
                case "lock":
                    lock (_slotLock)
                        return BookInternal(slotId, clientId, trainerId, dateTime, duration);
                case "semaphore":
                    _slotSemaphore.Wait();
                    try { return BookInternal(slotId, clientId, trainerId, dateTime, duration); }
                    finally { _slotSemaphore.Release(); }
                default:
                    return BookInternal(slotId, clientId, trainerId, dateTime, duration);
            }
        }

        private static Response BookInternal(int slotId, int clientId, int trainerId,
            DateTime dateTime, int duration)
        {
            return TransactionMode switch
            {
                "transaction" => BookWithTransaction(slotId, clientId, trainerId, dateTime, duration, false),
                "transaction-error" => BookWithTransaction(slotId, clientId, trainerId, dateTime, duration, true),
                _ => BookWithoutTransaction(slotId, clientId, trainerId, dateTime, duration)
            };
        }

        private static Response BookWithoutTransaction(int slotId, int clientId,
            int trainerId, DateTime dateTime, int duration)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -- START BookTraining (NO TX)");

            using var conn = Database.OpenConnection();

            int current;
            using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT AvailablePlaces FROM TrainingSlots WHERE Id=@id;";
                check.Parameters.AddWithValue("@id", slotId);
                current = Convert.ToInt32(check.ExecuteScalar());
            }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -- CHECK AvailablePlaces={current}");

            if (current <= 0)
                return new Response { Status = "error", Message = "Нет свободных мест." };

            if (BookingDelayMs > 0)
                Thread.Sleep(BookingDelayMs);

            try
            {
                using (var upd = conn.CreateCommand())
                {
                    upd.CommandText = "UPDATE TrainingSlots SET AvailablePlaces = AvailablePlaces - 1 WHERE Id=@id;";
                    upd.Parameters.AddWithValue("@id", slotId);
                    upd.ExecuteNonQuery();
                }
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -- UPDATE AvailablePlaces={Database.GetAvailablePlaces(slotId)}");
            }
            catch (SqlException ex) when (ex.Message.Contains("CHECK constraint"))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -- UPDATE FAILED: CHECK constraint");
                return new Response
                {
                    Status = "error",
                    Message = "Конфликт данных: другой клиент уже занял место (CHECK constraint)."
                };
            }

            if (TransactionMode == "none-error")
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -- !!! SIMULATED ERROR !!!");
                return new Response { Status = "error", Message = "Ошибка после UPDATE, до INSERT." };
            }

            int newTrainingId;
            using (var ins = conn.CreateCommand())
            {
                ins.CommandText = @"INSERT INTO Trainings (ClientId, TrainerId, DateTime, Duration, Status)
                                    VALUES (@c, @t, @d, @dur, 'Booked');
                                    SELECT CAST(SCOPE_IDENTITY() AS INT);";
                ins.Parameters.AddWithValue("@c", clientId);
                ins.Parameters.AddWithValue("@t", trainerId);
                ins.Parameters.AddWithValue("@d", dateTime);
                ins.Parameters.AddWithValue("@dur", duration);
                newTrainingId = Convert.ToInt32(ins.ExecuteScalar());
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -- INSERT Training Id={newTrainingId}");
            }

            Database.ReplicateIfPrimary();

            PubSubManager.Publish(new Event
            {
                EventType = EventTypes.TrainingBooked,
                EntityId = newTrainingId,
                Data = new Dictionary<string, object>
                {
                    ["ClientId"] = clientId,
                    ["TrainerId"] = trainerId,
                    ["SlotId"] = slotId,
                    ["DateTime"] = dateTime,
                    ["Duration"] = duration
                }
            });

            return new Response
            {
                Status = "success",
                Message = "Тренировка забронирована.",
                Data = new { slotId, clientId, AvailablePlaces = Database.GetAvailablePlaces(slotId), TrainingId = newTrainingId }
            };
        }

        private static Response BookWithTransaction(int slotId, int clientId,
            int trainerId, DateTime dateTime, int duration, bool simulateError)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -- START BookTraining (TX, error={simulateError})");

            using var conn = Database.OpenConnection();
            using var tx = conn.BeginTransaction();

            try
            {
                int current;
                using (var check = conn.CreateCommand())
                {
                    check.Transaction = tx;
                    check.CommandText = "SELECT AvailablePlaces FROM TrainingSlots WHERE Id=@id;";
                    check.Parameters.AddWithValue("@id", slotId);
                    current = Convert.ToInt32(check.ExecuteScalar());
                }
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -- CHECK AvailablePlaces={current} (in TX)");

                if (current <= 0)
                {
                    tx.Rollback();
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -- ROLLBACK (no places)");
                    return new Response { Status = "error", Message = "Нет свободных мест." };
                }

                if (BookingDelayMs > 0)
                    Thread.Sleep(BookingDelayMs);

                using (var upd = conn.CreateCommand())
                {
                    upd.Transaction = tx;
                    upd.CommandText = "UPDATE TrainingSlots SET AvailablePlaces = AvailablePlaces - 1 WHERE Id=@id;";
                    upd.Parameters.AddWithValue("@id", slotId);
                    upd.ExecuteNonQuery();
                }
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -- UPDATE (in TX)");

                if (simulateError)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -- !!! SIMULATED ERROR !!!");
                    throw new InvalidOperationException("Искусственная ошибка внутри транзакции.");
                }

                int newTrainingId = 0;
                using (var ins = conn.CreateCommand())
                {
                    ins.Transaction = tx;
                    ins.CommandText = @"INSERT INTO Trainings (ClientId, TrainerId, DateTime, Duration, Status)
                                        VALUES (@c, @t, @d, @dur, 'Booked');
                                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
                    ins.Parameters.AddWithValue("@c", clientId);
                    ins.Parameters.AddWithValue("@t", trainerId);
                    ins.Parameters.AddWithValue("@d", dateTime);
                    ins.Parameters.AddWithValue("@dur", duration);
                    newTrainingId = Convert.ToInt32(ins.ExecuteScalar());
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -- INSERT Training Id={newTrainingId} (in TX)");
                }

                tx.Commit();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -- COMMIT");

                Database.ReplicateIfPrimary();

                PubSubManager.Publish(new Event
                {
                    EventType = EventTypes.TrainingBooked,
                    EntityId = newTrainingId,
                    Data = new Dictionary<string, object>
                    {
                        ["ClientId"] = clientId,
                        ["TrainerId"] = trainerId,
                        ["SlotId"] = slotId,
                        ["DateTime"] = dateTime,
                        ["Duration"] = duration
                    }
                });

                return new Response
                {
                    Status = "success",
                    Message = "Тренировка забронирована.",
                    Data = new { slotId, clientId, AvailablePlaces = Database.GetAvailablePlaces(slotId), TrainingId = newTrainingId }
                };
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -- ROLLBACK: {ex.Message}");

                if (ex is InstanceUnavailableException) throw;

                return new Response { Status = "error", Message = $"Транзакция отменена: {ex.Message}" };
            }
        }

        public static Response LongOperation(JsonElement data)
        {
            var start = DateTime.Now;
            Console.WriteLine($"[{start:HH:mm:ss.fff}] [LongOperation] START");
            if (LongOperationDelayMs > 0) Thread.Sleep(LongOperationDelayMs);
            var end = DateTime.Now;
            Console.WriteLine($"[{end:HH:mm:ss.fff}] [LongOperation] END Duration={(end - start).TotalSeconds:F2}s");
            return new Response { Status = "success", Message = "Длительная операция завершена.", Data = new { Start = start, End = end } };
        }

        public static Response CancelTraining(JsonElement data)
        {
            var id = data.GetProperty("TrainingId").GetInt32();
            using var conn = Database.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Trainings SET Status='Cancelled' WHERE Id=@id AND Status<>'Cancelled';";
            cmd.Parameters.AddWithValue("@id", id);
            int affected = cmd.ExecuteNonQuery();
            if (affected == 0)
                return new Response { Status = "error", Message = "Тренировка не найдена или уже отменена." };

            Database.ReplicateIfPrimary();

            PubSubManager.Publish(new Event
            {
                EventType = EventTypes.TrainingCancelled,
                EntityId = id,
                Data = new Dictionary<string, object>
                {
                    ["TrainingId"] = id
                }
            });

            return new Response { Status = "success", Message = "Тренировка отменена.", Data = new { Id = id } };
        }

        public static Response BuyMembership(JsonElement data)
        {
            var clientId = data.GetProperty("ClientId").GetInt32();
            var type = data.GetProperty("Type").GetString() ?? "";
            var months = data.GetProperty("DurationMonths").GetInt32();

            if (type != "Monthly" && type != "Yearly")
                return new Response { Status = "error", Message = "Тип должен быть 'Monthly' или 'Yearly'." };
            if (months <= 0)
                return new Response { Status = "error", Message = "Количество месяцев должно быть > 0." };

            using var conn = Database.OpenConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                decimal amount = type == "Monthly" ? 1000m * months : 5000m * months;
                var start = DateTime.Now;
                var end = start.AddMonths(months);

                using (var ins = conn.CreateCommand())
                {
                    ins.Transaction = tx;
                    ins.CommandText = @"INSERT INTO Memberships (ClientId, Type, StartDate, EndDate)
                                        VALUES (@c, @t, @s, @e);";
                    ins.Parameters.AddWithValue("@c", clientId);
                    ins.Parameters.AddWithValue("@t", type);
                    ins.Parameters.AddWithValue("@s", start);
                    ins.Parameters.AddWithValue("@e", end);
                    ins.ExecuteNonQuery();
                }

                using (var ins = conn.CreateCommand())
                {
                    ins.Transaction = tx;
                    ins.CommandText = @"INSERT INTO Payments (ClientId, Amount, Date, Type)
                                        VALUES (@c, @a, @d, 'Membership');";
                    ins.Parameters.AddWithValue("@c", clientId);
                    ins.Parameters.AddWithValue("@a", amount);
                    ins.Parameters.AddWithValue("@d", start);
                    ins.ExecuteNonQuery();
                }

                tx.Commit();

                Database.ReplicateIfPrimary();

                PubSubManager.Publish(new Event
                {
                    EventType = EventTypes.MembershipPurchased,
                    EntityId = clientId,
                    Data = new Dictionary<string, object>
                    {
                        ["ClientId"] = clientId,
                        ["Type"] = type,
                        ["Months"] = months,
                        ["Amount"] = amount
                    }
                });

                return new Response { Status = "success", Message = "Абонемент приобретён.", Data = new { Amount = amount, EndDate = end } };
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }
                if (ex is InstanceUnavailableException) throw;
                return new Response { Status = "error", Message = $"Ошибка: {ex.Message}" };
            }
        }

        public static Response ResetDb(JsonElement data)
        {
            Database.Reset();
            Database.Seed();
            return new Response { Status = "success", Message = "БД сброшена и реплицирована." };
        }

        public static Response TimeRequest(JsonElement data)
        {
            var serverTime = DateTime.UtcNow;
            if (TimeResponseDelayMs > 0) Thread.Sleep(TimeResponseDelayMs);

            var appMode = Environment.GetEnvironmentVariable("APP_MODE") ?? "default";
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [TimeRequest] ServerTimeUtc={serverTime:O}, APP_MODE={appMode}");

            return new Response 
            { 
                Status = "success", 
                Message = "Время сервера получено.", 
                Data = new { ServerTimeUtc = serverTime, AppMode = appMode } 
            };
        }

        public static Response SimulatePrimaryFailure(JsonElement data)
        {
            Database.SimulateFailure(InstanceRole.Primary);
            return new Response { Status = "success", Message = "Primary -> UNAVAILABLE" };
        }

        public static Response RecoverPrimary(JsonElement data)
        {
            Database.RecoverPrimary();
            return new Response { Status = "success", Message = "Primary восстановлен и синхронизирован." };
        }

        public static Response SyncInstances(JsonElement data)
        {
            Database.SyncPrimaryToReplica();
            return new Response { Status = "success", Message = "Синхронизация Primary -> Replica выполнена." };
        }

        public static Response GetInstanceState(JsonElement data)
        {
            return new Response
            {
                Status = "success",
                Message = "Состояние экземпляров",
                Data = new
                {
                    Primary = Database.IsAvailable(InstanceRole.Primary) ? "AVAILABLE" : "UNAVAILABLE",
                    Replica = Database.IsAvailable(InstanceRole.Replica) ? "AVAILABLE" : "UNAVAILABLE",
                    Active  = Database.ActiveRole.ToString()
                }
            };
        }
    }
}