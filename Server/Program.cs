using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Threading.Tasks;

namespace FitnessServer
{
    class Program
    {
        static void Main(string[] args)
        {
            BusinessLogic.Seed();

            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddSingleton<RequestHandler>();

            var app = builder.Build();

            app.MapPost("/api", async (HttpContext context, RequestHandler handler) =>
            {
                var request = await context.Request.ReadFromJsonAsync<Request>();
                if (request == null)
                {
                    return Results.BadRequest(new Response
                    {
                        Status = "error",
                        Message = "Некорректный JSON запроса."
                    });
                }

                var response = handler.Handle(request);

                return Results.Json(response, statusCode: response.Status == "success" ? 200 : 400);
            });

            app.Run("http://localhost:5000");
        }
    }

    public class Request
    {
        public string Operation { get; set; }
        public JsonElement Data { get; set; }
    }

    public class Response
    {
        public string Status { get; set; }
        public string Message { get; set; }
        public object Data { get; set; }
    }
}