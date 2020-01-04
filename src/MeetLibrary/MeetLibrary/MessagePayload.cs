using System.Collections.Generic;
using Newtonsoft.Json;

namespace MeetLibrary
{
    public class MessagePayload
    {
        public MessagePayload(string channelId)
        {
            Channel = channelId;
        }

        public MessagePayload(string channelId, bool inChannel)
        {
            Channel = channelId;
            ResponseType = inChannel ? "in_channel" : "ephemeral";
        }

        [JsonProperty("channel")]
        public string Channel { get; set; }

        [JsonProperty("response_type")]
        public string ResponseType { get; set; } = "ephemeral";

        [JsonProperty("blocks")]
        public List<MessageBlock> Blocks { get; set; }

        public void AddSection(string markdownText)
        {
            if (Blocks == null)
                Blocks = new List<MessageBlock>();

            Blocks.Add(new MessageBlock
            {
                Type = "section",
                Text = new MessageBlockText
                {
                    Type = "mrkdwn",
                    Text = markdownText
                }
            });
        }

        public string Serialize()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    public class MessageBlock
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("text")]
        public MessageBlockText Text { get; set; }
    }

    public class MessageBlockText
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }
    }
}
