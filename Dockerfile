FROM microsoft/dotnet:2.2-sdk AS installer-env

COPY . /src/dotnet-function-app
RUN cd /src/dotnet-function-app && \
    mkdir -p /home/site/wwwroot && \
    dotnet publish *.csproj --output /home/site/wwwroot

FROM mcr.microsoft.com/azure-functions/dotnet:2.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
	input-hub-name="collector-to-proxy" \
	FUNCTIONS_WORKER_RUNTIME=dotnet

COPY --from=installer-env ["/home/site/wwwroot", "/home/site/wwwroot"]

ADD ssl/splunk_cacert.crt /usr/local/share/ca-certificates/splunk_cacert.crt
RUN chmod 644 /usr/local/share/ca-certificates/splunk_cacert.crt && update-ca-certificates
