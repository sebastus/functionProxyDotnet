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
                log.LogTrace($"SSL encryption required.");

                try
                {
                    if (!DetectCACertificate())
                    {
                        log.LogTrace($"No CA cert detected.");
                        InstallCACertificate();
                        log.LogTrace($"CA Cert was installed.");
                    } else
                    {
                        log.LogTrace("Certificate is already installed.");
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
            var caCertFilename = getEnvironmentVariable("CA_CERT_FILENAME", "splunk_cacert.pfx");
            var password = getEnvironmentVariable("CA_PRIVATE_KEY_PASSWORD", "password");
            var cert = new X509Certificate2(
                getCertFilename(caCertFilename),
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
                var handler = new SocketsHttpHandler
                {
                    SslOptions = new SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = ValidateMyCert
                    }
                };
                HttpClient = new HttpClient(handler);
            }

            static bool ValidateMyCert(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslErr)
            {
                bool returnValue = true;

                if (sslErr.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
                {
                    myLogger.LogError($"There are errors in the remote certificate chain.");
                    returnValue = false;
                }
                if (sslErr.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
                {
                    myLogger.LogError($"The remote certificate is not available.");
                    returnValue = false;
                }

                var caCertCommonName = getEnvironmentVariable("CA_CERT_COMMONNAME", "SplunkServerDefaultCert");
                var subjectName = chain.ChainElements[0].Certificate.SubjectName.Name;
                //subjectName = "O=SplunkUser, CN=SplunkServerDefaultCert"

                string[] subjectNameParts = subjectName.Split(',');
                string cn = "";
                foreach (var split in subjectNameParts)
                {
                    if (split.Trim().StartsWith("CN"))
                    {
                        cn = split.Split("=")[1];
                    }
                }

                myLogger.LogTrace($"commonName = {cn}");
                 
                if (cn != caCertCommonName)
                {
                    returnValue = false;
                }

                return returnValue;
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

        public static string getEnvironmentVariable(string name, string defaultValue = "")
        {
            var result = System.Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            if (result == null)
                return defaultValue;

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