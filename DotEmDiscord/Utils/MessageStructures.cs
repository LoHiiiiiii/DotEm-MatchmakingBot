using Discord;
using Discord.WebSocket;
using DotemChatMatchmaker;
using DotemModel;
using System.Linq;

namespace DotemDiscord.Utils
{

    public static class MessageStructures
    {

        public static (string? content, MessageComponent? components) GetStructure(this SearchResult result)
        {
            return result switch
            {
                SearchResult.Searching searching => GetStructure(searching),
                SearchResult.Found found => GetStructure(found),
                SearchResult.Suggestions suggestions => GetStructure(suggestions),
                _ => (null, null)
            };
        }

        public static (string? content, MessageComponent? components) GetStructure(SearchResult.Searching searching)
        {
            var min = searching.searches.Select(s => s.ExpireTime).Min();

            var builder = new ComponentBuilder();
            
            foreach(var details in searching.searches) {
                var extra = details.PlayerCount == 2 ? "" : $" - 1/{details.PlayerCount}";
                builder.WithButton(
                    label: details.GameId + extra,
                    style: ButtonStyle.Primary,
                    customId: details.GameId
                    );
            }

            return ($"Search expires <t:{min.ToUnixTimeSeconds()}:R>", builder.Build());
        }

        public static (string? content, MessageComponent? components) GetStructure(SearchResult.Found found)
        {
            return ("Missing", null);
        }

        public static (string? content, MessageComponent? components) GetStructure(SearchResult.Suggestions suggestions)
        {
            return ("Missing", null);
        }

        public static (string? content, MessageComponent? components) GetStoppedStructure(this SearchResult result)
        {
            return ("No longer searching.", null);
        }

        public static (string? content, MessageComponent? components) GetFailedAcceptStructure(this SearchResult result)
        {
            return ("Missing", null);
        }

		public static (string? content, MessageComponent? components) GetNoSearchStructure(this SearchResult result) {
			return ("No search.", null);
		}

	}
}
