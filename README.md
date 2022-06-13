# Azure Service Bus Utilities for high-scale systems

## AzureServiceBusScheduler

Allows multiple nodes to be all scheduling recurring jobs and relying on Service Bus Queues to trigger the job at the right time and to enforce that only one of the nodes will be running the job.

## AzureServiceBusBroadcaster

Allows multiple nodes to communicate with each other through broadcast messages using a Service Bus Topic. Nodes can register callbacks for the different possible messages that they receive from other nodes so that they can orchestrate their actions.

Usage Examples: 
- In a Blue/Green deployment when nodes from the new deployment start up they can notify all other nodes so that old nodes can verify that they should become inactive.
 - When we scale up or scale down (add new nodes or remove nodes) all active nodes can recalculate some partitioning function to distribute their loads
 - Periodially nodes can send some a heartbeat so that other nodes can trigger an alert if we run short of active nodes


# Examples

Scheduler:

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


Broadcaster:

```cs
var broadcaster = new AzureServiceBusBroadcaster(
        "Endpoint=sb://mybus.servicebus.windows.net/;SharedAccessKeyName=mykey;SharedAccessKey=mysecret",
        "mytopic");
			
// Trigger every 10 minutes. If now it's UTC 1:03am then next execution will be UTC 1:10am.
broadcaster.RegisterCallback("NewNodeIsUp", OnNewNodeUp);
broadcaster.Start();
broadcaster.SendBroadcast("NewNodeIsUp");

private void OnNewNodeUp()
{
  // do something
}
```




## License
MIT License
