using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AzureServiceBusUtils
{
    /// <summary>
    /// Allows multiple nodes to be all scheduling recurring jobs and relying on Service Bus Queues to trigger the job at the right time and to enforce that only one of the nodes will be running the job.
    /// The Queue must have Message Duplication detection enabled.
    /// </summary>
    public class AzureServiceBusScheduler<TInstanceInfo>
    {
        private readonly TInstanceInfo _instanceInfo;
        private readonly MessageSender _schedulerQueueSender;
        private readonly MessageReceiver _schedulerQueueReceiver;
        private readonly QueueClient _schedulerQueueClient;
        private readonly Timer _scheduleNextExecutionsTimer;

        private readonly List<JobDefinition> _jobDefinitions = new List<JobDefinition>(); // In the future we could persist the JobDefinitions in some storage. Currently in-memory.
        private readonly Dictionary<string, Action<TInstanceInfo, Message>> _jobHandlers = new Dictionary<string, Action<TInstanceInfo, Message>>();

        /// <inheritdoc />
        public AzureServiceBusScheduler(string connectionString, string controlChannelQueueName, TInstanceInfo instanceInfo)
        {
            _instanceInfo = instanceInfo;
            _schedulerQueueSender = new MessageSender(connectionString, controlChannelQueueName);
            _schedulerQueueReceiver = new MessageReceiver(connectionString, controlChannelQueueName);
            _schedulerQueueClient = new QueueClient(connectionString, controlChannelQueueName);
            _scheduleNextExecutionsTimer = new Timer(ScheduleNextJobs, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// Starts a background task that will listen to the scheduled-jobs queue and will handle those jobs (as they are released by the queue) by invoking the registered callbacks
        /// </summary>
        public void Start() => Task.Run(RunAsync);

        private class JobDefinition
        {
            public string JobId;
            public Func<DateTime, DateTime?> GetNextUtcExecution;
            public DateTime? LastScheduledExecution; // Last execution time (UTC) that THIS instance scheduled for this job
        }

        /// <summary>
        /// For a given JobId this registers a callback function, and defines a schedule Func that will be used to calculate the next executions of the job.
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="getNextUtcExecution"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        public async Task ScheduleRecurrentJobAsync(string jobId, Func<DateTime, DateTime?> getNextUtcExecution, Action<TInstanceInfo, Message> action)
        {
            _jobHandlers[jobId] = action;
            var jobDefinition = new JobDefinition() { JobId = jobId, GetNextUtcExecution = getNextUtcExecution };
            _jobDefinitions.Add(jobDefinition);
            await ScheduleNextExecutionsAsync(jobDefinition, DateTime.UtcNow);
        }

        private async Task ScheduleNextExecutionsAsync(JobDefinition jobDefinition, DateTime utcNow)
        {
            DateTime? nextExecution;

            DateTime startFrom = jobDefinition.LastScheduledExecution ?? utcNow;

            // Schedule next occurrences for the next 30 min
            while ((nextExecution = jobDefinition.GetNextUtcExecution(startFrom)) != null && nextExecution.Value.Subtract(DateTime.UtcNow).TotalMinutes <= 30)
            {
                // To be on the safe side we require at least a few seconds ahead of time for scheduling next execution
                // So some worker can pick the task, without the risk of other scheduler enqueuing a duplicate execution
                if ((jobDefinition.LastScheduledExecution == null || jobDefinition.LastScheduledExecution <= nextExecution) && nextExecution.Value.Subtract(DateTime.UtcNow).TotalSeconds >= 15)
                {
                    var msg = new Message()
                    {
                        Label = "JobExecution",
                        MessageId = $"{jobDefinition.JobId}-{nextExecution.Value.ToString("yyyy-MM-dd/HHmmss")}",
                        ScheduledEnqueueTimeUtc = nextExecution.Value
                    };
                    msg.UserProperties.Add("JobId", jobDefinition.JobId);

                    await _schedulerQueueSender.SendAsync(msg);
                    jobDefinition.LastScheduledExecution = nextExecution.Value;
                }
                startFrom = nextExecution.Value;
            }
        }
        private void ScheduleNextJobs(object state)
        {
            foreach (var jobDefinition in _jobDefinitions)
            {
                try
                {
                    _ = ScheduleNextExecutionsAsync(jobDefinition, DateTime.UtcNow);
                }
                catch { /*ignore*/ }
            }
        }

        private async Task RunAsync()
        {
            while (true)
            {
                var message = await _schedulerQueueReceiver.ReceiveAsync(TimeSpan.FromSeconds(10));
                if (message == null)
                {
                    continue;
                }
                if (message.UserProperties.ContainsKey("JobId") && _jobHandlers.ContainsKey((string)message.UserProperties["JobId"]))
                {
                    _ = Task.Run(async () => await HandleAsync(message));
                }
            }
        }
        private async Task HandleAsync(Message message)
        {
            try
            {
                // Complete message (so it's not sent again to this or other instance) before starting the callback,
                // because by default (unless renewed) locks will expire shortly.
                await _schedulerQueueReceiver.CompleteAsync(message.SystemProperties.LockToken);
            }
            catch (MessageLockLostException)
            {
                return; // if the lock is not valid maybe this job was reassigned to and handled by other instance.
            }
            catch (Exception ex) when (ex.GetBaseException() is MessageLockLostException)
            {
                return; // if the lock is not valid maybe this job was reassigned to and handled by other instance.
            }

            _jobHandlers[(string)message.UserProperties["JobId"]].Invoke(_instanceInfo, message);
        }

    }

    /// <inheritdoc />
    public class AzureServiceBusScheduler : AzureServiceBusScheduler<string>
    {
        /// <inheritdoc />
        public AzureServiceBusScheduler(string connectionString, string controlChannelQueueName, string instanceId = null)
            : base(connectionString, controlChannelQueueName, instanceId ?? Guid.NewGuid().ToString())
        {

        }
    }
}