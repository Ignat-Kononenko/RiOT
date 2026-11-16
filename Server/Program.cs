using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FitnessServer.PubSub;

namespace FitnessServer
{
    class Program
    {
        private static int _clientCounter = 0;

        static void Main(string[] args)
        {
            bool recreateDb = args.Length > 0 && args[0] == "recreate";

            WaitForDatabase();

            Database.Initialize(recreate: recreateDb);
            Database.Seed();

            PubSubManager.Initialize();
            ClusterManager.Initialize();

            BusinessLogic.SyncMode = "lock";
            BusinessLogic.TransactionMode = "none";
            BusinessLogic.BookingDelayMs = 3000;
            BusinessLogic.LongOperationDelayMs = 10000;
            BusinessLogic.TimeResponseDelayMs = 300;

            Console.WriteLine($"SYNC MODE: {BusinessLogic.SyncMode}");
            Console.WriteLine($"TX MODE: {BusinessLogic.TransactionMode}");
            Console.WriteLine($"PRIMARY DB: {new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(Database.PrimaryConnectionString).InitialCatalog}");
            Console.WriteLine($"REPLICA DB: {new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(Database.ReplicaConnectionString).InitialCatalog}");
            Console.WriteLine($"ACTIVE: {Database.ActiveRole}");
            Console.WriteLine($"PUBSUB: {PubSubManager.GetStatus()}");
            Console.WriteLine("CLUSTER: Node 1..Node 5 initialized");

            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSingleton<RequestHandler>();
            var app = builder.Build();

            app.MapPost("/api", async (HttpContext context, RequestHandler handler) =>
            {
                Request? request;
                try
                {
                    request = await context.Request.ReadFromJsonAsync<Request>();
                }
                catch (JsonException ex)
                {
                    return Results.BadRequest(new Response { Status = "error", Message = $"Некорректный JSON: {ex.Message}" });
                }

                if (request == null)
                    return Results.BadRequest(new Response { Status = "error", Message = "Некорректный JSON запроса." });

                int clientId = request.ClientId > 0 ? request.ClientId : Interlocked.Increment(ref _clientCounter);

                var start = DateTime.Now;
                Console.WriteLine($"[{start:HH:mm:ss.fff}] Client {clientId} -- START | {request.Operation} | Thread: {Environment.CurrentManagedThreadId}");

                Response response;
                try
                {
                    response = await Task.Run(() => handler.Handle(request), context.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -- CANCELLED | {request.Operation}");
                    return Results.StatusCode(499);
                }
                catch (Exception ex)
                {
                    response = new Response { Status = "error", Message = $"Ошибка обработки: {ex.Message}" };
                }

                var end = DateTime.Now;
                Console.WriteLine($"[{end:HH:mm:ss.fff}] Client {clientId} -- END | {request.Operation} | Duration: {(end - start).TotalSeconds:F2}s | {response.Status}");

                return Results.Json(response, statusCode: response.Status == "success" ? 200 : 400);
            });

            var appUrl = Environment.GetEnvironmentVariable("APP_URL") is { Length: > 0 } u
                ? u
                : "http://0.0.0.0:5000";

            // ЧТЕНИЕ ПЕРЕМЕННОЙ ИЗ CONFIGMAP
            var appMode = Environment.GetEnvironmentVariable("APP_MODE") ?? "default";
            Console.WriteLine($"[CONFIG] APP_MODE={appMode}");
            
            Console.WriteLine($"SERVER URL: {appUrl}");
            app.Run(appUrl);
        }

        private static void WaitForDatabase()
        {
            var attempts = int.TryParse(
                Environment.GetEnvironmentVariable("DB_WAIT_ATTEMPTS"), out var n) ? n : 30;

            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
                Database.PrimaryConnectionString)
            {
                InitialCatalog = "master"
            };

            for (int i = 1; i <= attempts; i++)
            {
                try
                {
                    using var conn = new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString);
                    conn.Open();
                    Console.WriteLine("[WAIT] SQL Server доступен.");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WAIT] SQL Server не готов ({i}/{attempts}): {ex.Message}");
                    Thread.Sleep(3000);
                }
            }

            throw new Exception("SQL Server не стал доступен за отведённое время.");
        }
    }

    public class Request
    {
        public string Operation { get; set; } = string.Empty;
        public JsonElement Data { get; set; }
        public int ClientId { get; set; }
    }

    public class Response
    {
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public object? Data { get; set; }
    }
}