using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace dotnetProxyFunctionApp
{
    public static class ehProxy
    {
        static string splunkCertThumbprint { get; set; }

        [FunctionName("ehProxy")]
        public static async Task Run(
            [EventHubTrigger("%input-hub-name%", Connection = "hubConnection")] string messages,
            ILogger log)
        {
            splunkCertThumbprint = getEnvironmentVariable("splunkCertThumbprint");

            await obHEC(messages, log);
        }

        public class SingleHttpClientInstance
        {
            private static readonly HttpClient HttpClient;

            static SingleHttpClientInstance()
            {
                var handler = new SocketsHttpHandler
                {
                    SslOptions = new SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = ValidateMyCert,
                        EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12                        
                    }
                };

                HttpClient = new HttpClient(handler);
            }

            public static async Task<HttpResponseMessage> SendToService(HttpRequestMessage req)
            {
                HttpResponseMessage response = await HttpClient.SendAsync(req);
                return response;
            }
        }

        public static string getFilename(string basename)
        {

            var filename = "";
            var home = getEnvironmentVariable("HOME");
            if (home.Length == 0)
            {
                filename = "../../../" + basename;
            }
            else
            {
                filename = home + "\\site\\wwwroot\\" + basename;
            }
            return filename;
        }
        
        public static bool ValidateMyCert(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslErr)
        {
            // if user has not configured a cert, anything goes
            if (string.IsNullOrWhiteSpace(splunkCertThumbprint))
                return true;

            // if user has configured a cert, must match
            var numcerts = chain.ChainElements.Count;
            var cacert = chain.ChainElements[numcerts - 1].Certificate;

            var thumbprint = cacert.GetCertHashString().ToLower();
            if (thumbprint == splunkCertThumbprint)
                return true;

            return false;
        }

        public static string getEnvironmentVariable(string name)
        {
            var result = System.Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            if (result == null)
                return "";

            return result;
        }


        public static async Task obHEC(string standardizedEvents, ILogger log)
        {
            string splunkAddress = getEnvironmentVariable("splunkAddress");
            string splunkToken = getEnvironmentVariable("splunkToken");
            if (splunkAddress.Length == 0 || splunkToken.Length == 0)
            {
                log.LogError("Values for splunkAddress and splunkToken are required.");
                throw new ArgumentException();
            }

            var client = new SingleHttpClientInstance();

            try
            {
                var httpRequestMessage = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri(splunkAddress),
                    Headers = {
                            { HttpRequestHeader.Authorization.ToString(), "Splunk " + splunkToken }
                        },
                    Content = new StringContent(standardizedEvents, Encoding.UTF8, "application/json")
                };

                HttpResponseMessage response = await SingleHttpClientInstance.SendToService(httpRequestMessage);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new System.Net.Http.HttpRequestException($"StatusCode from Splunk: {response.StatusCode}, and reason: {response.ReasonPhrase}");
                }
            }
            catch (System.Net.Http.HttpRequestException e)
            {
                throw new System.Net.Http.HttpRequestException("Sending to Splunk. Is Splunk service running?", e);
            }
            catch (Exception f)
            {
                throw new System.Exception("Sending to Splunk. Unplanned exception.", f);
            }
        }
    }
}