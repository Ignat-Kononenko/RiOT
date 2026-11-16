using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FitnessServer;

namespace FitnessServer.Tests
{
    public class FitnessServerTests : IDisposable
    {
        public FitnessServerTests()
        {
            ThreadPool.SetMinThreads(4, 4);
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            Database.Initialize();
            Database.Reset();
            Database.Seed();

            BusinessLogic.SyncMode = "none";
            BusinessLogic.TransactionMode = "none";
            BusinessLogic.BookingDelayMs = 0;
            BusinessLogic.LongOperationDelayMs = 0;
            BusinessLogic.TimeResponseDelayMs = 0;

            if (Database.ActiveRole != InstanceRole.Primary)
            {
                Database.SimulateRecovery(InstanceRole.Primary);
                Database.RecoverPrimary();
            }
        }

        public void Dispose() { }

        [Fact]
        public void Test1_OneClient_BookTraining_Success()
        {
            BusinessLogic.BookingDelayMs = 0;
            var data = new
            {
                ClientId = 1,
                TrainerId = 1,
                DateTime = new DateTime(2030, 1, 1, 18, 0, 0),
                Duration = 60
            };

            var response = BusinessLogic.BookTraining(JsonSerializer.SerializeToElement(data));
            Assert.Equal("success", response.Status);
        }

        [Fact]
        public void Test2_TwoClients_BothRequestsProcessed()
        {
            var r1 = BusinessLogic.GetClients();
            var r2 = BusinessLogic.GetTrainers();
            Assert.Equal("success", r1.Status);
            Assert.Equal("success", r2.Status);
        }

        [Fact]
        public async Task Test3_ParallelProcessing_Overlap()
        {
            BusinessLogic.LongOperationDelayMs = 500;
            var start = DateTime.Now;

            var t1 = Task.Run(() => BusinessLogic.LongOperation(JsonSerializer.SerializeToElement(new { })));
            var t2 = Task.Run(() => BusinessLogic.LongOperation(JsonSerializer.SerializeToElement(new { })));

            var results = await Task.WhenAll(t1, t2);
            var total = DateTime.Now - start;

            Assert.All(results, r => Assert.Equal("success", r.Status));
            Assert.True(total.TotalMilliseconds < 900,
                $"Общее время {total.TotalMilliseconds} мс, ожидалось < 900 мс (параллельно)");
        }

        [Fact]
        public async Task Test4_RaceCondition_TwoClientsBookLastPlace()
        {
            BusinessLogic.SyncMode = "none";
            BusinessLogic.TransactionMode = "none";
            BusinessLogic.BookingDelayMs = 500;

            var data1 = new { ClientId = 1, TrainerId = 1, DateTime = new DateTime(2030, 1, 1, 18, 0, 0), Duration = 60 };
            var data2 = new { ClientId = 2, TrainerId = 1, DateTime = new DateTime(2030, 1, 1, 18, 0, 0), Duration = 60 };

            var t1 = Task.Run(() => BusinessLogic.BookTraining(JsonSerializer.SerializeToElement(data1)));
            var t2 = Task.Run(() => BusinessLogic.BookTraining(JsonSerializer.SerializeToElement(data2)));
            var results = await Task.WhenAll(t1, t2);

            int successCount = results.Count(r => r.Status == "success");
            int errorCount = results.Count(r => r.Status == "error");

            Assert.Equal(1, successCount);
            Assert.Equal(1, errorCount);

            int places = Database.GetAvailablePlaces(1);
            Assert.True(places >= 0, $"AvailablePlaces = {places}, ожидалось >= 0");
        }

        [Fact]
        public async Task Test5_Lock_PreventsRaceCondition_OneSuccess_OneFail()
        {
            BusinessLogic.BookingDelayMs = 500;
            BusinessLogic.SyncMode = "lock";
            BusinessLogic.TransactionMode = "none";

            var data1 = new { ClientId = 1, TrainerId = 1, DateTime = new DateTime(2030, 1, 1, 18, 0, 0), Duration = 60 };
            var data2 = new { ClientId = 2, TrainerId = 1, DateTime = new DateTime(2030, 1, 1, 18, 0, 0), Duration = 60 };

            var t1 = Task.Run(() => BusinessLogic.BookTraining(JsonSerializer.SerializeToElement(data1)));
            var t2 = Task.Run(() => BusinessLogic.BookTraining(JsonSerializer.SerializeToElement(data2)));
            var results = await Task.WhenAll(t1, t2);

            Assert.Equal(1, results.Count(r => r.Status == "success"));
            Assert.Equal(1, results.Count(r => r.Status == "error"));
            Assert.Equal(0, Database.GetAvailablePlaces(1));
        }

        [Fact]
        public async Task Test6_Semaphore_PreventsRaceCondition_OneSuccess_OneFail()
        {
            BusinessLogic.BookingDelayMs = 500;
            BusinessLogic.SyncMode = "semaphore";
            BusinessLogic.TransactionMode = "none";

            var data1 = new { ClientId = 1, TrainerId = 1, DateTime = new DateTime(2030, 1, 1, 18, 0, 0), Duration = 60 };
            var data2 = new { ClientId = 2, TrainerId = 1, DateTime = new DateTime(2030, 1, 1, 18, 0, 0), Duration = 60 };

            var t1 = Task.Run(() => BusinessLogic.BookTraining(JsonSerializer.SerializeToElement(data1)));
            var t2 = Task.Run(() => BusinessLogic.BookTraining(JsonSerializer.SerializeToElement(data2)));
            var results = await Task.WhenAll(t1, t2);

            Assert.Equal(1, results.Count(r => r.Status == "success"));
            Assert.Equal(1, results.Count(r => r.Status == "error"));
            Assert.Equal(0, Database.GetAvailablePlaces(1));
        }

        [Fact]
        public void Test7_TimeRequest_ReturnsServerTime()
        {
            BusinessLogic.TimeResponseDelayMs = 200;

            var before = DateTime.UtcNow;
            var response = BusinessLogic.TimeRequest(JsonSerializer.SerializeToElement(new { }));
            var after = DateTime.UtcNow;

            Assert.Equal("success", response.Status);
            Assert.NotNull(response.Data);

            var data = JsonSerializer.SerializeToElement(response.Data);
            var serverTime = data.GetProperty("ServerTimeUtc").GetDateTime();

            Assert.InRange(serverTime, before.AddSeconds(-1), after.AddSeconds(1));
        }

        [Fact]
        public void Test8_RttMeasuresNetworkDelay()
        {
            BusinessLogic.TimeResponseDelayMs = 300;

            var serverTimeBefore = DateTime.UtcNow;
            var response = BusinessLogic.TimeRequest(JsonSerializer.SerializeToElement(new { }));
            var serverTimeAfter = DateTime.UtcNow;

            var data = JsonSerializer.SerializeToElement(response.Data);
            var serverTime = data.GetProperty("ServerTimeUtc").GetDateTime();

            Assert.True((serverTime - serverTimeBefore).Duration() < TimeSpan.FromMilliseconds(100));
            Assert.True((serverTimeAfter - serverTimeBefore).TotalMilliseconds >= 300);
        }

        [Fact]
        public void Test10_Transaction_Commit_SavesBothOperations()
        {
            Database.Reset();
            Database.Seed();
            BusinessLogic.SyncMode = "none";
            BusinessLogic.TransactionMode = "transaction";
            BusinessLogic.BookingDelayMs = 0;

            var data = JsonSerializer.SerializeToElement(new
            {
                ClientId = 1, TrainerId = 1,
                DateTime = new DateTime(2030, 1, 1, 18, 0, 0), Duration = 60
            });

            var response = BusinessLogic.BookTraining(data);

            Assert.Equal("success", response.Status);
            Assert.Equal(0, Database.GetAvailablePlaces(1));
            Assert.Equal(1, Database.CountTrainings());
        }

        [Fact]
        public void Test11_Transaction_Rollback_UndoesBothOperations()
        {
            Database.Reset();
            Database.Seed();
            BusinessLogic.SyncMode = "none";
            BusinessLogic.TransactionMode = "transaction-error";
            BusinessLogic.BookingDelayMs = 0;

            var data = JsonSerializer.SerializeToElement(new
            {
                ClientId = 1, TrainerId = 1,
                DateTime = new DateTime(2030, 1, 1, 18, 0, 0), Duration = 60
            });

            var response = BusinessLogic.BookTraining(data);

            Assert.Equal("error", response.Status);
            Assert.Equal(1, Database.GetAvailablePlaces(1));
            Assert.Equal(0, Database.CountTrainings());
        }

        [Fact]
        public void Test12_NoTransaction_WithError_LeavesPartialState()
        {
            Database.Reset();
            Database.Seed();
            BusinessLogic.SyncMode = "none";
            BusinessLogic.TransactionMode = "none-error";
            BusinessLogic.BookingDelayMs = 0;

            var data = JsonSerializer.SerializeToElement(new
            {
                ClientId = 1, TrainerId = 1,
                DateTime = new DateTime(2030, 1, 1, 18, 0, 0), Duration = 60
            });

            var response = BusinessLogic.BookTraining(data);

            Assert.Equal("error", response.Status);
            Assert.Equal(0, Database.GetAvailablePlaces(1));
            Assert.Equal(0, Database.CountTrainings());
        }

        [Fact]
        public void Test13_InitialState_PrimaryEqualsReplica()
        {
            Database.Reset();
            Database.Seed();

            int primaryPlaces = Database.GetAvailablePlacesFrom(InstanceRole.Primary, 1);
            int replicaPlaces = Database.GetAvailablePlacesFrom(InstanceRole.Replica, 1);

            Assert.Equal(primaryPlaces, replicaPlaces);
            Assert.True(primaryPlaces >= 0);
        }

        [Fact]
        public void Test14_Replication_AfterChange()
        {
            Database.Reset();
            Database.Seed();
            BusinessLogic.SyncMode = "none";
            BusinessLogic.TransactionMode = "none";
            BusinessLogic.BookingDelayMs = 0;

            var data = JsonSerializer.SerializeToElement(new
            {
                ClientId = 1, TrainerId = 1,
                DateTime = new DateTime(2030, 1, 1, 18, 0, 0), Duration = 60
            });

            var response = BusinessLogic.BookTraining(data);
            Assert.Equal("success", response.Status);

            int primaryPlaces = Database.GetAvailablePlacesFrom(InstanceRole.Primary, 1);
            int replicaPlaces = Database.GetAvailablePlacesFrom(InstanceRole.Replica, 1);

            Assert.Equal(primaryPlaces, replicaPlaces);
            Assert.Equal(0, primaryPlaces);
        }

        [Fact]
        public void Test15_DetectPrimaryUnavailable()
        {
            Database.SimulateFailure(InstanceRole.Primary);

            Assert.False(Database.IsAvailable(InstanceRole.Primary));
            Assert.True(Database.IsAvailable(InstanceRole.Replica));

            Database.SimulateRecovery(InstanceRole.Primary);
        }

        [Fact]
        public void Test16_SelectAvailableReplica()
        {
            Database.SimulateFailure(InstanceRole.Primary);

            Assert.True(Database.IsAvailable(InstanceRole.Replica));

            Database.SimulateRecovery(InstanceRole.Primary);
        }

        [Fact]
        public void Test17_Failover_RequestSucceedsOnReplica()
        {
            Database.SimulateFailure(InstanceRole.Primary);

            var handler = new RequestHandler();
            var request = new Request
            {
                Operation = "GetTrainingSlots",
                Data = JsonSerializer.SerializeToElement(new { }),
                ClientId = 1
            };

            var response = handler.Handle(request);

            Assert.Equal("success", response.Status);
            Assert.Equal(InstanceRole.Replica, Database.ActiveRole);

            Database.RecoverPrimary();
        }

        [Fact]
        public void Test18_Failover_UpdateSucceedsOnReplica()
        {
            Database.SimulateFailure(InstanceRole.Primary);
            Database.FailoverToReplica();

            var data = JsonSerializer.SerializeToElement(new
            {
                ClientId = 1, TrainerId = 1,
                DateTime = new DateTime(2030, 1, 1, 18, 0, 0), Duration = 60
            });

            var response = BusinessLogic.BookTraining(data);
            Assert.Equal("success", response.Status);

            Database.RecoverPrimary();
        }

        [Fact]
        public void Test19_RecoveryAndSync_PrimaryEqualsReplica()
        {
            Database.Reset();
            Database.Seed();
            BusinessLogic.SyncMode = "none";
            BusinessLogic.TransactionMode = "none";
            BusinessLogic.BookingDelayMs = 0;

            Database.SimulateFailure(InstanceRole.Primary);
            Database.FailoverToReplica();

            var data = JsonSerializer.SerializeToElement(new
            {
                ClientId = 1, TrainerId = 1,
                DateTime = new DateTime(2030, 1, 1, 18, 0, 0), Duration = 60
            });
            var response = BusinessLogic.BookTraining(data);
            Assert.Equal("success", response.Status);

            int replicaAfter = Database.GetAvailablePlacesFrom(InstanceRole.Replica, 1);
            Assert.Equal(0, replicaAfter);

            Database.RecoverPrimary();

            int primaryPlaces = Database.GetAvailablePlacesFrom(InstanceRole.Primary, 1);
            int replicaPlaces = Database.GetAvailablePlacesFrom(InstanceRole.Replica, 1);

            Assert.Equal(primaryPlaces, replicaPlaces);
            Assert.Equal(0, primaryPlaces);
        }

        [Fact]
        public void Test20_BothInstancesUnavailable_ReturnsError()
        {
            Database.SimulateFailure(InstanceRole.Primary);
            Database.SimulateFailure(InstanceRole.Replica);

            var handler = new RequestHandler();
            var request = new Request
            {
                Operation = "GetTrainingSlots",
                Data = JsonSerializer.SerializeToElement(new { }),
                ClientId = 1
            };

            var response = handler.Handle(request);

            Assert.Equal("error", response.Status);

            Database.SimulateRecovery(InstanceRole.Primary);
            Database.SimulateRecovery(InstanceRole.Replica);
            Database.RecoverPrimary();
        }
    }
}