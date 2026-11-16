using System.Linq;
using Xunit;
using FitnessServer;

namespace FitnessServer.Tests
{
    public class ConsensusTests
    {
        public ConsensusTests()
        {
            ClusterManager.Reset();
        }

        [Fact]
        public void CreatesFiveNodesWithUniqueIds()
        {
            var nodes = ClusterManager.Nodes;

            Assert.Equal(5, nodes.Count);
            Assert.Equal(5, nodes.Select(n => n.Id).Distinct().Count());
        }

        [Fact]
        public void LeaderIsMaxAvailableNode()
        {
            var result = ClusterManager.ElectLeader();

            Assert.True(result.Success);
            Assert.Equal(5, ClusterManager.CurrentLeaderId);
            Assert.Equal(ClusterNodeRole.Leader, ClusterManager.Nodes.First(n => n.Id == 5).Role);
        }

        [Fact]
        public void FailedNodeCannotBeLeader()
        {
            ClusterManager.SimulateNodeFailure(5);
            ClusterManager.ElectLeader();

            Assert.Equal(4, ClusterManager.CurrentLeaderId);
        }

        [Fact]
        public void LeaderFailureTriggersReelection()
        {
            ClusterManager.ElectLeader();
            ClusterManager.SimulateNodeFailure(5);

            Assert.Equal(4, ClusterManager.CurrentLeaderId);
            Assert.Equal(ClusterNodeState.Failed, ClusterManager.Nodes.First(n => n.Id == 5).State);
        }

        [Fact]
        public void HeartbeatDetectsLeaderFailure()
        {
            ClusterManager.ElectLeader();
            ClusterManager.SimulateNodeFailure(5);

            var result = ClusterManager.HeartbeatCheck();

            Assert.True(result.Success);
            Assert.NotEqual(5, ClusterManager.CurrentLeaderId);
        }

        [Fact]
        public void ConsensusAchievedWhenAllAvailable()
        {
            ClusterManager.ElectLeader();

            var result = ClusterManager.Propose("TrainingStatus", "Confirmed");

            Assert.True(result.Achieved);
            Assert.Equal(5, result.Yes);
            Assert.Equal(3, result.Majority);
        }

        [Fact]
        public void ConsensusAchievedWithOneFailure()
        {
            ClusterManager.SimulateNodeFailure(5);

            var result = ClusterManager.Propose("TrainingStatus", "Confirmed");

            Assert.True(result.Achieved);
            Assert.Equal(4, result.Yes);
        }

        [Fact]
        public void ConsensusNotAchievedWithoutMajority()
        {
            ClusterManager.SimulateNodeFailure(3);
            ClusterManager.SimulateNodeFailure(4);
            ClusterManager.SimulateNodeFailure(5);

            var result = ClusterManager.Propose("TrainingStatus", "Confirmed");

            Assert.False(result.Achieved);
            Assert.True(result.Yes < result.Majority);
        }

        [Fact]
        public void NegativeVotesCanPreventConsensus()
        {
            ClusterManager.ElectLeader();

            ClusterManager.SetVote(1, false);
            ClusterManager.SetVote(2, false);
            ClusterManager.SetVote(3, false);

            var result = ClusterManager.Propose("TrainingStatus", "Confirmed");

            Assert.False(result.Achieved);
            Assert.Equal(2, result.Yes);
        }

        [Fact]
        public void RecoverNodeTriggersElectionOfHigherId()
        {
            ClusterManager.SimulateNodeFailure(5);
            Assert.Equal(4, ClusterManager.CurrentLeaderId);

            ClusterManager.RecoverNode(5);

            Assert.Equal(5, ClusterManager.CurrentLeaderId);
        }
    }
}