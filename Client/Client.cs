using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace FitnessClient
{
    // Модели для десериализации списков
    public class ClientInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }

    public class TrainerInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Specialization { get; set; } = string.Empty;
    }

    public class Client
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public Client(string baseUrl = "http://localhost:5000")
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(5); 
            _baseUrl = baseUrl;
        }

        public Client(HttpClient httpClient, string baseUrl = "http://localhost:5000")
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _baseUrl = baseUrl;
        }

        public async Task<Response> SendRequestAsync(Request request)
        {
            try
            {
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var httpResponse = await _httpClient.PostAsync(_baseUrl + "/api", content);
                var responseJson = await httpResponse.Content.ReadAsStringAsync();

                if (httpResponse.IsSuccessStatusCode)
                {
                    try
                    {
                        return JsonSerializer.Deserialize<Response>(responseJson, _jsonOptions)
                               ?? new Response { Status = "error", Message = "Не удалось разобрать ответ" };
                    }
                    catch (JsonException jsonEx)
                    {
                        return new Response { Status = "error", Message = $"Ошибка JSON: {jsonEx.Message}" };
                    }
                }
                else
                {
                    try
                    {
                        var errorResponse = JsonSerializer.Deserialize<Response>(responseJson, _jsonOptions);
                        return errorResponse ?? new Response { Status = "error", Message = $"Ошибка HTTP {httpResponse.StatusCode}" };
                    }
                    catch
                    {
                        return new Response { Status = "error", Message = $"Ошибка HTTP {httpResponse.StatusCode}" };
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                return new Response { Status = "error", Message = $"Сервер недоступен: {ex.Message}" };
            }
            catch (Exception ex)
            {
                return new Response { Status = "error", Message = $"Ошибка сети: {ex.Message}" };
            }
        }

        public async Task<List<ClientInfo>> GetClientsAsync()
        {
            var request = new Request { Operation = "GetClients", Data = JsonSerializer.SerializeToElement(new { }) };
            var response = await SendRequestAsync(request);
            if (response.Status == "success" && response.Data != null)
            {
                try
                {
                    var element = (JsonElement)response.Data;
                    return element.Deserialize<List<ClientInfo>>(_jsonOptions) ?? new List<ClientInfo>();
                }
                catch
                {
                    return new List<ClientInfo>();
                }
            }
            return new List<ClientInfo>();
        }

        public async Task<List<TrainerInfo>> GetTrainersAsync()
        {
            var request = new Request { Operation = "GetTrainers", Data = JsonSerializer.SerializeToElement(new { }) };
            var response = await SendRequestAsync(request);
            if (response.Status == "success" && response.Data != null)
            {
                try
                {
                    var element = (JsonElement)response.Data;
                    return element.Deserialize<List<TrainerInfo>>(_jsonOptions) ?? new List<TrainerInfo>();
                }
                catch
                {
                    return new List<TrainerInfo>();
                }
            }
            return new List<TrainerInfo>();
        }
    }

    public class Request
    {
        public string Operation { get; set; } = string.Empty;
        public JsonElement? Data { get; set; }
    }

    public class Response
    {
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public object Data { get; set; }
    }
}