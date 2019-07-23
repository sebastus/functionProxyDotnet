# .NET Proxy: Azure Function for Splunk --> Splunk HEC

The generalized architecture for Splunk includes a cluster of indexers. If using the HEC, the indexers can follow a load balancer. When monitoring a large Azure environment, it's likely that many subscriptions will be monitored by a central Splunk cluster. The proxy function allows the indexer LB to be hidden in a private network. Since the proxy is configured to be triggered by an event hub (and has no other endpoints), it's a hard target.

![Architecture](images/Splunk-with-Proxy-Function.png)

