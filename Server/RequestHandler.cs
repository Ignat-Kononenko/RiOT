using System;
using System.Text.Json;

namespace FitnessServer
{
    public class RequestHandler
    {
        public Response Handle(Request request)
        {
            try
            {
                return request.Operation switch
                {
                    "RegisterClient" => BusinessLogic.RegisterClient(request.Data),
                    "BookTraining"   => BusinessLogic.BookTraining(request.Data),
                    "CancelTraining" => BusinessLogic.CancelTraining(request.Data),
                    "BuyMembership"  => BusinessLogic.BuyMembership(request.Data),
                    "GetClients"     => BusinessLogic.GetClients(),      // новое
                    "GetTrainers"    => BusinessLogic.GetTrainers(),     // новое
                    _ => new Response { Status = "error", Message = $"Неизвестная операция: {request.Operation}" }
                };
            }
            catch (Exception ex)
            {
                return new Response { Status = "error", Message = $"Внутренняя ошибка: {ex.Message}" };
            }
        }
    }
}