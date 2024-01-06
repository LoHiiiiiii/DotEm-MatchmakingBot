using Discord;
using Discord.Rest;
using Discord.WebSocket;
using DotemChatMatchmaker;
using DotemDiscord.Utils;
using DotemModel;

namespace DotemDiscord.ButtonMessages {

	public class SuggestionMessage {

		private readonly DiscordSocketClient _client;
		private readonly ChatMatchmaker _chatMatchmaker;

		public Guid SuggestionId { get; } = Guid.NewGuid();

		public IUserMessage Message { get; }
		public Dictionary<Guid, SessionDetails> JoinableSessions { get; }
		public ulong CreatorId { get; }
		public ulong ServerId { get; }
		public ulong ChannelId { get; }
		public int DurationMinutes { get; } //Outside of search params because full session might have someone left once accepted
		public (string[]? gameIds, string? description, int? playerCount)? SearchParams { get; }
		public SemaphoreSlim SuggestionSignal { get; } = new SemaphoreSlim(0, 1);
		public SemaphoreSlim MessageSemaphore { get; } = new SemaphoreSlim(0, 1);// To handle creating before accepting calls
		public SessionResult? ExitResult { get; private set; } = null;
		public bool Released { get; private set; }

		public SuggestionMessage(
			DiscordSocketClient client,
			ChatMatchmaker chatMatchmaker,
			IUserMessage message,
			IEnumerable<SessionDetails> joinableSessions,
			ulong creatorId,
			int durationMinutes,
			(string[]? gameIds, string? description, int? playerCount)? searchParams,
			Guid id
		) {
			_client = client;
			_client.ButtonExecuted += HandleButtonPress;
			_client.MessageDeleted += HandleMessageDeleted;
			_chatMatchmaker = chatMatchmaker;
			_chatMatchmaker.SessionChanged += HandleSessionChanged;
			Message = message;
			JoinableSessions = joinableSessions.ToDictionary(s => s.SessionId, s => s);
			CreatorId = creatorId;
			DurationMinutes = durationMinutes;
			SearchParams = searchParams;
			SuggestionId = id;
		}

		public async Task HandleButtonPress(SocketMessageComponent component) {
			if (component.Message.Id != Message.Id) { return; }
			await MessageSemaphore.WaitAsync();
			try {
				if (component.Data.CustomId == SuggestionId.ToString()) {
					ExitResult = (await _chatMatchmaker.SearchSessionAsync(
						serverId: ServerId.ToString(),
						userId: CreatorId.ToString(),
						channelId: ChannelId.ToString(),
						gameIds: SearchParams?.gameIds,
						playerCount: SearchParams?.playerCount,
						durationMinutes: DurationMinutes,
						description: SearchParams?.description,
						allowSuggestions: false
						));
					await UpdateMessage();
					await component.DeferAsync();
					Release();
					return;
				}

				if (!Guid.TryParse(component.Data.CustomId, out var guid)) { return; }
				if (!JoinableSessions.TryGetValue(guid, out var _)) { return; }

				var result = await _chatMatchmaker.TryJoinSessionAsync(CreatorId.ToString(), guid, DurationMinutes);
				if (result is not SessionResult.Waiting 
					&& result is not SessionResult.NoAction) { ExitResult = result; }
				await UpdateMessage();
				await component.DeferAsync();
				Release();
			} finally { MessageSemaphore.Release(); }
		}

		private void Release() {
			if (Released) return;
			_client.ButtonExecuted -= HandleButtonPress;
			_client.MessageDeleted -= HandleMessageDeleted;
			_chatMatchmaker.SessionChanged -= HandleSessionChanged;
			SuggestionSignal.Release();
			Released = true;
		}

		private async Task UpdateMessage() {
			if (ExitResult != null) {
				if (Message is RestFollowupMessage) {
					await ((RestFollowupMessage) Message).DeleteAsync();
				} else await Message.DeleteAsync();
				return;
			}

			var structure =  MessageStructures.GetSuggestionStructure(
				joinables: JoinableSessions.Values,
				userId: CreatorId,
				searchId: SearchParams != null
					? SuggestionId
					: null
			);

			if (Message is RestFollowupMessage) {
				await ((RestFollowupMessage)Message).ModifyAsync(x => {
					x.Content = structure.content;
					x.Components = structure.components;
				});
			}
			else await Message.ModifyAsync(x => {
				x.Content = structure.content;
				x.Components = structure.components;
			});
		}

		private async void HandleSessionChanged(SessionDetails[] _, SessionDetails[] updated, SessionDetails[] stopped) {
			if (!updated.Any() && !stopped.Any()) { return; }

			await MessageSemaphore.WaitAsync();
			try {
				var modified = false;
				foreach (var search in stopped) {
					if (!JoinableSessions.ContainsKey(search.SessionId)) continue;
					modified = true;
					JoinableSessions.Remove(search.SessionId);
				}
				if (!modified) {
					foreach (var search in updated) {
						if (!JoinableSessions.ContainsKey(search.SessionId)) { continue; }
						modified = true;
						break;
					}
				}
				if (modified) { await UpdateMessage(); }
			} finally { MessageSemaphore.Release(); }
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
