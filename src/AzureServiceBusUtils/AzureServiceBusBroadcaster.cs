using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Management;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AzureServiceBusUtils
{
    /// <summary>
    /// Allows multiple nodes to communicate with each other through broadcast messages using a Service Bus Topic (each node will create it's own Subscription).
    /// Through broadcast messages (and callbacks registered for each possible message) the nodes can communicate and orchestrate their actions. Some examples:
    /// - In a Blue/Green deployment when nodes from the new deployment start up they can notify all other nodes so that old nodes can verify that they should become inactive.
    /// - When we scale up or scale down (add new nodes or remove nodes) all active nodes can recalculate some partitioning function to distribute their loads
    /// - Periodially nodes can send a heartbeat so that other nodes can trigger an alert if we run short of active nodes
    /// </summary>
    public class AzureServiceBusBroadcaster
    {
        private readonly string _connectionString = null;
        private readonly string _topicPath;
        private readonly TopicClient _topicClient;
        private string _subscriptionName = null;
        private SubscriptionClient _subscriptionClient;
        private ManagementClient _managementClient;

        private readonly Dictionary<string, Action> _jobHandlers = new Dictionary<string, Action>();

        public AzureServiceBusBroadcaster(string connectionString, string topicPath)
        {
            _connectionString = connectionString;
            _topicPath = topicPath;
            _topicClient = new TopicClient(connectionString, topicPath);
        }

        public void Start() => Task.Run(RunAsync);

        public void RegisterCallback(string eventId, Action action)
        {
            _jobHandlers[eventId] = action;
        }

        public void SendBroadcast(string eventId)
        {
            var msg = new Message()
            {
                MessageId = $"{eventId}-{DateTime.Now.ToString("yyyy-MM-dd/HHmmss")}",
            };
            msg.UserProperties.Add("EventId", eventId);
            _topicClient.SendAsync(msg);
        }

        private async Task RunAsync()
        {
            _managementClient = new ManagementClient(_connectionString);

            _subscriptionName = Guid.NewGuid().ToString();
            var subscriptionDef = new SubscriptionDescription(_topicPath, Guid.NewGuid().ToString())
            {
                AutoDeleteOnIdle = TimeSpan.FromHours(24)
            };
            subscriptionDef = await _managementClient.CreateSubscriptionAsync(subscriptionDef);
            _subscriptionClient = new SubscriptionClient(_connectionString, _topicPath, _subscriptionName);
            _subscriptionClient.RegisterMessageHandler(HandleAsync, new MessageHandlerOptions(OnExceptionAsync) { AutoComplete = true, MaxConcurrentCalls = 1 });
        }
        private async Task HandleAsync(Message message, CancellationToken cancellation)
        {
            if (message.UserProperties.ContainsKey("EventId") && _jobHandlers.ContainsKey((string)message.UserProperties["EventId"]))
            {
                _jobHandlers[(string)message.UserProperties["EventId"]].Invoke();
            }
            try
            {
                await _subscriptionClient.CompleteAsync(message.SystemProperties.LockToken);
            }
            catch { /*ignore*/ }
        }
        private Task OnExceptionAsync(ExceptionReceivedEventArgs args) { return Task.CompletedTask; }


    }
}
