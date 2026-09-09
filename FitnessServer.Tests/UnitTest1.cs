using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using Xunit;
using FitnessClient;

namespace FitnessServer.Tests
{
    public class FitnessServerTests : IDisposable
    {
        public FitnessServerTests()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8; // <-- Добавлено
            BusinessLogic.Reset();
            BusinessLogic.Seed();
            Console.WriteLine("Тест-класс инициализирован (сброс состояния)");
        }

        public void Dispose() { }

        [Fact]
        public void BookTraining_WithValidData_ReturnsSuccess()
        {
            Console.WriteLine("Тест 1: Успешное бронирование тренировки");
            var data = new
            {
                ClientId = 1,
                TrainerId = 1,
                DateTime = DateTime.Now.AddDays(1),
                Duration = 60
            };
            var jsonElement = JsonSerializer.SerializeToElement(data);

            var response = BusinessLogic.BookTraining(jsonElement);

            Assert.Equal("success", response.Status);
            Assert.Contains("забронирована", response.Message);
            Assert.NotNull(response.Data);
            Console.WriteLine("Тест 1 пройден");
        }

        [Fact]
        public void RequestHandler_UnknownOperation_ReturnsError()
        {
            Console.WriteLine("Тест 2: Неизвестная операция");
            var handler = new RequestHandler();
            var request = new FitnessServer.Request
            {
                Operation = "Unknown",
                Data = JsonSerializer.SerializeToElement(new { })
            };

            var response = handler.Handle(request);

            Assert.Equal("error", response.Status);
            Assert.Contains("Неизвестная операция", response.Message);
            Console.WriteLine("Тест 2 пройден");
        }

        [Fact]
        public void RegisterClient_WithEmptyName_ReturnsError()
        {
            Console.WriteLine("Тест 3: Регистрация с пустым именем");
            var data = new { Name = "", Email = "test@test.com", Phone = "+123" };
            var jsonElement = JsonSerializer.SerializeToElement(data);

            var response = BusinessLogic.RegisterClient(jsonElement);

            Assert.Equal("error", response.Status);
            Assert.Equal("Имя и Email обязательны.", response.Message);
            Console.WriteLine("Тест 3 пройден");
        }

        [Fact]
        public async Task Client_SendRequest_WhenServerUnavailable_ReturnsError()
        {
            Console.WriteLine("Тест 4: Сервер недоступен");
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Сервер недоступен"));

            var httpClient = new HttpClient(mockHandler.Object);
            var client = new Client(httpClient, "http://localhost:5000");
            var request = new FitnessClient.Request
            {
                Operation = "GetClients",
                Data = JsonSerializer.SerializeToElement(new { })
            };

            var response = await client.SendRequestAsync(request);

            Assert.Equal("error", response.Status);
            Assert.Contains("Сервер недоступен", response.Message);
            Console.WriteLine("Тест 4 пройден");
        }
    }
}