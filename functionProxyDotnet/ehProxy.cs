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
        static string splunkAddress { get; set; }
        static string splunkCertThumbprint { get; set; }
        static ILogger myLogger { get; set; }

        [FunctionName("ehProxy")]
        public static async Task Run(
            [EventHubTrigger("%input-hub-name%", Connection = "hubConnection")] string messages,
            ILogger log)
        {
            myLogger = log;

            splunkAddress = getEnvironmentVariable("splunkAddress");
            splunkCertThumbprint = getEnvironmentVariable("splunkCertThumbprint");
            if (splunkAddress.ToLower().StartsWith("https"))
            {
                log.LogInformation($"SSL encryption required.");

                try
                {
                    if (!DetectCACertificate())
                    {
                        log.LogInformation($"No CA cert detected.");
                        InstallCACertificate();
                        log.LogInformation($"CA Cert was installed.");
                    }
                } catch (Exception ex)
                {
                    log.LogError($"Error during certificate operations: {ex.Message}");
                    throw ex;
                }
            }

            await obHEC(messages, log);
        }

        static void InstallCACertificate()
        {
            var password = getEnvironmentVariable("CA_PRIVATE_KEY_PASSWORD");
            var cert = new X509Certificate2(
                getCertFilename("splunk_cacert.pfx"),
                password,
                X509KeyStorageFlags.DefaultKeySet);

            var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(cert);
            store.Close();
        }

        static bool DetectCACertificate()
        {
            var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, splunkCertThumbprint, false);
            store.Close();
            if (certificates.Count == 0)
            {
                return false;
            }
            return true;
        }

        public class SingleHttpClientInstance
        {
            private static readonly HttpClient HttpClient;

            static SingleHttpClientInstance()
            {
                //var handler = new SocketsHttpHandler
                //{
                //    SslOptions = new SslClientAuthenticationOptions
                //    {
                //        RemoteCertificateValidationCallback = ValidateMyCert,
                //        EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12                        
                //    }
                //};
                var handler = new SocketsHttpHandler
                {
                    SslOptions = new SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = ValidateMyCert
                    }
                };
                HttpClient = new HttpClient(handler);
//                HttpClient = new HttpClient();
            }

            static bool ValidateMyCert(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslErr)
            {
                if (sslErr == SslPolicyErrors.None) { 
                    return true;
                }

                if (sslErr.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
                {
                    myLogger.LogError($"There are errors in the remote certificate chain.\n");
                }
                if (sslErr.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
                {
                    myLogger.LogError($"The remote certificate name doesn't match.\n");
                }
                if (sslErr.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
                {
                    myLogger.LogError($"The remote certificate is not available.\n");
                }

                return false;
            }

            public static async Task<HttpResponseMessage> SendToService(HttpRequestMessage req)
            {
                HttpResponseMessage response = await HttpClient.SendAsync(req);
                return response;
            }
        }

        public static string getCertFilename(string basename)
        {

            var filename = "";
            var home = getEnvironmentVariable("HOME");
            if (home.Length == 0)
            {
                filename = "../../../ssl/" + basename;
            }
            else
            {
                filename = home + "/site/ca-certificates/" + basename;
            }
            return filename;
        }
        
        // public static bool ValidateMyCert(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslErr)
        // {
        //     // if user has not configured a cert, anything goes
        //     if (string.IsNullOrWhiteSpace(splunkCertThumbprint))
        //         return true;

        //     // if user has configured a cert, must match
        //     var numcerts = chain.ChainElements.Count;
        //     var cacert = chain.ChainElements[numcerts - 1].Certificate;

        //     var thumbprint = cacert.GetCertHashString().ToLower();
        //     if (thumbprint == splunkCertThumbprint)
        //         return true;

        //     return false;
        // }

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
                var msg = "Error sending to Splunk. \n";
                msg += e.Message + "\n";
                if (e.InnerException != null)
                {
                    msg += e.InnerException.Message;

                    if (e.InnerException.InnerException != null)
                    {
                        msg += e.InnerException.InnerException.Message;
                    }
                }
                throw new System.Net.Http.HttpRequestException(msg);
            }
            catch (Exception f)
            {
                throw new System.Exception($"Sending to Splunk. Unplanned exception. {f.Message}");
            }
        }
    }
}