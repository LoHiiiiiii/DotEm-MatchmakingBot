using Discord.Rest;
using Discord.WebSocket;
using DotemChatMatchmaker;
using DotemDiscord.Utils;
using DotemModel;

namespace DotemDiscord.Messages {

	public class SearchMessage {

		private readonly DiscordSocketClient _client;
		private readonly ChatMatchmaker _matchmaker;

		public RestInteractionMessage Message { get; }
		public Dictionary<Guid, SessionDetails> Searches { get; }
		public ulong CreatorId { get; }


		private SemaphoreSlim messageSemaphore = new SemaphoreSlim(1, 1);

		public SearchMessage(DiscordSocketClient client, ChatMatchmaker matchmaker, RestInteractionMessage message, SessionDetails[] searches, ulong creatorId) {
			_client = client;
			_client.ButtonExecuted += HandleButtonPress;
			_matchmaker = matchmaker;
			_matchmaker.SessionChanged += HandleSearchUpdated;
			Message = message;
			CreatorId = creatorId;
			Searches = searches.ToDictionary(s => s.SessionId, s => s);
		}

		public async Task HandleButtonPress(SocketMessageComponent component) {
			try {
				if (component.Message.Id != Message.Id) { return; }
				await messageSemaphore.WaitAsync();
				try {
					if (!Searches.TryGetValue(new Guid(component.Data.CustomId), out var search)) { return; }

					var stringId = component.User.Id.ToString();
					if (search.UserExpires.ContainsKey(stringId)) {
						var sessionsLeaved = await _matchmaker.LeaveSessionsAsync(stringId, search.SessionId);

						foreach (var session in sessionsLeaved) { 
							if (!session.UserExpires.Any()) Searches.Remove(session.SessionId); 
						}

						await UpdateMessage();
						await component.DeferAsync();

						var noSearchStructure = MessageStructures.GetStoppedStructure();
						await component.FollowupAsync(
							text: noSearchStructure.content,
							components: noSearchStructure.components,
							ephemeral: true
						);
						return;
					}

					var result = await _matchmaker.TryJoinSessionAsync(stringId, search.SessionId);
					await UpdateMessage();
					await component.DeferAsync();

					if (result is SessionResult.Waiting) return;
					var (structure, ephemeral) = result switch {
						SessionResult.NoAction => (MessageStructures.GetFailedJoinStructure(), true),
						SessionResult.Matched matched => (MessageStructures.GetMatchedStructure(matched.gameId, matched.playerIds, matched.description), false),
						_ => (("Unknown result.", null), true)
					};
					await component.FollowupAsync(
						text: structure.content,
						components: structure.components,
						ephemeral: ephemeral
					);
				} finally { messageSemaphore.Release(); }
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportSlashCommandException(component);

			}
		}

		// Remember to await semaphore before calling!
		private async Task UpdateMessage() {
			var stillSearching = Searches.Any();
			if (!stillSearching) {
				_client.ButtonExecuted -= HandleButtonPress;
				_matchmaker.SessionChanged -= HandleSearchUpdated;
			}
			var structure = stillSearching
				? MessageStructures.GetWaitingStructure(Searches.Values, CreatorId)
				: MessageStructures.GetStoppedStructure();
		
			await Message.ModifyAsync(x => {
				x.Content = structure.content;
				x.Components = structure.components;
			});
		}

		private async void HandleSearchUpdated(SessionDetails[] _, SessionDetails[] updated, SessionDetails[] stopped) {
			if (!updated.Any() && !stopped.Any()) { return; }

			await messageSemaphore.WaitAsync();
			try {
				var modified = false;
				foreach (var search in stopped) {
					if (!Searches.ContainsKey(search.SessionId)) continue;
					modified = true;
					Searches.Remove(search.SessionId);
				}
				if (!modified) {
					foreach (var search in updated) {
						if (!Searches.ContainsKey(search.SessionId)) { continue; }
						modified = true;
						break;
					}
				}
				if (modified) { await UpdateMessage(); }
			} finally { messageSemaphore.Release(); }
		}

		public static SearchMessage Create(
			DiscordSocketClient client,
			ChatMatchmaker matchmaker,
			RestInteractionMessage message,
			SessionDetails[] searches,
			ulong creatorId) => new SearchMessage(client, matchmaker, message, searches, creatorId);
	}
}
