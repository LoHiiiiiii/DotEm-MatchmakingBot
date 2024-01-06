using Discord;
using Discord.WebSocket;
using DotemChatMatchmaker;
using DotemDiscord.Utils;
using DotemModel;

namespace DotemDiscord.ButtonMessages {

	public class SearchMessage {

		private readonly DiscordSocketClient _client;
		private readonly ChatMatchmaker _chatMatchmaker;

		public IUserMessage Message { get; }
		public Dictionary<Guid, SessionDetails> Searches { get; }
		public ulong CreatorId { get; }


		private SemaphoreSlim messageSemaphore = new SemaphoreSlim(1, 1);
		private bool released;

		public SearchMessage(DiscordSocketClient client, ChatMatchmaker chatMatchmaker, IUserMessage message, IEnumerable<SessionDetails> searches, ulong creatorId) {
			_client = client;
			_client.ButtonExecuted += HandleButtonPress;
			_client.MessageDeleted += HandleMessageDeleted;
			_chatMatchmaker = chatMatchmaker;
			_chatMatchmaker.SessionChanged += HandleSessionChanged;
			Message = message;
			CreatorId = creatorId;
			Searches = searches.ToDictionary(s => s.SessionId, s => s);
		}

		public async Task HandleButtonPress(SocketMessageComponent component) {
			try {
				if (component.Message.Id != Message.Id) { return; }
				await messageSemaphore.WaitAsync();
				try {
					if (!Guid.TryParse(component.Data.CustomId, out var guid)) { return; }
					if (!Searches.TryGetValue(guid, out var search)) { return; }

					var stringId = component.User.Id.ToString();
					if (search.UserExpires.ContainsKey(stringId)) {
						var sessionsLeaved = await _chatMatchmaker.LeaveSessionsAsync(stringId, search.SessionId);

						foreach (var session in sessionsLeaved) { 
							if (!session.UserExpires.Any()) Searches.Remove(session.SessionId); 
						}

						await UpdateMessage();
						await component.DeferAsync();
						return;
					}

					var result = await _chatMatchmaker.TryJoinSessionAsync(stringId, search.SessionId);
					
					var (structure, ephemeral) = (MessageStructures.GetFailedJoinStructure(), true);
					
					if (result is SessionResult.Matched matched) {
						structure = MessageStructures.GetMatchedStructure(
							gameName: _chatMatchmaker.GameNameHandler.GetGameIdFullName(matched.matchedSession.GameId),
							playerIds: matched.playerIds,
							description: matched.description
						);
						ephemeral = false;
						Searches.Remove(matched.matchedSession.SessionId);
					}
					
					await UpdateMessage();
					await component.DeferAsync();

					if (result is SessionResult.Waiting) return;
					
					await component.FollowupAsync(
						text: structure.content,
						components: structure.components,
						ephemeral: ephemeral
					);
				} finally { messageSemaphore.Release(); }
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionException(component);

			}
		}

		// Remember to await semaphore before calling!
		private async Task UpdateMessage() {
			if (released) return;
			var stillSearching = Searches.Any();
			if (!stillSearching) { Release(); }
			var structure = stillSearching
				? MessageStructures.GetWaitingStructure(Searches.Values, CreatorId)
				: MessageStructures.GetStoppedStructure();
		
			await Message.ModifyAsync(x => {
				x.Content = structure.content;
				x.Components = structure.components;
				x.AllowedMentions = AllowedMentions.None;
			});
		}

		private void Release() {
			if (released) { return; }
			_client.ButtonExecuted -= HandleButtonPress;
			_client.MessageDeleted -= HandleMessageDeleted;
			_chatMatchmaker.SessionChanged -= HandleSessionChanged;
			Searches.Clear();
			released = true;
		}

		private async Task HandleMessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel) {
			if (message.Id != Message.Id) { return; }
			await messageSemaphore.WaitAsync();
			try {
				Release();
			} finally { messageSemaphore.Release(); }
		}

		private async void HandleSessionChanged(SessionDetails[] _, SessionDetails[] updated, SessionDetails[] stopped) {
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
	}
}
