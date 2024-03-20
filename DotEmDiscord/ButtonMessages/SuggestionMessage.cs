using Discord;
using Discord.Rest;
using Discord.WebSocket;
using DotemMatchmaker;
using DotemDiscord.Utils;
using DotemModel;
using Discord.Net;

namespace DotemDiscord.ButtonMessages {

	public class SuggestionMessage {

		private readonly DiscordSocketClient _client;
		private readonly Matchmaker _matchmaker;

		public Guid SuggestionId { get; } = Guid.NewGuid();
		public IUserMessage Message { get; }
		public Dictionary<Guid, SessionDetails> JoinableSessions { get; }
		public ulong CreatorId { get; }
		public ulong ServerId { get; }
		public ulong ChannelId { get; }
		public int? DurationMinutes { get; } //Outside of search params because full session might have someone left once accepted
		public (string[]? gameIds, string? description, int? playerCount)? SearchParams { get; }
		public SemaphoreSlim SuggestionSignal { get; } = new SemaphoreSlim(0, 1);
		public SemaphoreSlim MessageSemaphore { get; } = new SemaphoreSlim(0, 1); // To handle creating before accepting calls
		public SessionResult? ExitResult { get; private set; } = null;
		public bool Released { get; private set; }
		public bool AllowCancel { get; private set; }

		public SuggestionMessage(
			DiscordSocketClient client,
			Matchmaker matchmaker,
			IUserMessage message,
			IEnumerable<SessionDetails> joinableSessions,
			ulong creatorId,
			int? durationMinutes,
			(string[]? gameIds, string? description, int? playerCount)? searchParams,
			Guid id,
			bool allowCancel
		) {
			_client = client;
			_client.ButtonExecuted += HandleButtonPress;
			_client.MessageDeleted += HandleMessageDeleted;
			_matchmaker = matchmaker;
			_matchmaker.SessionChanged += HandleSessionChanged;
			Message = message;
			JoinableSessions = joinableSessions.ToDictionary(s => s.SessionId, s => s);
			CreatorId = creatorId;
			DurationMinutes = durationMinutes;
			SearchParams = searchParams;
			SuggestionId = id;
			AllowCancel = allowCancel;
		}

		public async Task HandleButtonPress(SocketMessageComponent component) {
			if (component.Message.Id != Message.Id) { return; }
			await MessageSemaphore.WaitAsync();
			try {
				if (component.Data.CustomId == SuggestionId.ToString()) {
					ExitResult = (await _matchmaker.SearchSessionAsync(
						serverId: ServerId.ToString(),
						userId: CreatorId.ToString(),
						gameIds: SearchParams?.gameIds ?? [],
						maxPlayerCount: SearchParams?.playerCount,
						joinDuration: DurationMinutes,
						description: SearchParams?.description,
						allowSuggestions: false
						));
					await UpdateMessageAsync();
					await component.DeferAsync();
					return;
				}

				if (component.Data.CustomId == MessageStructures.CANCEL_ID) {
					await DeleteMessage();
					Release();
					await component.DeferAsync();
					return;
				}

				if (!Guid.TryParse(component.Data.CustomId, out var guid)) { return; }
				if (!JoinableSessions.ContainsKey(guid)) { return; }

				var result = await _matchmaker.TryJoinSessionAsync(CreatorId.ToString(), guid, DurationMinutes);
				if (result is not SessionResult.FailedToJoin
					&& result is not SessionResult.NoAction) { 
					ExitResult = result; 
				}
				await UpdateMessageAsync();
				await component.DeferAsync();
			} catch (Exception e) {
				Console.WriteLine(e.Message);
			} finally { MessageSemaphore.Release(); }
		}

		private void Release() {
			if (Released) return;
			_client.ButtonExecuted -= HandleButtonPress;
			_client.MessageDeleted -= HandleMessageDeleted;
			_matchmaker.SessionChanged -= HandleSessionChanged;
			SuggestionSignal.Release();
			Released = true;
		}

		private async Task UpdateMessageAsync() {
			if (
				ExitResult != null 
				|| (!JoinableSessions.Any() && SearchParams == null)
			) {
				await DeleteMessage();
				Release();
				return;
			}

			try {
				var structure = MessageStructures.GetSuggestionStructure(
					joinables: JoinableSessions.Values,
					userId: CreatorId,
					searchId: SearchParams != null
						? SuggestionId
						: null,
					allowCancel: AllowCancel
				);

				if (Message is RestFollowupMessage restMessage) {
					await restMessage.ModifyAsync(x => {
						x.Content = structure.content;
						x.Components = structure.components;
					});
				} else {
					await Message.ModifyAsync(x => {
						x.Content = structure.content;
						x.Components = structure.components;
					});
				}
			} catch (Exception e) {
				Console.WriteLine(e.Message);
			}
		}

		private async Task DeleteMessage() {
			try {
				if (Message is RestFollowupMessage restMessage) {
					await restMessage.DeleteAsync();
				} else {
					await Message.DeleteAsync();
				}
			} catch (Exception e) {
				if (e is HttpException http) {
					if (http.DiscordCode != DiscordErrorCode.UnknownMessage) { throw; }
				}
			}
		}

		private async void HandleSessionChanged(IEnumerable<SessionDetails> added, IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped) {
			try {
				if (!updated.Any() && !stopped.Any()) { return; }

				await MessageSemaphore.WaitAsync();
				try {
					var modified = false;
					foreach (var id in stopped) {
						if (!JoinableSessions.ContainsKey(id)) continue;
						modified = true;
						JoinableSessions.Remove(id);
					}
					foreach (var session in updated) {
						if (!JoinableSessions.ContainsKey(session.SessionId)) { continue; }
						modified = true;
						JoinableSessions[session.SessionId] = session;
					}
					if (modified) { await UpdateMessageAsync(); }
				} finally { MessageSemaphore.Release(); }
			} catch (Exception e) {
				Console.WriteLine(e.Message);
			}
		}

		private async Task HandleMessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel) {
			if (message.Id != Message.Id) { return; }

			await MessageSemaphore.WaitAsync();
			try {
				Release();
			} finally { MessageSemaphore.Release(); }
		}
	}
}
