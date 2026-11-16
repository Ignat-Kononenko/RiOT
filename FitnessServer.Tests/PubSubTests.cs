using System;
using System.Threading.Tasks;
using Xunit;
using FitnessServer.PubSub;

namespace FitnessServer.Tests
{
    public class PubSubTests
    {
        private class TestSubscriber : SubscriberBase
        {
            public TestSubscriber(string name) : base(name, 0) { }

            protected override Task ProcessAsync(Event evt)
                => Task.CompletedTask;
        }

        [Fact]
        public void Event_CreatedCorrectly()
        {
            var evt = new Event
            {
                EventType = EventTypes.TrainingBooked,
                EntityId = 42
            };

            Assert.Equal(EventTypes.TrainingBooked, evt.EventType);
            Assert.Equal(42, evt.EntityId);
            Assert.True((DateTime.UtcNow - evt.Timestamp).TotalSeconds < 5);
        }

        [Fact]
        public async Task Publish_SubscriberReceivesEvent()
        {
            var broker = new Broker("TestBroker");
            var sub = new TestSubscriber("Test");
            broker.Subscribe(EventTypes.TrainingBooked, sub);

            var publisher = new Publisher(new[] { broker });

            await publisher.PublishAsync(new Event
            {
                EventType = EventTypes.TrainingBooked,
                EntityId = 1
            });

            await Task.Delay(300);

            Assert.Single(sub.ReceivedEvents);
        }

        [Fact]
        public async Task UnsubscribedSubscriber_DoesNotReceive()
        {
            var broker = new Broker("TestBroker");
            var sub = new TestSubscriber("Test");

            broker.Subscribe(EventTypes.TrainingCancelled, sub);

            var publisher = new Publisher(new[] { broker });

            await publisher.PublishAsync(new Event
            {
                EventType = EventTypes.TrainingBooked,
                EntityId = 1
            });

            await Task.Delay(300);

            Assert.Empty(sub.ReceivedEvents);
        }

        [Fact]
        public async Task MultipleSubscribers_ReceiveSameEvent()
        {
            var broker = new Broker("TestBroker");
            var s1 = new TestSubscriber("S1");
            var s2 = new TestSubscriber("S2");

            broker.Subscribe(EventTypes.TrainingBooked, s1);
            broker.Subscribe(EventTypes.TrainingBooked, s2);

            var publisher = new Publisher(new[] { broker });

            await publisher.PublishAsync(new Event
            {
                EventType = EventTypes.TrainingBooked,
                EntityId = 1
            });

            await Task.Delay(300);

            Assert.Single(s1.ReceivedEvents);
            Assert.Single(s2.ReceivedEvents);
        }

        [Fact]
        public async Task MultipleEventTypes_OnlyInterestedReceived()
        {
            var broker = new Broker("TestBroker");
            var sub = new TestSubscriber("S");

            broker.Subscribe(EventTypes.TrainingBooked, sub);
            var publisher = new Publisher(new[] { broker });

            await publisher.PublishAsync(new Event
            {
                EventType = EventTypes.TrainingBooked,
                EntityId = 1
            });

            await publisher.PublishAsync(new Event
            {
                EventType = EventTypes.TrainingCancelled,
                EntityId = 2
            });

            await Task.Delay(300);

            Assert.Single(sub.ReceivedEvents);
            Assert.Equal(EventTypes.TrainingBooked, sub.ReceivedEvents[0].EventType);
        }

        [Fact]
        public async Task SubscriberFailure_DoesNotStopOthers()
        {
            var broker = new Broker("TestBroker") { MaxRetries = 1 };
            var bad = new TestSubscriber("Bad") { IsAvailable = false };
            var good = new TestSubscriber("Good");

            broker.Subscribe(EventTypes.TrainingBooked, bad);
            broker.Subscribe(EventTypes.TrainingBooked, good);

            var publisher = new Publisher(new[] { broker });

            await publisher.PublishAsync(new Event
            {
                EventType = EventTypes.TrainingBooked,
                EntityId = 1
            });

            await Task.Delay(500);

            Assert.Empty(bad.ReceivedEvents);
            Assert.Single(good.ReceivedEvents);
        }

        [Fact]
        public async Task NewSubscriber_AddedWithoutPublisherChange()
        {
            var broker = new Broker("TestBroker");
            var publisher = new Publisher(new[] { broker });
            var newSub = new TestSubscriber("New");

            broker.Subscribe(EventTypes.TrainingBooked, newSub);

            await publisher.PublishAsync(new Event
            {
                EventType = EventTypes.TrainingBooked,
                EntityId = 1
            });

            await Task.Delay(300);

            Assert.Single(newSub.ReceivedEvents);
        }

        [Fact]
        public async Task SecondBroker_DeliversWhenFirstUnavailable()
        {
            var b1 = new Broker("B1") { IsAvailable = false };
            var b2 = new Broker("B2");

            var sub = new TestSubscriber("S");
            b2.Subscribe(EventTypes.TrainingBooked, sub);

            var publisher = new Publisher(new[] { b1, b2 });

            var result = await publisher.PublishAsync(new Event
            {
                EventType = EventTypes.TrainingBooked,
                EntityId = 1
            });

            await Task.Delay(300);

            Assert.True(result.Success);
            Assert.Single(sub.ReceivedEvents);
        }
    }
}