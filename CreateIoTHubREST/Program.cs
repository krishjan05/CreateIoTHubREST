using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.ResourceManager.Models;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;

namespace CreateIoTHubREST
{
    class Program
    {
        static string applicationId = "<application Id>";
        static string subscriptionId = "<Subscription Id>";
        static string tenantId = "<Tenant Id>";
        static string password = "<Password>";

        static string rgName = "<Name of resource group>";
        static string iotHubName = "<Name of IoT Hub>";

        static void Main(string[] args)
        {
            // 1. Retrieving token from Active Directory
            var authContext = new AuthenticationContext(string.Format("https://login.microsoftonline.com/{0}", tenantId));
            var credential = new ClientCredential(applicationId, password);
            AuthenticationResult token = authContext.AcquireTokenAsync("https://management.core.windows.net/", credential).Result;

            if(token == null)
            {
                Console.WriteLine("Failed to obtain token");
                return;
            }

            // 2. Creating a ResourceManagementClient
            var creds = new TokenCredentials(token.AccessToken);
            var client = new ResourceManagementClient(creds);
            client.SubscriptionId = subscriptionId;

            // 3. Create or obtain a reference to resource group
            var rgResponse = client.ResourceGroups.CreateOrUpdate(rgName, new ResourceGroup("East US"));

            if(rgResponse.Properties.ProvisioningState != "Succeeded")
            {
                Console.WriteLine("Problem creating resource group.");
                return;
            }

            CreateIoTHub(token.AccessToken);
            Console.ReadLine();
        }

        static void CreateIoTHub(string token)
        {
            // 1. Creating HttpClient using authentication token
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // 2. Create json object to hold IoT hub details
            var description = new
            {
                name = iotHubName,
                location = "East US",
                sku = new
                {
                    name = "S1",
                    tier = "Standard",
                    capacity = 1
                }
            };

            var json = JsonConvert.SerializeObject(description, Formatting.Indented);

            // 3. Submit a request to azure and get the url of newly created IoT hub
            var content = new StringContent(JsonConvert.SerializeObject(description), Encoding.UTF8, "application/json");
            var requestUri = string.Format("https://management.azure.com/subscriptions/{0}/resourcegroups/{1}/providers/Microsoft.devices/IotHubs/{2}?api-version=2016-02-03", subscriptionId, rgName, iotHubName);
            var result = client.PutAsync(requestUri, content).Result;

            if (!result.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed {0}", result.Content.ReadAsStringAsync().Result);
                return;
            }

            var asyncStatusUri = result.Headers.GetValues("Azure-AsyncOperation").First();

            // 4. Check status and Wait for deployment to complete
            string body;
            do
            {
                Thread.Sleep(10000);
                HttpResponseMessage deploymentstatus = client.GetAsync(asyncStatusUri).Result;
                body = deploymentstatus.Content.ReadAsStringAsync().Result;
            } while (body == "{\"status\":\"Running\"}");

            // 5. Retrieving the key of newly created IoT hub
            var listKeysUri = string.Format("https://management.azure.com/subscriptions/{0}/resourceGroups/{1}/providers/Microsoft.Devices/IotHubs/{2}/IoTHubKeys/listkeys?api-version=2016-02-03", subscriptionId, rgName, iotHubName);
            var keysresults = client.PostAsync(listKeysUri, null).Result;

            Console.WriteLine("Keys: {0}", keysresults.Content.ReadAsStringAsync().Result);

        }
    }
}
