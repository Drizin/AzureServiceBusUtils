# Azure Service Bus Utilities for high-scale systems

Some utility classes that rely on Azure Service Bus to provide misc functionality to high-scale/distributed systems. 

# Azure ServiceBus Scheduler

Allows multiple nodes to be all scheduling recurring jobs and relying on Service Bus Queues to trigger the job at the right time and to enforce that only one of the nodes will be running the job.

## Example

```cs
var scheduler = new AzureServiceBusScheduler(
        "Endpoint=sb://mybus.servicebus.windows.net/;SharedAccessKeyName=mykey;SharedAccessKey=mysecret",
        "myqueue");
			
// Trigger every 10 minutes. If now it's UTC 1:03am then next execution will be UTC 1:10am.
Func<DateTime> calcNextUtcExecution = () => {
        var nowUtc = DateTime.UtcNow;
        var nextUtc = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, nowUtc.Minute - (nowUtc.Minute%10), 0, DateTimeKind.Utc).AddMinutes(10);
        return nextUtc;
    };

await scheduler.ScheduleRecurrentJobAsync("MyJob", calcNextUtcExecution, MyJob);

private void MyJob()
{
  //...
}
```

## FAQ

### Is ServiceBus Scheduler like Hangfire?

[Hangfire](https://github.com/HangfireIO/Hangfire) is the most popular and comprehensive dotnet library for recurring jobs. 
It uses persistent storage (SQL Server or Redis) to store the jobs definitions, and it supports [multiple instances](https://docs.hangfire.io/en/latest/background-processing/running-multiple-server-instances.html) of a server so that the nodes will automatically coordinate (through distributed locks) in such a way that only one instance starts the job. 

However in microservices architecture it's frequent that the only resources used by microservices are stateless services and a Service Bus. Our ServiceBus Scheduler is a lightweight alternative for this scenario, where the recurrent jobs can be defined directly inside the service (in-memory), and a simple timer is responsible for scheduling the next execution. Azure Service Bus is used both to ignore duplicate schedules (since all nodes are identical and all will be scheduling the executions) and it's also used to deliver the message (the execution) at the scheduled time, to a random node.



# Azure ServiceBus Broadcaster

Allows multiple nodes to communicate with each other through broadcast messages using a Service Bus Topic. Nodes can register callbacks for the different possible messages that they receive from other nodes so that they can orchestrate their actions.

Usage Examples: 
- In a Blue/Green deployment when nodes from the new deployment start up they can notify all other nodes so that old nodes can verify that they should become inactive.
 - When we scale up or scale down (add new nodes or remove nodes) all active nodes can recalculate some partitioning function to distribute their loads
 - Periodially nodes can send a heartbeat so that other nodes can trigger an alert if we run short of active nodes


## Example

```cs
var broadcaster = new AzureServiceBusBroadcaster(
        "Endpoint=sb://mybus.servicebus.windows.net/;SharedAccessKeyName=mykey;SharedAccessKey=mysecret",
        "mytopic");
			
broadcaster.RegisterCallback("NewNodeIsUp", OnNewNodeUp);
broadcaster.Start();
broadcaster.SendBroadcast("NewNodeIsUp");

private void OnNewNodeUp()
{
  // do something
}
```


# License
MIT License
