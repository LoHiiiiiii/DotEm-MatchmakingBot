using Discord;
using Discord.WebSocket;
using Matchmaker.Model;

namespace DotemDiscord.Handlers {

	public class SearchMessageHandler {

		private readonly DiscordSocketClient _client;
		private Dictionary<ulong, PlayerMessages> playerMessages = new Dictionary<ulong, PlayerMessages>();

		private class PlayerMessages {
			public ulong Id { get; private set; }
			public HashSet<MessageDetails> Messages { get; } = new HashSet<MessageDetails>();

			public PlayerMessages(ulong id) {
				Id = id;
			}
		}

		private class MessageDetails {
			public ulong MessageId { get; set; }
			public ulong ChannelId { get; set; }
			public HashSet<SearchDetails> Searches { get; } = new HashSet<SearchDetails>();

			public MessageDetails(ulong messageId, ulong channelId) {
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
			if (!playerMessages.ContainsKey(id)) return;
		}


		public async Task<MessageComponent> AddMessage(IUserMessage message, SearchDetails[] searches) {
			var messageDetails = new MessageDetails(message.Id, message.Channel.Id);
			messageDetails.Searches.UnionWith(searches.ToHashSet());
			PlayerMessages player;
			if (playerMessages.ContainsKey(message.Author.Id)) {
				player = playerMessages[message.Author.Id];
			} else {
				player = new PlayerMessages(message.Author.Id);
				playerMessages.Add(player.Id, player);
			}
			player.Messages.Add(messageDetails);
			return await GetMatchButtons(messageDetails);
		}

		private async Task<MessageComponent> GetMatchButtons(MessageDetails details) {
			var channel = await _client.GetChannelAsync(details.ChannelId);
			IMessage? message = null;

			if (channel is ISocketMessageChannel messageChannel) {
				message = await messageChannel.GetMessageAsync(details.MessageId);
			}


			var builder = new ComponentBuilder();
			if (message == null || !(message is IUserMessage userMessage)) return builder.Build();
			foreach (SearchDetails search in details.Searches) {
				builder.WithButton(search.GameName, search.GameId);
			}

			return builder.Build();
		}
	}
}
