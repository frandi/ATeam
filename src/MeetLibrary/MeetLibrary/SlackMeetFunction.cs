using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Text;
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

            var data = await new Microsoft.AspNetCore.WebUtilities.FormReader(req.Body).ReadFormAsync();

            log.LogInformation($"Request Body: {JsonConvert.SerializeObject(data)}");

            string baseUrl = Environment.GetEnvironmentVariable("BaseMeetUrl");
            if (string.IsNullOrEmpty(baseUrl))
                baseUrl = "https://meet.google.com";

            data.TryGetValue("team_domain", out var teamDomain);
            data.TryGetValue("channel_id", out var channelId);
            data.TryGetValue("channel_name", out var channelName);
            data.TryGetValue("user_name", out var userName);
            data.TryGetValue("command", out var command);
            data.TryGetValue("text", out var text);
            data.TryGetValue("response_url", out var responseUrl);

            var meetItem = ParseCommandText(text);

            if (meetItem.IsHelpOperation) // helper
            {
                var helpText = GetHelpText();

                return BlockMessageResult(channelId, helpText);
            }

            bool isDirectMessage = channelName.ToString().Equals("directmessage", StringComparison.InvariantCultureIgnoreCase);
            bool isPrivateChannel = channelName.ToString().Equals("privategroup", StringComparison.InvariantCultureIgnoreCase);
            if (string.IsNullOrEmpty(meetItem.Alias) && !isDirectMessage && !isPrivateChannel)
                meetItem.Alias = channelName;

            if (string.IsNullOrEmpty(meetItem.Alias))
                return BlockMessageResult(channelId, "*Failed*. You need to include `room-alias` as parameter in the command.");

            // get item based on the alias
            Uri collectionUri = UriFactory.CreateDocumentCollectionUri("MeetLibrary", "Items");
            var query = client.CreateDocumentQuery<MeetLibraryItem>(collectionUri)
                .Where(p => p.id == meetItem.Alias && p.partitionKey == teamDomain.ToString())
                .AsDocumentQuery();
            var items = await query.ExecuteNextAsync<MeetLibraryItem>();
            var meetLibraryItem = items.FirstOrDefault();

            if (meetItem.IsSetOperation) // setter
            {
                if (string.IsNullOrEmpty(meetItem.Code))
                    return BlockMessageResult(channelId, "*Failed*. You need to include `room-code` as parameter in the command.");

                if (meetLibraryItem != null) // exists
                {
                    if (meetLibraryItem.Code.Equals(meetItem.Code, StringComparison.InvariantCultureIgnoreCase))
                        return BlockMessageResult(channelId, $"Well, `{meetItem.Code}` has been configured as the meeting room for `{meetItem.Alias}` before. But, thanks for your effort!");

                    if (!meetItem.IsForceUpdate)
                        return BlockMessageResult(channelId, $"Oops, the meeting room for `{meetItem.Alias}` has been configured before. If you want to update it, please include `force` parameter in the end of the command.");

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

                return BlockMessageResult(channelId, true, $"@{userName} has updated the meeting room for `{meetItem.Alias}` to {baseUrl}/{meetItem.Code}.");
            } 
            else // getter
            {
                if (meetLibraryItem == null)
                    return BlockMessageResult(channelId, $"Sorry, no meeting room was configured for `{meetItem.Alias}` yet.");

                return BlockMessageResult(channelId, true, $"Meeting room for `{meetItem.Alias}` is {baseUrl}/{meetLibraryItem.Code}");
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
                        else if (partsWithoutForce[i].Equals("help", StringComparison.InvariantCultureIgnoreCase))
                            item.IsHelpOperation = true;
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

        private static string GetHelpText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("*Description*");
            sb.AppendLine("`/meet` is a simple command to get or set a meeting room which associates to certain channel or an alias.");
            sb.AppendLine("");
            sb.AppendLine("*GET Usage*");
            sb.AppendLine("  - `/meet`              : display meeting room for current *public* channel");
            sb.AppendLine("  - `/meet [room-alias]` : display meeting room by the alias. The alias could be the channel name.");
            sb.AppendLine("");
            sb.AppendLine("*SET Usage*");
            sb.AppendLine("  - `/meet set [room-code]`  : set meeting room for current *public* channel");
            sb.AppendLine("  - `/meet set [room-code] [room-alias]` : set meeting room with an alias. The alias could be the channel name.");
            sb.AppendLine("");
            sb.AppendLine("*Note*: If the meeting room for the channel/alias exist, you need to add `force` parameter in the end of the command to update the value.");
            sb.AppendLine("");
            sb.AppendLine("*HELP Usage*");
            sb.AppendLine("  - `/meet help`         : display help");

            return sb.ToString();

        }

        private static JsonResult BlockMessageResult(string channelId, bool inChannel, string markdownText, params string[] additionalMdTexts)
        {
            var payload = new MessagePayload(channelId, inChannel);
            payload.AddSection(markdownText);

            foreach (var mdText in additionalMdTexts)
            {
                payload.AddSection(mdText);
            }

            return new JsonResult(payload);
        }

        private static JsonResult BlockMessageResult(string channelId, string markdownText, params string[] additionalMdTexts)
        {
            return BlockMessageResult(channelId, false, markdownText, additionalMdTexts);
        }
    }
}
