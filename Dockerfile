FROM microsoft/dotnet:2.2-sdk AS installer-env

COPY . /src/dotnet-function-app
RUN cd /src/dotnet-function-app && \
    mkdir -p /home/site/wwwroot && \
    dotnet publish *.csproj --output /home/site/wwwroot

FROM mcr.microsoft.com/azure-functions/dotnet:2.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
	AzureWebJobsStorage="DefaultEndpointsProtocol=https;AccountName=gregfunctionproxy;AccountKey=8L3VtWk8XCFhVISM31T/Ys0yAxMHjrItf8QJobeM7TuzGSm/OGtIPvaMhr9QWKZMfVpu7Mq6Ko9V1cu5TIvJeA==" \
	FUNCTIONS_WORKER_RUNTIME=dotnet \
	input-hub-name="output-to-proxy" \
	hubConnection="Endpoint=sb://asdt-benchmark-eventhub-fflob8w52a.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SXLfRmtSH+I+HVg3+uHwJQXefvDRp/hfoxb5sFxv8MU=" \
	splunkAddress="https://51.145.49.233:8088/services/collector" \
	splunkToken="c5f2415d-ca48-44d6-9f28-40c4cbe91bb5" \
	splunkCertThumbprint="39e41d46954c315f2fc56f7e62229a7e7422ab44"

COPY --from=installer-env ["/home/site/wwwroot", "/home/site/wwwroot"]

ADD ssl/splunk_cacert.crt /usr/local/share/ca-certificates/splunk_cacert.crt
RUN chmod 644 /usr/local/share/ca-certificates/splunk_cacert.crt && update-ca-certificates
