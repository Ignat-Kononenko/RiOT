using System;
using System.Text.Json;
using FitnessServer.PubSub;

namespace FitnessServer
{
    public class RequestHandler
    {
        public Response Handle(Request request)
        {
            try
            {
                return Execute(request);
            }
            catch (InstanceUnavailableException ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [FAILOVER] {ex.Message}");

                if (Database.ActiveRole == InstanceRole.Primary && Database.IsAvailable(InstanceRole.Replica))
                {
                    try
                    {
                        Database.FailoverToReplica();
                        return Execute(request);
                    }
                    catch (Exception inner)
                    {
                        return new Response
                        {
                            Status = "error",
                            Message = $"Failover не удался: {inner.Message}"
                        };
                    }
                }

                return new Response
                {
                    Status = "error",
                    Message = $"Нет доступных экземпляров: {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                return new Response { Status = "error", Message = $"Внутренняя ошибка: {ex.Message}" };
            }
        }

        private Response Execute(Request request)
        {
            return request.Operation switch
            {
                "RegisterClient"           => BusinessLogic.RegisterClient(request.Data),
                "BookTraining"             => BusinessLogic.BookTraining(request.Data),
                "CancelTraining"           => BusinessLogic.CancelTraining(request.Data),
                "BuyMembership"            => BusinessLogic.BuyMembership(request.Data),
                "GetClients"               => BusinessLogic.GetClients(),
                "GetTrainers"              => BusinessLogic.GetTrainers(),
                "GetTrainingSlots"         => BusinessLogic.GetTrainingSlots(),
                "LongOperation"            => BusinessLogic.LongOperation(request.Data),
                "TimeRequest"              => BusinessLogic.TimeRequest(request.Data),
                "SetTransactionMode"       => BusinessLogic.SetTransactionMode(request.Data),
                "SetSyncMode"              => BusinessLogic.SetSyncMode(request.Data),
                "ResetDb"                  => BusinessLogic.ResetDb(request.Data),
                "SimulatePrimaryFailure"   => BusinessLogic.SimulatePrimaryFailure(request.Data),
                "RecoverPrimary"           => BusinessLogic.RecoverPrimary(request.Data),
                "SyncInstances"            => BusinessLogic.SyncInstances(request.Data),
                "GetInstanceState"         => BusinessLogic.GetInstanceState(request.Data),
                "AddAnalyticsSubscriber"   => AddAnalyticsSubscriber(),
                "SimulateSubscriberFailure" => SimulateSubscriberFailure(),
                "RecoverSubscriber"        => RecoverSubscriber(),
                "GetPubSubStatus"          => GetPubSubStatus(),
                "GetClusterState"              => GetClusterState(),
                "ElectLeader"                  => ElectLeader(),
                "SimulateNodeFailure"          => SimulateNodeFailure(request.Data),
                "RecoverNode"                  => RecoverNode(request.Data),
                "HeartbeatCheck"               => HeartbeatCheck(),
                "ProposeConsensus"             => ProposeConsensus(request.Data),
                "SetConsensusVote"             => SetConsensusVote(request.Data),
                "ResetCluster"                 => ResetCluster(),
                "LeaderFailureExperiment"      => LeaderFailureExperiment(),
                "ConsensusAllAvailableExperiment" => ConsensusAllAvailableExperiment(),
                "ConsensusNoMajorityExperiment"   => ConsensusNoMajorityExperiment(),
                _ => new Response { Status = "error", Message = $"Неизвестная операция: {request.Operation}" }
            };
        }

        private Response AddAnalyticsSubscriber()
        {
            PubSubManager.AddAnalyticsSubscriber();
            return new Response
            {
                Status = "success",
                Message = "AnalyticsService подписан на TrainingBooked. Publisher не изменялся.",
                Data = new { Status = PubSubManager.GetStatus() }
            };
        }

        private Response SimulateSubscriberFailure()
        {
            PubSubManager.SimulateScheduleServiceFailure();
            return new Response
            {
                Status = "success",
                Message = "ScheduleService переведён в состояние UNAVAILABLE.",
                Data = new { Status = PubSubManager.GetStatus() }
            };
        }

        private Response RecoverSubscriber()
        {
            PubSubManager.RecoverScheduleService();
            return new Response
            {
                Status = "success",
                Message = "ScheduleService восстановлен.",
                Data = new { Status = PubSubManager.GetStatus() }
            };
        }

        private Response GetPubSubStatus()
        {
            return new Response
            {
                Status = "success",
                Message = "Состояние Pub/Sub",
                Data = new
                {
                    State = PubSubManager.GetStatus(),
                    Broker1Available = PubSubManager.Broker1.IsAvailable,
                    Broker2Available = PubSubManager.Broker2.IsAvailable,
                    LoggerAvailable = PubSubManager.Logger.IsAvailable,
                    ScheduleServiceAvailable = PubSubManager.ScheduleService.IsAvailable,
                    NotificationServiceAvailable = PubSubManager.NotificationService.IsAvailable,
                    AnalyticsServiceAvailable = PubSubManager.AnalyticsService.IsAvailable
                }
            };
        }

        private Response GetClusterState()
        {
            return new Response
            {
                Status = "success",
                Message = "Состояние кластера",
                Data = ClusterManager.GetClusterState()
            };
        }

        private Response ElectLeader()
        {
            var r = ClusterManager.ElectLeader();
            return new Response
            {
                Status = r.Success ? "success" : "error",
                Message = r.Message,
                Data = r.Data ?? ClusterManager.GetClusterState()
            };
        }

        private Response SimulateNodeFailure(JsonElement data)
        {
            var nodeId = GetInt(data, "NodeId");
            var r = ClusterManager.SimulateNodeFailure(nodeId);
            return new Response
            {
                Status = r.Success ? "success" : "error",
                Message = r.Message,
                Data = r.Data ?? ClusterManager.GetClusterState()
            };
        }

        private Response RecoverNode(JsonElement data)
        {
            var nodeId = GetInt(data, "NodeId");
            var r = ClusterManager.RecoverNode(nodeId);
            return new Response
            {
                Status = r.Success ? "success" : "error",
                Message = r.Message,
                Data = r.Data ?? ClusterManager.GetClusterState()
            };
        }

        private Response HeartbeatCheck()
        {
            var r = ClusterManager.HeartbeatCheck();
            return new Response
            {
                Status = r.Success ? "success" : "error",
                Message = r.Message,
                Data = r.Data ?? ClusterManager.GetClusterState()
            };
        }

        private Response ProposeConsensus(JsonElement data)
        {
            var key = GetString(data, "Key", "TrainingStatus");
            var value = GetString(data, "Value", "Confirmed");

            var result = ClusterManager.Propose(key, value);

            return new Response
            {
                Status = "success",
                Message = result.Message,
                Data = result
            };
        }

        private Response SetConsensusVote(JsonElement data)
        {
            var nodeId = GetInt(data, "NodeId");
            var vote = GetBool(data, "Vote", true);

            var r = ClusterManager.SetVote(nodeId, vote);

            return new Response
            {
                Status = r.Success ? "success" : "error",
                Message = r.Message,
                Data = r.Data ?? ClusterManager.GetClusterState()
            };
        }

        private Response ResetCluster()
        {
            var r = ClusterManager.Reset();
            return new Response
            {
                Status = r.Success ? "success" : "error",
                Message = r.Message,
                Data = r.Data ?? ClusterManager.GetClusterState()
            };
        }

        private Response LeaderFailureExperiment()
        {
            var r = ClusterManager.RunLeaderFailureExperiment();
            return new Response
            {
                Status = r.Success ? "success" : "error",
                Message = r.Message,
                Data = r.Data ?? ClusterManager.GetClusterState()
            };
        }

        private Response ConsensusAllAvailableExperiment()
        {
            var result = ClusterManager.RunConsensusAllAvailableExperiment();
            return new Response
            {
                Status = "success",
                Message = result.Message,
                Data = result
            };
        }

        private Response ConsensusNoMajorityExperiment()
        {
            var result = ClusterManager.RunConsensusNoMajorityExperiment();
            return new Response
            {
                Status = "success",
                Message = result.Message,
                Data = result
            };
        }

        private static int GetInt(JsonElement data, string name, int fallback = 0)
        {
            if (data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty(name, out var p) &&
                p.TryGetInt32(out var v))
                return v;

            return fallback;
        }

        private static string GetString(JsonElement data, string name, string fallback)
        {
            if (data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty(name, out var p) &&
                p.ValueKind == JsonValueKind.String)
                return p.GetString() ?? fallback;

            return fallback;
        }

        private static bool GetBool(JsonElement data, string name, bool fallback)
        {
            if (data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty(name, out var p) &&
                (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False))
                return p.GetBoolean();

            return fallback;
        }
    }
}