using Discord;
using Discord.WebSocket;
using DotemChatMatchmaker;
using DotemModel;

namespace DotemDiscord.Handlers {

	public class SearchMessageHandler {

		private readonly DiscordSocketClient _client;
		private readonly ChatMatchmaker _matchmaker;

		public SearchMessageHandler(DiscordSocketClient client, ChatMatchmaker matchmaker) {
			_client = client;
			_client.ButtonExecuted += HandleButtonPress;

			_matchmaker = matchmaker;
			_matchmaker.SearchChanged += HandleSearchUpdated;
		}

		private async Task HandleButtonPress(SocketMessageComponent component) {
			await Task.CompletedTask;
		}

		private void HandleSearchUpdated(object sender, SearchDetails[]? added, SearchDetails[]? updated, SearchDetails[]? stopped) { }
	}
}
