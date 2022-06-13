using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AzureServiceBusUtils
{
    /// <summary>
    /// Using Service Bus Queues (with Message Duplication detection enabled) we can use the Queue as a simple scheduler.
    /// In a multinode cluster all nodes can define the same scheduled jobs, they will all enqueue the execution of jobs as a scheduled message 
    /// which will only be delivered when it's time for the job to run,
    /// and only one node will get the message and trigger the job.
    /// </summary>
    public class AzureServiceBusScheduler
    {
        private readonly MessageSender _schedulerQueueSender;
        private readonly MessageReceiver _schedulerQueueReceiver;
        private readonly QueueClient _schedulerQueueClient;
        private readonly Timer _scheduleNextExecutionsTimer;

        private readonly List<JobDefinition> _jobDefinitions = new List<JobDefinition>(); // In the future we could persist the JobDefinitions in some storage. Currently in-memory.
        private readonly Dictionary<string, Action> _jobHandlers = new Dictionary<string, Action>();

        public AzureServiceBusScheduler(string connectionString, string controlChannelQueueName)
        {
            _schedulerQueueSender = new MessageSender(connectionString, controlChannelQueueName);
            _schedulerQueueReceiver = new MessageReceiver(connectionString, controlChannelQueueName);
            _schedulerQueueClient = new QueueClient(connectionString, controlChannelQueueName);
            _scheduleNextExecutionsTimer = new Timer(ScheduleNextJobs, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        }

        public void Start() => Task.Run(RunAsync);

        private class JobDefinition
        {
            public string JobId;
            //public string CronExpression; //TODO: Cronos lib
            public Func<DateTime> CronExpression;
            public DateTime? LastScheduledExecution; // Last execution time (UTC) that THIS instance scheduled for this job
        }

        public async Task ScheduleRecurrentJobAsync(string jobId, /*string cronExpression, */ Func<DateTime> cronExpression, Action action)
        {
            _jobHandlers[jobId] = action;
            var jobDefinition = new JobDefinition() { JobId = jobId, CronExpression = cronExpression };
            _jobDefinitions.Add(jobDefinition);
            await ScheduleNextExecutionAsync(jobDefinition);
        }

        private async Task ScheduleNextExecutionAsync(JobDefinition jobDefinition)
        {
            //var cron = Cronos.CronExpression.Parse(jobDefinition.CronExpression);
            //var nextExecution = cron.GetNextOccurrence(DateTime.UtcNow);
            DateTime? nextExecution = jobDefinition.CronExpression();

            // To be on the safe side we require at least 1 minute ahead of time for scheduling next execution
            // So some worker can pick the task, without the risk of other scheduler enqueuing a duplicate execution
            if (nextExecution != null && nextExecution != jobDefinition.LastScheduledExecution && nextExecution.Value.Subtract(DateTime.UtcNow).TotalSeconds >= 60)
            {
                var msg = new Message()
                {
                    Label = "JobExecution",
                    MessageId = $"{jobDefinition.JobId}-{nextExecution.Value.ToString("yyyy-MM-dd_HH:mm:ss")}",
                    ScheduledEnqueueTimeUtc = nextExecution.Value
                };
                msg.UserProperties.Add("JobId", jobDefinition.JobId);

                await _schedulerQueueSender.SendAsync(msg);
                jobDefinition.LastScheduledExecution = nextExecution.Value;
            }
        }
        private void ScheduleNextJobs(object state)
        {
            foreach (var jobDefinition in _jobDefinitions)
            {
                try
                {
                    _ = ScheduleNextExecutionAsync(jobDefinition);
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
                    _jobHandlers[(string)message.UserProperties["JobId"]].Invoke();
                }
                try
                {
                    await _schedulerQueueReceiver.CompleteAsync(message.SystemProperties.LockToken);
                }
                catch { /*ignore*/ }
            }
        }


    }
}
