FROM microsoft/dotnet:2.2-sdk AS installer-env

COPY ./functionProxyDotnet /src/dotnet-function-app
RUN cd /src/dotnet-function-app && \
    mkdir -p /home/site/wwwroot && \
    dotnet publish *.csproj --output /home/site/wwwroot

FROM mcr.microsoft.com/azure-functions/dotnet:2.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
	input-hub-name="collector-to-proxy" \
	FUNCTIONS_WORKER_RUNTIME=dotnet \
    VERSION=sebastus/splunkproxyfunction:v1.0.8
	
COPY --from=installer-env ["/home/site", "/home/site"]

COPY functionProxyDotnet/ssl/splunk_cacert.pfx /home/site/ca-certificates/splunk_cacert.pfx

RUN echo 'alias ll="ls -la"' >> ~/.bashrc
