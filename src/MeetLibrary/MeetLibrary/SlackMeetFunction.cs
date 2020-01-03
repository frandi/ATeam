using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MeetLibrary
{
    public static class SlackMeetFunction
    {
        [FunctionName("SlackMeet")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            [CosmosDB(
                databaseName:"MeetLibrary",
                collectionName:"Items",
                ConnectionStringSetting = "CosmosDBConnection")] DocumentClient client,
            ILogger log)
        {
            log.LogInformation("Parsing Slack command.");

            string baseUrl = Environment.GetEnvironmentVariable("BaseMeetUrl");
            if (string.IsNullOrEmpty(baseUrl))
                baseUrl = "https://meet.google.com";

            string teamDomain = req.Query["team_domain"];
            string channelName = req.Query["channel_name"];
            string userName = req.Query["user_name"];
            string command = req.Query["command"];
            string text = req.Query["text"];
            string responseUrl = req.Query["response_url"];

            var meetItem = ParseCommandText(text);
            if (string.IsNullOrEmpty(meetItem.Alias))
                meetItem.Alias = channelName;

            if (string.IsNullOrEmpty(meetItem.Alias))
                return new BadRequestObjectResult("Alias parameter is required.");

            // get item based on the alias
            Uri collectionUri = UriFactory.CreateDocumentCollectionUri("MeetLibrary", "Items");
            var query = client.CreateDocumentQuery<MeetLibraryItem>(collectionUri)
                .Where(p => p.id == meetItem.Alias && p.partitionKey == teamDomain)
                .AsDocumentQuery();
            var items = await query.ExecuteNextAsync<MeetLibraryItem>();
            var meetLibraryItem = items.FirstOrDefault();

            if (meetItem.IsSetOperation) // setter
            {
                if (string.IsNullOrEmpty(meetItem.Code))
                    return new BadRequestObjectResult("Code parameter is required");

                if (meetLibraryItem != null) // exists
                {
                    if (!meetItem.IsForceUpdate)
                        return new BadRequestObjectResult($"The alias \"{meetItem.Alias}\" exists. Please include \"force\" parameter if you want to replace the code.");

                    meetLibraryItem.Code = meetItem.Code;

                    await client.UpsertDocumentAsync(collectionUri, meetLibraryItem);

                    log.LogInformation($"The code for \"{meetItem.Alias}\" has been updated to \"{meetItem.Code}\"");
                }
                else // new
                {
                    meetLibraryItem = new MeetLibraryItem
                    {
                        id = meetItem.Alias,
                        partitionKey = teamDomain,
                        Code = meetItem.Code
                    };

                    await client.CreateDocumentAsync(collectionUri, meetLibraryItem);

                    log.LogInformation($"A new code \"{meetItem.Code}\" has been added with alias \"{meetItem.Alias}\"");
                }

                return new OkObjectResult($"The code \"{meetItem.Code}\" has been saved for alias \"{meetItem.Alias}\"");
            } 
            else // getter
            {
                if (meetLibraryItem == null)
                    return new NotFoundObjectResult($"The alias \"{meetItem.Alias}\" was not found.");

                return new OkObjectResult($"{baseUrl}/{meetLibraryItem.Code}");
            }
        }

        private static MeetItem ParseCommandText(string text)
        {
            var item = new MeetItem();

            var parts = text.Split(" ").Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

            item.IsForceUpdate = parts.Any(p => p.Equals("force", StringComparison.InvariantCultureIgnoreCase));

            var partsWithoutForce = parts.Where(p => !p.Equals("force", StringComparison.InvariantCultureIgnoreCase)).ToArray();
            for (int i = 0; i < partsWithoutForce.Length; i++)
            {
                switch (i)
                {
                    case 0:
                        if (partsWithoutForce[i].Equals("set", StringComparison.InvariantCultureIgnoreCase))
                            item.IsSetOperation = true;
                        else
                            item.Alias = partsWithoutForce[i];
                        break;
                    case 1:
                        if (item.IsSetOperation)
                            item.Code = partsWithoutForce[i];
                        break;
                    case 2:
                        if (item.IsSetOperation)
                            item.Alias = partsWithoutForce[i];
                        break;
                    default:
                        break;
                }
            }

            return item;
        }
    }
}
