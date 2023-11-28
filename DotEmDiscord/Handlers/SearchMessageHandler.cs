using Discord;
using Discord.WebSocket;
using DotemChatMatchmaker;
using DotemDiscord.Utils;
using DotemModel;

namespace DotemDiscord.Handlers {

	public class SearchMessageHandler {

		private readonly DiscordSocketClient _client;
		private readonly ChatMatchmaker _matchmaker;

		private Dictionary<ulong, (IMessage message, SemaphoreSlim semaphore, Dictionary<string, SearchDetails > searches)> searchMessages = new();

		public SearchMessageHandler(DiscordSocketClient client, ChatMatchmaker matchmaker) {
			_client = client;
			_client.ButtonExecuted += HandleButtonPress;
			_matchmaker = matchmaker;
			_matchmaker.SearchChanged += HandleSearchUpdated;
		}

		public async void AddMessageAsync(ulong channelId, ulong messageId, SearchDetails[] searchDetails) {
			var channel = _client.GetChannel(channelId);
			if (channel is not SocketTextChannel textChannel) return;
			AddMessage(await textChannel.GetMessageAsync(messageId), searchDetails);
		}

		public void AddMessage(IMessage message, SearchDetails[] searchDetails) {

			var detailDictionary = searchDetails.ToDictionary(details => details.GameId, details => details);
			searchMessages.Add(message.Id, (message, new SemaphoreSlim(1, 1), detailDictionary));
		}

		public async Task HandleButtonPress(SocketMessageComponent component) {
			if (!searchMessages.TryGetValue(component.Message.Id, out var message)) return;
			await message.semaphore.WaitAsync();
			try {
				if (!message.searches.TryGetValue(component.Data.CustomId, out var search)) return;
				if (component.User.Id.ToString() == search.UserId) {
					await _matchmaker.CancelSearchesAsync(search.SearchId);
					await component.RespondAsync(text: "No longer searching.", ephemeral: true);
				} else {
					var result = await _matchmaker.TryAcceptMatchAsync(component.User.Id.ToString(), search.SearchId);
					
					await ReplyToMessage(component, result);
				}
			} finally { message.semaphore.Release(); }
		}

		private async Task ReplyToMessage(SocketMessageComponent component, SearchResult searchResult) {
			var structure = searchResult switch {
				SearchResult.NoSearch => searchResult.GetFailedAcceptStructure(),
				_ => searchResult.GetStructure()
			};
			if (structure.content == null) return;
			await component.RespondAsync(text: structure.content, components: structure.components);
		}

		private async void HandleSearchUpdated(object sender, SearchDetails[]? added, SearchDetails[]? updated, SearchDetails[]? stopped) { 
			if (stopped != null) {
				
			}
			
			if (added != null) {

			}
		}
	}
}
