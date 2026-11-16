using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FitnessServer.PubSub
{
    public static class EventTypes
    {
        public const string TrainingBooked = "TrainingBooked";
        public const string TrainingCancelled = "TrainingCancelled";
        public const string MembershipPurchased = "MembershipPurchased";
    }

    public class Event
    {
        public string EventType { get; set; } = string.Empty;
        public int EntityId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> Data { get; set; } = new();
    }

    public class SubscriberUnavailableException : Exception
    {
        public SubscriberUnavailableException(string name)
            : base($"Subscriber {name} unavailable") { }
    }

    public interface ISubscriber
    {
        string Name { get; }
        bool IsAvailable { get; set; }
        Task HandleAsync(Event evt);
    }

    public abstract class SubscriberBase : ISubscriber
    {
        public string Name { get; }
        public bool IsAvailable { get; set; } = true;
        public int DelayMs { get; set; }
        public List<Event> ReceivedEvents { get; } = new();

        protected SubscriberBase(string name, int delayMs)
        {
            Name = name;
            DelayMs = delayMs;
        }

        public async Task HandleAsync(Event evt)
        {
            if (!IsAvailable)
                throw new SubscriberUnavailableException(Name);

            if (DelayMs > 0)
                await Task.Delay(DelayMs);

            await ProcessAsync(evt);

            lock (ReceivedEvents)
                ReceivedEvents.Add(evt);
        }

        protected abstract Task ProcessAsync(Event evt);
    }

    public class LoggerSubscriber : SubscriberBase
    {
        public LoggerSubscriber(int delayMs = 1000) : base("Logger", delayMs) { }

        protected override Task ProcessAsync(Event evt)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Logger -- {evt.EventType} EntityId={evt.EntityId}");
            return Task.CompletedTask;
        }
    }

    public class ScheduleServiceSubscriber : SubscriberBase
    {
        public ScheduleServiceSubscriber(int delayMs = 3000) : base("ScheduleService", delayMs) { }

        protected override Task ProcessAsync(Event evt)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ScheduleService -- {evt.EventType} EntityId={evt.EntityId}");
            return Task.CompletedTask;
        }
    }

    public class NotificationServiceSubscriber : SubscriberBase
    {
        public NotificationServiceSubscriber(int delayMs = 2000) : base("NotificationService", delayMs) { }

        protected override Task ProcessAsync(Event evt)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] NotificationService -- {evt.EventType} EntityId={evt.EntityId}");
            return Task.CompletedTask;
        }
    }

    public class AnalyticsServiceSubscriber : SubscriberBase
    {
        public AnalyticsServiceSubscriber(int delayMs = 500) : base("AnalyticsService", delayMs) { }

        protected override Task ProcessAsync(Event evt)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] AnalyticsService -- {evt.EventType} EntityId={evt.EntityId}");
            return Task.CompletedTask;
        }
    }

    public class Broker
    {
        public string Name { get; }
        public bool IsAvailable { get; set; } = true;
        public int MaxRetries { get; set; } = 3;

        private readonly Dictionary<string, List<ISubscriber>> _subscriptions = new();
        private readonly ConcurrentQueue<Event> _queue = new();
        private readonly SemaphoreSlim _signal = new(0);
        private readonly CancellationTokenSource _cts = new();

        public Broker(string name)
        {
            Name = name;
            _ = Task.Run(ProcessQueueAsync);
        }

        public void Subscribe(string eventType, ISubscriber subscriber)
        {
            lock (_subscriptions)
            {
                if (!_subscriptions.ContainsKey(eventType))
                    _subscriptions[eventType] = new List<ISubscriber>();

                if (!_subscriptions[eventType].Contains(subscriber))
                    _subscriptions[eventType].Add(subscriber);
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {Name} -- SUBSCRIBE {subscriber.Name} -> {eventType}");
        }

        public void Unsubscribe(string eventType, ISubscriber subscriber)
        {
            lock (_subscriptions)
            {
                if (_subscriptions.TryGetValue(eventType, out var list))
                    list.Remove(subscriber);
            }
        }

        public Task PublishAsync(Event evt)
        {
            if (!IsAvailable)
                throw new InvalidOperationException($"Broker {Name} unavailable");

            _queue.Enqueue(evt);
            _signal.Release();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {Name} -- EVENT RECEIVED {evt.EventType} EntityId={evt.EntityId}");
            return Task.CompletedTask;
        }

        private async Task ProcessQueueAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await _signal.WaitAsync(_cts.Token);

                    if (_queue.TryDequeue(out var evt))
                        await DeliverAsync(evt);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{Name}] Queue error: {ex.Message}");
                }
            }
        }

        private async Task DeliverAsync(Event evt)
        {
            List<ISubscriber> subscribers;

            lock (_subscriptions)
            {
                if (!_subscriptions.TryGetValue(evt.EventType, out var list) || list.Count == 0)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {Name} -- NO SUBSCRIBERS for {evt.EventType}");
                    return;
                }

                subscribers = list.ToList();
            }

            var tasks = subscribers.Select(sub => DeliverToSubscriberWithRetryAsync(evt, sub));
            await Task.WhenAll(tasks);
        }

        private async Task DeliverToSubscriberWithRetryAsync(Event evt, ISubscriber sub)
        {
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {Name} -- {sub.Name} attempt {attempt}");
                    await sub.HandleAsync(evt);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {Name} -- {sub.Name} SUCCESS");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {Name} -- {sub.Name} ERROR: {ex.Message}");

                    if (attempt < MaxRetries)
                        await Task.Delay(500 * attempt);
                }
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {Name} -- {sub.Name} FAILED after {MaxRetries} attempts");
        }
    }

    public class PublishResult
    {
        public bool Success { get; set; }
        public List<string> Details { get; set; } = new();
    }

    public class Publisher
    {
        private readonly List<Broker> _brokers;

        public Publisher(IEnumerable<Broker> brokers)
        {
            _brokers = brokers.ToList();
        }

        public async Task<PublishResult> PublishAsync(Event evt)
        {
            var result = new PublishResult { Success = false };

            foreach (var broker in _brokers)
            {
                try
                {
                    await broker.PublishAsync(evt);
                    result.Success = true;
                    result.Details.Add($"{broker.Name}: OK");
                }
                catch (Exception ex)
                {
                    result.Details.Add($"{broker.Name}: {ex.Message}");
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Publisher -- PUBLISH ERROR {broker.Name}: {ex.Message}");
                }
            }

            return result;
        }
    }

    public static class PubSubManager
    {
        private static bool _initialized;
        private static readonly object _lock = new();

        public static Broker Broker1 { get; private set; } = null!;
        public static Broker Broker2 { get; private set; } = null!;
        public static Publisher Publisher { get; private set; } = null!;

        public static LoggerSubscriber Logger { get; } = new();
        public static ScheduleServiceSubscriber ScheduleService { get; } = new();
        public static NotificationServiceSubscriber NotificationService { get; } = new();
        public static AnalyticsServiceSubscriber AnalyticsService { get; } = new();

        public static void Initialize()
        {
            lock (_lock)
            {
                if (_initialized) return;

                Broker1 = new Broker("Broker1");
                Broker2 = new Broker("Broker2");
                Publisher = new Publisher(new[] { Broker1, Broker2 });

                Broker1.Subscribe(EventTypes.TrainingBooked, Logger);
                Broker1.Subscribe(EventTypes.TrainingCancelled, Logger);
                Broker1.Subscribe(EventTypes.MembershipPurchased, Logger);

                Broker2.Subscribe(EventTypes.TrainingBooked, ScheduleService);
                Broker2.Subscribe(EventTypes.TrainingBooked, NotificationService);
                Broker2.Subscribe(EventTypes.TrainingCancelled, NotificationService);
                Broker2.Subscribe(EventTypes.MembershipPurchased, NotificationService);

                _initialized = true;
                Console.WriteLine("[PubSub] Initialized with 2 brokers");
            }
        }

        public static void EnsureInitialized()
        {
            if (!_initialized)
                Initialize();
        }

        public static async Task PublishAsync(Event evt)
        {
            EnsureInitialized();
            await Publisher.PublishAsync(evt);
        }

        public static void Publish(Event evt)
        {
            EnsureInitialized();
            Publisher.PublishAsync(evt).GetAwaiter().GetResult();
        }

        public static void AddAnalyticsSubscriber()
        {
            EnsureInitialized();
            Broker1.Subscribe(EventTypes.TrainingBooked, AnalyticsService);
            Console.WriteLine("[PubSub] AnalyticsService subscribed to TrainingBooked. Publisher unchanged.");
        }

        public static void SimulateScheduleServiceFailure()
        {
            EnsureInitialized();
            ScheduleService.IsAvailable = false;
            Console.WriteLine("[PubSub] ScheduleService -> UNAVAILABLE");
        }

        public static void RecoverScheduleService()
        {
            EnsureInitialized();
            ScheduleService.IsAvailable = true;
            Console.WriteLine("[PubSub] ScheduleService -> AVAILABLE");
        }

        public static string GetStatus()
        {
            EnsureInitialized();
            return $"Broker1={(Broker1.IsAvailable ? "AVAILABLE" : "UNAVAILABLE")}, " +
                   $"Broker2={(Broker2.IsAvailable ? "AVAILABLE" : "UNAVAILABLE")}, " +
                   $"Logger={(Logger.IsAvailable ? "AVAILABLE" : "UNAVAILABLE")}, " +
                   $"ScheduleService={(ScheduleService.IsAvailable ? "AVAILABLE" : "UNAVAILABLE")}, " +
                   $"NotificationService={(NotificationService.IsAvailable ? "AVAILABLE" : "UNAVAILABLE")}, " +
                   $"AnalyticsService={(AnalyticsService.IsAvailable ? "AVAILABLE" : "UNAVAILABLE")}";
        }
    }
}