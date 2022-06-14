using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Management;
using Newtonsoft.Json;
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
    public class AzureServiceBusBroadcaster<TInstanceInfo>
    {
        private readonly string _connectionString = null;
        private readonly string _topicPath;
        private readonly TInstanceInfo _instanceInfo;
        private readonly TopicClient _topicClient;
        private readonly ManagementClient _managementClient;
        private readonly Dictionary<string, Action<TInstanceInfo, Message>> _jobHandlers = new Dictionary<string, Action<TInstanceInfo, Message>>();
        private string _subscriptionName = null;
        private SubscriptionClient _subscriptionClient;

        /// <inheritdoc />
        public AzureServiceBusBroadcaster(string connectionString, string topicPath, TInstanceInfo instanceInfo)
        {
            _instanceInfo = instanceInfo;
            _connectionString = connectionString;
            _topicPath = topicPath;
            _topicClient = new TopicClient(connectionString, topicPath);
            _managementClient = new ManagementClient(_connectionString);
        }

        /// <summary>
        /// For a given EventId this registers a callback function
        /// </summary>
        /// <param name="eventId"></param>
        /// <param name="action"></param>
        public void RegisterCallback(string eventId, Action<TInstanceInfo, Message> action)
        {
            _jobHandlers[eventId] = action;
        }

        /// <summary>
        /// Sends a broadcast message to all registered instances which may handle the event if they have a registered callback
        /// </summary>
        /// <param name="eventId"></param>
        public void SendBroadcast(string eventId)
        {
            var msg = new Message()
            {
                MessageId = $"{eventId}-{DateTime.Now.ToString("yyyy-MM-dd/HHmmss")}",
            };
            msg.UserProperties.Add("EventId", eventId);
            msg.UserProperties.Add("InstanceInfo", JsonConvert.SerializeObject(_instanceInfo));
            _topicClient.SendAsync(msg);
        }

        /// <summary>
        /// Starts a background task that will listen to the broadcast messages topic and will handle those events by invoking the registered callbacks
        /// </summary>
        public async Task StartAsync()
        {
            await CreateNewSubscriptionAsync(); // RegisterMessageHandler will automatically start a new thread to receive/handle the messages
        }

        private async Task CreateNewSubscriptionAsync()
        {
            if (_subscriptionClient != null)
            {
                await _subscriptionClient.UnregisterMessageHandlerAsync(TimeSpan.FromSeconds(5));
                _subscriptionClient = null;
            }

            _subscriptionName = Guid.NewGuid().ToString();
            var subscriptionDef = new SubscriptionDescription(_topicPath, _subscriptionName) { AutoDeleteOnIdle = TimeSpan.FromMinutes(5) };
            subscriptionDef = await _managementClient.CreateSubscriptionAsync(subscriptionDef);
            _subscriptionClient = new SubscriptionClient(_connectionString, _topicPath, _subscriptionName);

            _subscriptionClient.RegisterMessageHandler(HandleAsync, new MessageHandlerOptions(OnExceptionAsync) { AutoComplete = true, MaxConcurrentCalls = 1 });
        }

        private async Task HandleAsync(Message message, CancellationToken cancellation)
        {
            //System.Diagnostics.Debug.WriteLine($"Received message: SequenceNumber:{message.SystemProperties.SequenceNumber} Body:{Encoding.UTF8.GetString(message.Body)}");

            if (message.UserProperties.ContainsKey("EventId") && _jobHandlers.ContainsKey((string)message.UserProperties["EventId"]))
            {
                try
                {
                    // Complete message (so it's not sent again to this or other instance) before starting the callback,
                    // because by default (unless renewed) locks will expire shortly.
                    await _subscriptionClient.CompleteAsync(message.SystemProperties.LockToken);
                }
                catch (MessageLockLostException)
                {
                    return; // if the lock is not valid maybe this job was reassigned to and handled by other instance.
                }
                catch (Exception ex) when (ex.GetBaseException() is MessageLockLostException)
                {
                    return; // if the lock is not valid maybe this job was reassigned to and handled by other instance.
                }

                _jobHandlers[(string)message.UserProperties["EventId"]].Invoke(_instanceInfo, message);
            }
        }
        private Task OnExceptionAsync(ExceptionReceivedEventArgs args)
        {
            System.Diagnostics.Debug.WriteLine("Exception = " + args.Exception.Message);
            return Task.CompletedTask;
        }


    }

    /// <inheritdoc />
    public class AzureServiceBusBroadcaster : AzureServiceBusBroadcaster<string>
    {
        /// <inheritdoc />
        public AzureServiceBusBroadcaster(string connectionString, string topicPath, string instanceId = null)
            : base(connectionString, topicPath, instanceId ?? Guid.NewGuid().ToString())
        {
        }

    }
}
