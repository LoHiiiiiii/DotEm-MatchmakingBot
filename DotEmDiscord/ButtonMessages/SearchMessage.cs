using Discord;
using Discord.Net;
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
		public ulong? CreatorId { get; }
		public bool DeleteOnStop { get; }
		public IUserMessage? ReplyMessage { get; set; }

		private SemaphoreSlim messageSemaphore = new SemaphoreSlim(1, 1);
		private bool released;
		private bool deleted;

		public SearchMessage(
			DiscordSocketClient client,
			Matchmaker matchmaker,
			DiscordContext context,
			IUserMessage message,
			IEnumerable<SessionDetails> searches,
			ulong? creatorId,
			bool deleteOnStop = false
		) {
			_client = client;
			_client.ButtonExecuted += HandleButtonPress;
			_client.MessageDeleted += HandleMessageDeleted;
			_matchmaker = matchmaker;
			_matchmaker.SessionChanged += HandleSessionChanged;
			_context = context;
			Message = message;
			CreatorId = creatorId;
			Searches = searches.ToDictionary(s => s.SessionId, s => s);
			DeleteOnStop = deleteOnStop;
		}

		public async Task HandleButtonPress(SocketMessageComponent component) {
			try {
				if (component.Message.Id != Message.Id) { return; }
				await messageSemaphore.WaitAsync();
				try {
					if (!Guid.TryParse(component.Data.CustomId, out var guid)) { return; }
					if (!Searches.TryGetValue(guid, out var search)) { return; }

					var stringId = component.User.Id.ToString();
					SessionStopReason? stopReason = null;

					if (search.UserExpires.ContainsKey(stringId)) {
						(var updated, var stopped) = await _matchmaker.LeaveSessionsAsync(stringId, search.SessionId);

						foreach (var session in updated) {
							if (!Searches.ContainsKey(session.SessionId)) { continue; }
							Searches[session.SessionId] = session;
						}

						foreach (var id in stopped.Keys) {
							if (Searches.ContainsKey(id)) { Searches.Remove(id); }
							await _context.DeleteSessionConnectionAsync(Message.Id, id);

							stopReason = stopReason.GetHigherPriorityReason(stopped[id]);
						}

						if (!Searches.Any()) {
							await ReleaseAsync();
						}

						await UpdateMessageAsync(stopReason);
						await component.DeferAsync();
						return;
					}

					var expireTime = MessageStructures.GetDisplayedUserExpire(Searches.Values, CreatorId);
					var result = await _matchmaker.TryJoinSessionAsync(stringId, search.SessionId, expireTime);

					var (structure, ephemeral) = (MessageStructures.GetFailedJoinStructure(), true);

					if (result is SessionResult.Matched matched) {
						structure = MessageStructures.GetMatchedStructure(
							gameName: matched.matchedSession.GameName,
							playerIds: matched.matchedSession.UserExpires.Keys,
							description: matched.matchedSession.Description
						);
						ephemeral = false;
						await ReleaseAsync();
						stopReason = SessionStopReason.Joined;
					}
					await UpdateMessageAsync(stopReason);
					await component.DeferAsync();

					if (result is SessionResult.Waiting) return;

					if (!ephemeral && ReplyMessage != null) {
						await ReplyMessage.ReplyAsync(text: structure.content, components: structure.components);
						return;
					}

					await component.FollowupAsync(
						text: structure.content,
						components: structure.components,
						ephemeral: ephemeral
					);
				} finally { messageSemaphore.Release(); }
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				if (e is HttpException unknown && unknown.DiscordCode == DiscordErrorCode.UnknownInteraction) return;
				if (e is HttpException acknowledged && acknowledged.DiscordCode == DiscordErrorCode.InteractionHasAlreadyBeenAcknowledged) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(component);

			}
		}

		public async void ForceMessageUpdate() {
			try {
				await messageSemaphore.WaitAsync();
				try {
					await UpdateMessageAsync();
				} finally { messageSemaphore.Release(); }
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
			}
		}

		public async Task VerifySearchesAsync() {
			var existing = (await _matchmaker.GetSessionsAsync(Searches.Keys.ToArray()))
				.Select(s => s.SessionId)
				.ToHashSet();

			var missing = Searches.Keys.Where(id => !existing.Contains(id)).ToArray();
			if (missing.Length == 0) return;

			await messageSemaphore.WaitAsync();
			try {
				foreach (var id in missing) Searches.Remove(id);
				await UpdateMessageAsync();
			} finally { messageSemaphore.Release(); }
		}

		// Remember to await semaphore before calling!
		private async Task UpdateMessageAsync(SessionStopReason? stopReason = null) {
			if (deleted) { return; }
			var stillSearching = Searches.Any();

			if (!stillSearching && DeleteOnStop) {
				for (int i = 0; i < 3; i++) {
					try {
						await Message.DeleteAsync();
						deleted = true;
						break;
					} catch (HttpException e) when (
						  e.DiscordCode == DiscordErrorCode.UnknownMessage ||
						  e.DiscordCode == DiscordErrorCode.MissingPermissions) { break; } catch { await Task.Delay(1000); }
				}
				if (!deleted) { return; }
				if (!released) { await ReleaseAsync(); }
				return;
			}

			var structure = stillSearching
				? MessageStructures.GetWaitingStructure(Searches.Values, CreatorId)
				: MessageStructures.GetSessionStoppedStructure(stopReason);

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
			if (message.Id != Message.Id || released) { return; }

			await messageSemaphore.WaitAsync();
			try {
				deleted = true;
				await ReleaseAsync();
			} finally { messageSemaphore.Release(); }
		}

		private async void HandleSessionChanged(IEnumerable<SessionDetails> updated, Dictionary<Guid, SessionStopReason> stopped) {
			try {
				if (!updated.Any() && !stopped.Any()) { return; }

				await messageSemaphore.WaitAsync();
				try {
					SessionStopReason? stopReason = null;
					var modified = false;
					foreach (var id in stopped.Keys) {
						if (!Searches.ContainsKey(id)) { continue; }
						modified = true;
						Searches.Remove(id);
						stopReason = stopReason.GetHigherPriorityReason(stopped[id]);
					}
					foreach (var session in updated) {
						if (!Searches.ContainsKey(session.SessionId)) { continue; }
						modified = true;
						Searches[session.SessionId] = session;
					}
					if (modified) { await UpdateMessageAsync(stopReason); }
				} finally { messageSemaphore.Release(); }
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
			}
		}
	}
}
