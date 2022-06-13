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


## License
MIT License
