using Discord;
using Discord.WebSocket;
using DotemDiscord.Context;
using DotemDiscord.Utils;
using DotemMatchmaker;
using DotemModel;

namespace DotemDiscord.ButtonMessages {

	public class SearchMessage {

		private readonly DiscordSocketClient _client;
		private readonly DiscordContext _context;
		private readonly Matchmaker _matchmaker;

		public IUserMessage Message { get; }
		public Dictionary<Guid, SessionDetails> Searches { get; }
		public ulong CreatorId { get; }

		private SemaphoreSlim messageSemaphore = new SemaphoreSlim(1, 1);
		private bool released;

		public SearchMessage(DiscordSocketClient client, Matchmaker matchmaker, DiscordContext context, IUserMessage message, IEnumerable<SessionDetails> searches, ulong creatorId) {
			_client = client;
			_client.ButtonExecuted += HandleButtonPress;
			_client.MessageDeleted += HandleMessageDeleted;
			_matchmaker = matchmaker;
			_matchmaker.SessionChanged += HandleSessionChanged;
			_context = context;
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
						(var updated, var stopped) = await _matchmaker.LeaveSessionsAsync(stringId, search.SessionId);

						foreach (var session in updated) {
							if (!Searches.ContainsKey(session.SessionId)) { continue; }
							Searches[session.SessionId] = session;
						}

						foreach (var id in stopped) {
							if (Searches.ContainsKey(id)) Searches.Remove(id);
							await _context.DeleteSessionConnectionAsync(Message.Id, id);
						}

						if (!Searches.Any()) {
							await ReleaseAsync();
						}

						await UpdateMessageAsync();
						await component.DeferAsync();
						return;
					}

					var result = await _matchmaker.TryJoinSessionAsync(stringId, search.SessionId);

					var (structure, ephemeral) = (MessageStructures.GetFailedJoinStructure(), true);

					if (result is SessionResult.Matched matched) {
						structure = MessageStructures.GetMatchedStructure(
							gameName: matched.matchedSession.GameName,
							playerIds: matched.matchedSession.UserExpires.Keys,
							description: matched.matchedSession.Description
						);
						ephemeral = false;
						await ReleaseAsync();
					}

					await UpdateMessageAsync();
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
				await ExceptionHandling.ReportInteractionExceptionAsync(component);

			}
		}

		// Remember to await semaphore before calling!
		private async Task UpdateMessageAsync() {
			var stillSearching = Searches.Any();
			var structure = stillSearching
				? MessageStructures.GetWaitingStructure(Searches.Values, CreatorId)
				: MessageStructures.GetStoppedStructure();

			await Message.ModifyAsync(x => {
				x.Content = structure.content;
				x.Components = structure.components;
				x.AllowedMentions = AllowedMentions.None;
			});
		}

		private async Task ReleaseAsync() {
			if (released) { return; }
			if (Searches.Any()) {
				await _context.DeleteSessionConnectionAsync(messageId: Message.Id, sessionIds: Searches.Keys.ToArray());
			}
			_client.ButtonExecuted -= HandleButtonPress;
			_client.MessageDeleted -= HandleMessageDeleted;
			_matchmaker.SessionChanged -= HandleSessionChanged;
			Searches.Clear();
			released = true;
		}

		private async Task HandleMessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel) {
			if (message.Id != Message.Id) { return; }

			await messageSemaphore.WaitAsync();
			try {
				await ReleaseAsync();
			} finally { messageSemaphore.Release(); }
		}

		private async void HandleSessionChanged(IEnumerable<SessionDetails> added, IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped) {
			if (!updated.Any() && !stopped.Any()) { return; }

			await messageSemaphore.WaitAsync();
			try {
				var modified = false;
				foreach (var id in stopped) {
					if (!Searches.ContainsKey(id)) continue;
					modified = true;
					Searches.Remove(id);
				}
				foreach (var session in updated) {
					if (!Searches.ContainsKey(session.SessionId)) { continue; }
					modified = true;
					Searches[session.SessionId] = session;
				}
				if (modified) { await UpdateMessageAsync(); }
			} finally { messageSemaphore.Release(); }
		}
	}
}
