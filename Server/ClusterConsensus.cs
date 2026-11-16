using System;
using System.Collections.Generic;
using System.Linq;

namespace FitnessServer
{
    public enum ClusterNodeState { Available, Failed }
    public enum ClusterNodeRole { Leader, Follower }

    public class ClusterNode
    {
        public int Id { get; }
        public ClusterNodeState State { get; private set; } = ClusterNodeState.Available;
        public ClusterNodeRole Role { get; private set; } = ClusterNodeRole.Follower;
        public bool VoteYes { get; set; } = true;

        public ClusterNode(int id)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            Id = id;
        }

        public bool IsAvailable => State == ClusterNodeState.Available;
        public string NodeLabel => $"Node {Id}";

        public void SetAvailable(bool available)
        {
            State = available ? ClusterNodeState.Available : ClusterNodeState.Failed;
            if (!available) Role = ClusterNodeRole.Follower;
            ClusterManager.Log($"{NodeLabel} -> {(available ? "AVAILABLE" : "FAILED")}");
        }

        public void SetRole(ClusterNodeRole role)
        {
            Role = role;
        }

        public bool SendHeartbeat(ClusterNode target)
        {
            ClusterManager.Log($"{NodeLabel} -> {target.NodeLabel}: HEARTBEAT?");
            return target.ReceiveHeartbeat(this);
        }

        public bool ReceiveHeartbeat(ClusterNode from)
        {
            if (!IsAvailable)
            {
                ClusterManager.Log($"{NodeLabel} -> {from.NodeLabel}: NO RESPONSE");
                return false;
            }

            ClusterManager.Log($"{NodeLabel} -> {from.NodeLabel}: ALIVE");
            return true;
        }

        public bool Vote(ConsensusProposal proposal)
        {
            if (!IsAvailable)
            {
                ClusterManager.Log($"{NodeLabel} -> NO RESPONSE ({proposal.Key}={proposal.Value})");
                return false;
            }

            ClusterManager.Log($"{NodeLabel} -> {(VoteYes ? "YES" : "NO")} ({proposal.Key}={proposal.Value})");
            return VoteYes;
        }

        public override string ToString()
            => $"{NodeLabel} Status: {State.ToString().ToUpperInvariant()} Role: {Role.ToString().ToUpperInvariant()}";
    }

    public class ConsensusProposal
    {
        public int Id { get; set; }
        public string Key { get; set; } = "TrainingStatus";
        public string Value { get; set; } = "Confirmed";
        public int ProposedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ConsensusResult
    {
        public bool Achieved { get; set; }
        public int Yes { get; set; }
        public int No { get; set; }
        public int NoResponse { get; set; }
        public int Majority { get; set; }
        public int? LeaderId { get; set; }
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class ClusterOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public object? Data { get; set; }
    }

    public static class ClusterManager
    {
        private static readonly object _sync = new();
        private static List<ClusterNode> _nodes = new();
        private static int _proposalCounter;

        public static int CurrentLeaderId { get; private set; }
        public static int Term { get; private set; } = 1;
        public static ConsensusResult? LastConsensus { get; private set; }

        public static IReadOnlyList<ClusterNode> Nodes
        {
            get { lock (_sync) return _nodes.ToList(); }
        }

        public static void Log(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }

        public static void Initialize()
        {
            lock (_sync)
            {
                if (_nodes.Count > 0) return;

                _nodes = Enumerable.Range(1, 5).Select(i => new ClusterNode(i)).ToList();
                Term = 1;
                CurrentLeaderId = 0;
                LastConsensus = null;
                _proposalCounter = 0;

                Log("Cluster initialized: Node 1..Node 5");
                ElectLeaderInternal();
            }
        }

        public static ClusterOperationResult Reset()
        {
            lock (_sync)
            {
                _nodes = Enumerable.Range(1, 5).Select(i => new ClusterNode(i)).ToList();
                Term = 1;
                CurrentLeaderId = 0;
                LastConsensus = null;
                _proposalCounter = 0;

                Log("Cluster reset");
                var election = ElectLeaderInternal();
                return new ClusterOperationResult
                {
                    Success = true,
                    Message = election.Message,
                    Data = GetClusterStateInternal()
                };
            }
        }

        public static ClusterOperationResult ElectLeader()
        {
            lock (_sync) return ElectLeaderInternal();
        }

        private static ClusterOperationResult ElectLeaderInternal()
        {
            Log("Election started");

            foreach (var node in _nodes)
                Log($"{node.NodeLabel} -> {node.State.ToString().ToUpperInvariant()}");

            var available = _nodes
                .Where(n => n.IsAvailable)
                .OrderByDescending(n => n.Id)
                .ToList();

            foreach (var node in _nodes.Where(n => n.IsAvailable))
                node.SetRole(ClusterNodeRole.Follower);

            if (available.Count == 0)
            {
                CurrentLeaderId = 0;
                Log("No available nodes. Leader not elected.");
                return new ClusterOperationResult
                {
                    Success = false,
                    Message = "Нет доступных узлов для выбора лидера.",
                    Data = GetClusterStateInternal()
                };
            }

            var leader = available.First();
            leader.SetRole(ClusterNodeRole.Leader);
            CurrentLeaderId = leader.Id;
            Term++;

            Log($"{leader.NodeLabel} elected as LEADER (term {Term})");

            return new ClusterOperationResult
            {
                Success = true,
                Message = $"Лидер выбран: Node {CurrentLeaderId}",
                Data = GetClusterStateInternal()
            };
        }

        public static ClusterOperationResult SimulateNodeFailure(int nodeId)
        {
            lock (_sync) return SimulateNodeFailureInternal(nodeId);
        }

        private static ClusterOperationResult SimulateNodeFailureInternal(int nodeId)
        {
            var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null)
            {
                return new ClusterOperationResult
                {
                    Success = false,
                    Message = $"Node {nodeId} не найден.",
                    Data = GetClusterStateInternal()
                };
            }

            if (!node.IsAvailable)
            {
                return new ClusterOperationResult
                {
                    Success = false,
                    Message = $"Node {nodeId} уже недоступен.",
                    Data = GetClusterStateInternal()
                };
            }

            node.SetAvailable(false);

            if (node.Id == CurrentLeaderId)
            {
                Log($"{node.NodeLabel} -> HEARTBEAT TIMEOUT");
                Log($"{node.NodeLabel} -> FAILED");
                CurrentLeaderId = 0;
                var election = ElectLeaderInternal();

                return new ClusterOperationResult
                {
                    Success = true,
                    Message = $"Node {nodeId} FAILED. {election.Message}",
                    Data = GetClusterStateInternal()
                };
            }

            return new ClusterOperationResult
            {
                Success = true,
                Message = $"Node {nodeId} FAILED.",
                Data = GetClusterStateInternal()
            };
        }

        public static ClusterOperationResult RecoverNode(int nodeId)
        {
            lock (_sync)
            {
                var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
                if (node == null)
                {
                    return new ClusterOperationResult
                    {
                        Success = false,
                        Message = $"Node {nodeId} не найден.",
                        Data = GetClusterStateInternal()
                    };
                }

                if (node.IsAvailable)
                {
                    return new ClusterOperationResult
                    {
                        Success = true,
                        Message = $"Node {nodeId} уже доступен.",
                        Data = GetClusterStateInternal()
                    };
                }

                node.SetAvailable(true);
                node.SetRole(ClusterNodeRole.Follower);

                if (CurrentLeaderId == 0 || node.Id > CurrentLeaderId)
                    ElectLeaderInternal();

                return new ClusterOperationResult
                {
                    Success = true,
                    Message = $"Node {nodeId} восстановлен.",
                    Data = GetClusterStateInternal()
                };
            }
        }

        public static ClusterOperationResult HeartbeatCheck()
        {
            lock (_sync)
            {
                if (CurrentLeaderId == 0)
                {
                    Log("Heartbeat: leader not elected");
                    var election = ElectLeaderInternal();
                    return new ClusterOperationResult
                    {
                        Success = election.Success,
                        Message = $"Heartbeat: лидер не был выбран. {election.Message}",
                        Data = GetClusterStateInternal()
                    };
                }

                var leader = _nodes.FirstOrDefault(n => n.Id == CurrentLeaderId);

                if (leader == null || !leader.IsAvailable)
                {
                    Log($"Node {CurrentLeaderId} -> HEARTBEAT TIMEOUT");
                    if (leader != null && leader.IsAvailable)
                        leader.SetAvailable(false);

                    CurrentLeaderId = 0;
                    var election = ElectLeaderInternal();

                    return new ClusterOperationResult
                    {
                        Success = true,
                        Message = $"Лидер Node {CurrentLeaderId} недоступен. {election.Message}",
                        Data = GetClusterStateInternal()
                    };
                }

                int alive = 0;
                int noResponse = 0;

                foreach (var node in _nodes.Where(n => n.Id != leader.Id && n.IsAvailable))
                {
                    if (node.SendHeartbeat(leader)) alive++;
                    else noResponse++;
                }

                Log($"Heartbeat result: ALIVE={alive}, NO RESPONSE={noResponse}");

                return new ClusterOperationResult
                {
                    Success = true,
                    Message = $"Heartbeat выполнен: ALIVE={alive}, NO RESPONSE={noResponse}",
                    Data = GetClusterStateInternal()
                };
            }
        }

        public static ClusterOperationResult SetVote(int nodeId, bool yes)
        {
            lock (_sync)
            {
                var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
                if (node == null)
                {
                    return new ClusterOperationResult
                    {
                        Success = false,
                        Message = $"Node {nodeId} не найден.",
                        Data = GetClusterStateInternal()
                    };
                }

                node.VoteYes = yes;
                Log($"{node.NodeLabel} vote preference set to {(yes ? "YES" : "NO")}");

                return new ClusterOperationResult
                {
                    Success = true,
                    Message = $"Голос Node {nodeId} = {(yes ? "YES" : "NO")}",
                    Data = GetClusterStateInternal()
                };
            }
        }

        public static ConsensusResult Propose(string key, string value)
        {
            lock (_sync) return ProposeInternal(key, value);
        }

        private static ConsensusResult ProposeInternal(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key)) key = "TrainingStatus";
            if (string.IsNullOrWhiteSpace(value)) value = "Confirmed";

            var leader = CurrentLeaderId == 0
                ? null
                : _nodes.FirstOrDefault(n => n.Id == CurrentLeaderId);

            if (leader == null || !leader.IsAvailable)
            {
                var failed = new ConsensusResult
                {
                    Achieved = false,
                    Yes = 0,
                    No = 0,
                    NoResponse = _nodes.Count,
                    Majority = _nodes.Count / 2 + 1,
                    Key = key,
                    Value = value,
                    Message = "CONSENSUS NOT ACHIEVED: нет доступного лидера."
                };

                LastConsensus = failed;
                Log(failed.Message);
                return failed;
            }

            _proposalCounter++;

            var proposal = new ConsensusProposal
            {
                Id = _proposalCounter,
                Key = key,
                Value = value,
                ProposedBy = leader.Id
            };

            Log($"Leader {leader.NodeLabel} -> PROPOSE {proposal.Key}={proposal.Value} (proposal {proposal.Id})");

            int yes = 0;
            int no = 0;
            int noResponse = 0;

            foreach (var node in _nodes)
            {
                if (!node.IsAvailable)
                {
                    noResponse++;
                    Log($"{node.NodeLabel} -> NO RESPONSE ({proposal.Key}={proposal.Value})");
                    continue;
                }

                if (node.Vote(proposal)) yes++;
                else no++;
            }

            int majority = _nodes.Count / 2 + 1;
            bool achieved = yes >= majority;

            Log($"YES = {yes}; NO = {no}; NO RESPONSE = {noResponse}; Majority = {majority}");
            Log(achieved ? "CONSENSUS ACHIEVED" : "CONSENSUS NOT ACHIEVED");

            var result = new ConsensusResult
            {
                Achieved = achieved,
                Yes = yes,
                No = no,
                NoResponse = noResponse,
                Majority = majority,
                LeaderId = leader.Id,
                Key = key,
                Value = value,
                Message = achieved ? "CONSENSUS ACHIEVED" : "CONSENSUS NOT ACHIEVED"
            };

            LastConsensus = result;
            return result;
        }

        public static ClusterOperationResult RunLeaderFailureExperiment()
        {
            lock (_sync)
            {
                if (CurrentLeaderId == 0) ElectLeaderInternal();

                var leader = _nodes.FirstOrDefault(n => n.Id == CurrentLeaderId);
                if (leader == null)
                {
                    return new ClusterOperationResult
                    {
                        Success = false,
                        Message = "Лидер не выбран.",
                        Data = GetClusterStateInternal()
                    };
                }

                Log($"[Experiment] Leader failure: {leader.NodeLabel}");
                return SimulateNodeFailureInternal(leader.Id);
            }
        }

        public static ConsensusResult RunConsensusAllAvailableExperiment()
        {
            lock (_sync)
            {
                foreach (var node in _nodes)
                {
                    if (!node.IsAvailable) node.SetAvailable(true);
                    node.SetRole(ClusterNodeRole.Follower);
                    node.VoteYes = true;
                }

                CurrentLeaderId = 0;
                ElectLeaderInternal();
                return ProposeInternal("TrainingStatus", "Confirmed");
            }
        }

        public static ConsensusResult RunConsensusNoMajorityExperiment()
        {
            lock (_sync)
            {

                foreach (var node in _nodes)
                {
                    if (!node.IsAvailable) node.SetAvailable(true);
                    node.SetRole(ClusterNodeRole.Follower);
                    node.VoteYes = true;
                }

                CurrentLeaderId = 0;
                ElectLeaderInternal();

                SimulateNodeFailureInternal(5);
                SimulateNodeFailureInternal(4);
                SimulateNodeFailureInternal(3);

                return ProposeInternal("TrainingStatus", "Confirmed");
            }
        }

        public static object GetClusterState()
        {
            lock (_sync) return GetClusterStateInternal();
        }

        private static object GetClusterStateInternal()
        {
            return new
            {
                Term,
                Majority = _nodes.Count / 2 + 1,
                LeaderId = CurrentLeaderId == 0 ? (int?)null : CurrentLeaderId,
                Leader = CurrentLeaderId == 0 ? "NONE" : $"Node {CurrentLeaderId}",
                Nodes = _nodes.Select(n => new
                {
                    n.Id,
                    State = n.State.ToString().ToUpperInvariant(),
                    Role = n.Role.ToString().ToUpperInvariant(),
                    Vote = n.VoteYes ? "YES" : "NO"
                }).ToList(),
                LastConsensus = LastConsensus == null ? null : new
                {
                    LastConsensus.Achieved,
                    LastConsensus.Yes,
                    LastConsensus.No,
                    LastConsensus.NoResponse,
                    LastConsensus.Majority,
                    LastConsensus.Key,
                    LastConsensus.Value,
                    LastConsensus.Message
                }
            };
        }
    }
}