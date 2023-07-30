using Discord;
using Discord.WebSocket;
using Matchmaker.Model;

namespace DotemDiscord.Handlers {

	public class SearchMessageHandler {

		private readonly DiscordSocketClient _client;
		private Dictionary<ulong, PlayerResponses> playerResponses = new Dictionary<ulong, PlayerResponses>();

		private class PlayerResponses {
			public ulong Id { get; private set; }
			public HashSet<ResponseDetail> Responses { get; } = new HashSet<ResponseDetail>();

			public PlayerResponses(ulong id) {
				Id = id;
			}
		}

		private class ResponseDetail {
			public ulong MessageId { get; set; }
			public ulong ChannelId { get; set; }
			public HashSet<SearchDetails> Searches { get; } = new HashSet<SearchDetails>();

			public ResponseDetail(ulong messageId, ulong channelId) {
				MessageId = messageId;
				ChannelId = channelId;
			}
		}

		public SearchMessageHandler(DiscordSocketClient client) {
			_client = client;
		}

		public void PlayerSearchCanceled(string playerId, params string[] gameKeys) {
			var parsed = ulong.TryParse(playerId, out var id);
			if (!parsed) return;
			if (!playerResponses.ContainsKey(id)) return;
			// TODO: Cancel seaarch
		}


		public async Task<(string? content, MessageComponent? components)> CreateMatchSearchParts(IUserMessage response, IUser user, string[] gameIds, int duration, string description) {
			var messageDetails = new ResponseDetail(response.Id, response.Channel.Id);
			//messageDetails.Searches.UnionWith(searches.ToHashSet());
			PlayerResponses player;
			if (playerResponses.ContainsKey(user.Id)) {
				player = playerResponses[user.Id];
			} else {
				player = new PlayerResponses(user.Id);
				playerResponses.Add(player.Id, player);
			}
			player.Responses.Add(messageDetails);
			var buttons =  await GetMatchButtons(messageDetails);
			return (null, null);
		}

		private async Task<MessageComponent> GetMatchButtons(ResponseDetail details) {
			var channel = await _client.GetChannelAsync(details.ChannelId);
			IMessage? message = null;

			if (channel is ISocketMessageChannel messageChannel) {
				message = await messageChannel.GetMessageAsync(details.MessageId);
			}


			var builder = new ComponentBuilder();
			if (message == null || !(message is IUserMessage userMessage)) return builder.Build();
			foreach (SearchDetails search in details.Searches) {
				builder.WithButton(search.GameId, search.GameId);
			}

			return builder.Build();
		}
	}
}
