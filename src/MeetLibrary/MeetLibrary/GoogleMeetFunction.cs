using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MeetLibrary
{
    public static class GoogleMeetFunction
    {
        private const string BaseUrl = "https://meet.google.com";

        [FunctionName("GoogleMeet")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            [CosmosDB(
                databaseName:"MeetLibrary", 
                collectionName:"Items", 
                ConnectionStringSetting = "CosmosDBConnection",
                Id = "{Query.alias}",
                PartitionKey = "{Query.group}")] MeetLibraryItem meetLibraryItem,
            [CosmosDB(
                databaseName:"MeetLibrary",
                collectionName:"Items",
                ConnectionStringSetting = "CosmosDBConnection")] IAsyncCollector<MeetLibraryItem> meetLibraryItems,
            ILogger log)
        {
            log.LogInformation("Processing Google Meet function request.");

            string alias = req.Query["alias"];
            string group = req.Query["group"];

            if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(group))
                return new BadRequestObjectResult("Both \"alias\" and \"group\" parameters are required.");

            log.LogInformation($"Parameter: alias = {alias}, group = {group}");

            if (HttpMethods.IsGet(req.Method))
            {
                if (meetLibraryItem == null)
                    return new NotFoundObjectResult("Google Meet item was not found. Please make sure you have included the correct values for alias and group.");

                return new OkObjectResult($"{BaseUrl}/{meetLibraryItem.Code}");
            } 
            else if (HttpMethods.IsPost(req.Method))
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);

                string code = data?.code;
                string force = data?.force;
                if (!bool.TryParse(force, out bool forceUpdate))
                    forceUpdate = false;

                if (string.IsNullOrEmpty(code))
                    return new BadRequestObjectResult("Meet code is required.");

                if (meetLibraryItem != null)
                {
                    if (!forceUpdate)
                        return new BadRequestObjectResult($"Google Meet item for \"{alias}\" already exist. Please include \"force=true\" parameter if you want to replace the code.");

                    meetLibraryItem.Code = code;

                    log.LogInformation($"Google Meet code for \"{alias}\" has been updated to \"{code}\"");
                } else
                {
                    await meetLibraryItems.AddAsync(new MeetLibraryItem
                    {
                        id = alias,
                        partitionKey = group,
                        Code = code
                    });

                    log.LogInformation($"A new Google Meet code \"{code}\" has been added with alias \"{alias}\"");
                }

                return new OkObjectResult($"Google Meet code \"{code}\" has been saved for alias \"{alias}\"");
            }
            
            return new BadRequestObjectResult("Unsupported method.");
        }
    }
}
