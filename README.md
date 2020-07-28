# Metrics: From .NET to Kubernetes (via Istio)

The objective of this exercise is to publish custom metrics from a .NET web application and create a dashboard with them. To achieve this objective, this tuturial uses the Istio service mesh to bootstrap a minimally viable monitoring system comprised of the Web Application itself, a metric scraper provided by Istio, Promnetheus (time series database) and Grafana (a visualization tool).

## Infrastructure

To start this example, the first step is to have the following infrastructure in place:
1. An Azure Kubernetes Service cluster to run the workloads.
2. An Azure Container Registry, to publish and deploy the containerized application.
3. The Istio Service Mesh installed in the cluster.

For the AKS Cluster:
```pwsh
$resourceGroup = 'MonitorGroup'
$clusterName = 'MonitorCluster'

# Login to Azure
az login

# Create resource group
az group create --name $resourceGroup --location westus

# Create a service principal
$principalObject = az ad sp create-for-rbac --skip-assignment | ConvertFrom-Json

# Create cluster
az aks create --resource-group $resourceGroup --name $clusterName --node-vm-size Standard_B2s --generate-ssh-keys --node-count 2 --service-principal $principalObject.appId --client-secret $principalObject.password

# Create local configuration file to talk to the AKS Cluster
az aks get-credentials --resource-group $resourceGroup --name $clusterName
```

For the Azure Container Registry
```pwsh
$acrName = 'MonitorContainerRegistry'

# Create Azure Container Registry
az acr create --name $acrName --resource-group $resourceGroup --location westus --sku Basic --admin-enabled true

# Store the ID of the recently created ACR into a variable.
$acrResourceId = az acr show --name $acrName --resource-group $resourceGroup --query id

# Attach ACR to AKS
az aks update --name $clusterName --resource-group $resourceGroup --attach-acr $acrResourceId
```

For the Istio Service Mesh
```pwsh
istioctl install --set profile=demo
```

## Application
The example application is an ASP.NET Core 3.1 application that simulates a Content Service. It allows CRUD operations over a very simplified definition of document stored in an in memory collection.

```csharp
[HttpGet]
[Route("articles")]
public IActionResult GetArticles()
{
    var results = (
        from element in Hwids
        select new { hwid = element.Key, type = element.Value }
    ).ToList();

    results.ForEach(document =>
    {
        _logger.LogInformation($"Retrieved {document.hwid}");
    });

    return Ok(results);
}

[HttpGet]
[Route("articles/{hwid}")]
public IActionResult GetArticle([NotNull] string hwid)
{
    if (!Hwids.ContainsKey(hwid))
    {
        _logger.LogWarning($"Missing {hwid}");
        return NotFound("Hwid is missing");
    }

    _logger.LogInformation($"Retrieved {hwid}");

    return Ok(new { hwid, type = Hwids[hwid] });
}

[HttpPost]
[Route("articles")]
public IActionResult CreateArticle([FromBody]DocumentPost post)
{
    var hwid = post.Hwid;

    if (Hwids.ContainsKey(hwid))
    {
        return BadRequest($"Already there! {hwid}");
    }

    Hwids.Add(hwid, DateTime.Now.Ticks % 2 == 0 ? "legacy" : "structured");

    _logger.LogWarning($"Created {hwid}");
    return new ObjectResult(hwid) { StatusCode = 201 };
}

[HttpDelete]
[Route("articles/{hwid}")]
public IActionResult DeleteArticle([NotNull] string hwid)
{
    if (!Hwids.ContainsKey(hwid))
    {
        _logger.LogWarning($"Missing {hwid}");
        return NotFound("Hwid is missing");
    }

    Hwids.Remove(hwid);

    _logger.LogInformation($"Deleted {hwid}");
    return Ok(hwid);
}
```

## Deployment of the application

### Containerize the App
The ASP.NET Core uses a very simple Dockerfile:
```docker
FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-env
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY *.csproj ./
RUN dotnet restore

# Copy everything else and build
COPY . ./
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/core/aspnet:3.1
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "AspnetApp.dll"]
```

### Build and publish the container in ACR
Then the next step is to build the project, create the image and publish it in ACR (all that is done with one command):
```pwsh
# From the server source code directory, build the image in ACR.
az acr build --resource-group $resourceGroup --registry $acrName --image aspnetapp:v1.0 .
```

### Create a Kubernetes Deployment
And a very simple resource definition in Kubernetes.
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: aspnetapp
  labels:
    app: aspnetapp
spec:
  replicas: 1
  selector:
    matchLabels:
      app: aspnetapp
  template:
    metadata:
      labels:
        app: aspnetapp
      annotations:
        prometheus.io/scrape: "true"
        prometheus.io/path: "/metrics"
        prometheus.io/port: "80"
    spec:
      containers:
        - name: aspnetapp
          image: monitorcontainerregistry.azurecr.io/aspnetapp:v2.0
          ports:
            - containerPort: 80
              name: http
```

Notice the annotation section in the metadata element of the Deployment. This labels will enable Istio to scrape the metrics from the pods.

To deploy the app in the cluster:
```pwsh
kubectl apply -f kubernetes_resources/01.aspnetapp-deployment.yaml -f kubernetes_resources/02.aspnetapp-service.yaml
```

## Adding metric support for ASP.NET

Installing prometheus-net package:
```pwsh
prometheus-net.AspNetCore
```

Add metrics endpoint through middleware (in `AspnetApp.Startup.cs`):
```csharp
using Prometheus;

...

public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
{
    if (env.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }

    app.UseHttpsRedirection();

    app.UseRouting();

    app.UseAuthorization();

    app.UseEndpoints(endpoints =>
    {
        endpoints.MapControllers();
        endpoints.MapMetrics();
    });
}
```

Add static metrics to be referenced by the controllers:
```csharp
public static class ContentControllerMetrics
{
    public static readonly Counter ContentRetrievals = Metrics.CreateCounter("aspnetapp_number_of_articles_retrieved_total", "Number of articles served");
    public static readonly Counter ContentByType = Metrics.CreateCounter("aspnetapp_number_of_articles_retrieved_total_articles_retrieved_by_type_total", "Trend of articles requested over the past 10 minutes", new CounterConfiguration { LabelNames = new[] { "type", "hwid" } });
    public static readonly Gauge ArticleCollectionSize = Metrics.CreateGauge("aspnetapp_number_of_articles_retrieved_total_articles_collection_size_total", "Number of articles available");
    public static readonly Histogram ArticleRetrievalTime = Metrics.CreateHistogram("aspnetapp_number_of_articles_retrieved_total_articles_retrieval_time_ms", "Article fetch time");
}
```

Use the metrics inside the controllers (or everywhere that is architecturally relevant):
```csharp
[HttpGet]
[Route("articles/{hwid}")]
public IActionResult GetArticle([NotNull] string hwid)
{
    if (!Hwids.ContainsKey(hwid))
    {
        _logger.LogWarning($"Missing {hwid}");
        return NotFound("Hwid is missing");
    }

    _logger.LogInformation($"Retrieved {hwid}");

    ContentControllerMetrics.ContentRetrievals.Inc();
    ContentControllerMetrics.ContentByType.WithLabels(Hwids[hwid], hwid).Inc();
    ContentControllerMetrics.ArticleRetrievalTime.Observe(new Random().NextDouble() * 100);

    return Ok(new { hwid, type = Hwids[hwid] });
}
```

## Port forward Prometheus, Grafana and the ASP.NET app to localhost
To port forward, execute the following scripts in pairs in separate terminals.

```pwsh
$prometheusPod = kubectl get pods -n istio-system | grep prometheus | awk '{print $1}'
kubectl port-forward $prometheusPod 9090:9090

$grafanaPod = kubectl get pods -n istio-system | grep grafana | awk '{print $1}'
kubectl port-forward $grafanaPod 3000:3000

$aspnetappPod = kubectl get pods  | grep aspnetapp | awk '{print $1}'
kubectl port-forward $aspnetappPod 8080:80
```

## Configuring Grafana
Go to `http://127.0.0.1:3000/`, and then to got to the side menu (+) -> Create Dashboard.

In the Query section use `aspnetapp_articles_retrieved_by_type_total{job="kubernetes-service-endpoints"}` in the Metrics field. Then type `[{{type}}] {{hwid}}` in legend.
Make sure in the Display section, you select Show -> Calculation and Calc -> Last (not null), and give a title to the Panel.

## References
* [Istio Installation](https://istio.io/latest/docs/setup/getting-started/)
* [Prometheus-Net](https://github.com/prometheus-net/prometheus-net)
* [Prometheus Naming Best Practices](https://prometheus.io/docs/practices/naming/)
* [Querying Prometheus](https://prometheus.io/docs/prometheus/latest/querying/basics/)
* [Querying Prometheus](https://prometheus.io/docs/prometheus/latest/querying/basics/)
* [Capture and Visualize Metrics with Prometheus and Grafana](https://docs.particular.net/samples/logging/prometheus-grafana/)
