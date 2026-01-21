using Discord.WebSocket;
using Discord;
using DotemMatchmaker;
using DotemDiscord.ButtonMessages;
using DotemDiscord.Utils;
using DotemModel;
using DotemDiscord.Context;
using Discord.Net;
using DotemExtensions;

namespace DotemDiscord.Handlers {
	public class ButtonMessageHandler {

		private readonly int timeOutMinutes = 15;

		public readonly DiscordContext _discordContext;
		public readonly ExtensionContext _extensionContext;
		public readonly Matchmaker _matchmaker;
		public readonly DiscordSocketClient _client;

		public delegate void SearchMessageEvent(SearchMessage searchMessage);
		public event SearchMessageEvent? SearchMessageCreated;

		public ButtonMessageHandler(DiscordContext discordContext, ExtensionContext extensionContext, Matchmaker matchmaker, DiscordSocketClient client) {
			_discordContext = discordContext;
			_extensionContext = extensionContext;
			_matchmaker = matchmaker;
			_matchmaker.SessionChanged += HandleSessionChanged;
			_client = client;
		}

		public async Task<SessionResult> GetSuggestionResultAsync(
			SocketInteraction interaction,
			IEnumerable<SessionDetails> joinableSessions,
			int? durationMinutes,
			(string[]? gameIds, string? description, int? playerCount)? searchParams,
			bool allowCancel = false
		) {
			var guid = Guid.NewGuid();
			var structure = MessageStructures.GetSuggestionStructure(
				joinables: joinableSessions,
				userId: interaction.User.Id,
				searchId: searchParams != null
					? guid
					: null,
				allowCancel: allowCancel
			);
			var followup = await interaction.FollowupAsync(
				text: structure.content,
				components: structure.components,
				ephemeral: true
			);

			if (interaction.GuildId == null) {
				throw new ArgumentNullException("Can't suggest without a server");
			}

			var suggestion = new SuggestionMessage(
				client: _client,
				matchmaker: _matchmaker,
				message: followup,
				joinableSessions: joinableSessions,
				creatorId: interaction.User.Id,
				serverId: interaction.GuildId.Value,
				durationMinutes: durationMinutes,
				searchParams: searchParams,
				id: guid,
				allowCancel: allowCancel
			);
			suggestion.MessageSemaphore.Release();

			var cts = new CancellationTokenSource();
			cts.CancelAfter(timeOutMinutes * 60 * 1000);

			try {
				await suggestion.SuggestionSignal.WaitAsync(cts.Token);
			} catch (OperationCanceledException) { }

			return suggestion.ExitResult ?? new SessionResult.NoAction();
		}

		public async Task<SessionResult> GetSuggestionResultAsync(
			IUser user,
			IEnumerable<SessionDetails> joinableSessions,
			int? durationMinutes,
			(string[]? gameIds, string? description, int? playerCount)? searchParams,
			bool allowCancel = false,
			ulong? serverId = null
		) {
			if (searchParams != null && serverId == null) {
				throw new ArgumentNullException("ServerId cannot be null for search with searchParams!");
			}

			var guid = Guid.NewGuid();
			var structure = MessageStructures.GetSuggestionStructure(
				joinables: joinableSessions,
				userId: user.Id,
				searchId: searchParams != null
					? guid
					: null,
				allowCancel: allowCancel
			);
			IUserMessage? message;
			try {
				message = await user.SendMessageAsync(
					text: structure.content,
					components: structure.components
				);
			} catch (HttpException e) {
				if (e.DiscordCode == DiscordErrorCode.CannotSendMessageToUser) {
					return new SessionResult.Exception();
				}
				if (e.DiscordCode == DiscordErrorCode.OpeningDMTooFast) {
					return new SessionResult.Exception(ExceptionReason.TooManyDMs);
				}
				ExceptionHandling.ReportExceptionToFile(e);
				return new SessionResult.Exception();
			}
			if (message == null) {
				return new SessionResult.Exception();
			}

			var suggestion = new SuggestionMessage(
				client: _client,
				matchmaker: _matchmaker,
				message: message,
				joinableSessions: joinableSessions,
				creatorId: user.Id,
				serverId: serverId ?? 0,
				durationMinutes: durationMinutes,
				searchParams: searchParams,
				id: guid,
				allowCancel: allowCancel
			);
			suggestion.MessageSemaphore.Release();

			await suggestion.SuggestionSignal.WaitAsync();
			return suggestion.ExitResult ?? new SessionResult.NoAction();
		}

		public async Task<SearchMessage?> CreateSearchMessageAsync(
			ulong channelId,
			IEnumerable<SessionDetails> searches,
			ulong? creatorId = null,
			IUserMessage? replyMessage = null
		) {
			var channel = await _client.GetChannelAsync(channelId);
			if (channel is not IMessageChannel messageChannel) { return null; }

			return await CreateSearchMessageAsync(messageChannel, searches, creatorId, replyMessage);
		}

		public async Task<SearchMessage?> CreateSearchMessageAsync(
			IMessageChannel messageChannel,
			IEnumerable<SessionDetails> searches,
			ulong? creatorId = null,
			IUserMessage? replyMessage = null
		) {
			var structure = MessageStructures.GetWaitingStructure(searches, null);
			IUserMessage? message = null;
			try {
				message = await messageChannel.SendMessageAsync(structure.content, components: structure.components);
			} catch { }
			if (message == null) { return null; }

			var searchMessage = await CreateSearchMessageAsync(message, searches, creatorId);
			if (replyMessage != null) { searchMessage.ReplyMessage = replyMessage; }
			return searchMessage;
		}

		public async Task<SearchMessage?> CreateSearchMessageAsync(
			ulong channelId,
			IEnumerable<SessionDetails> searches,
			ulong? messageId,
			ulong? messageChannelId,
			ulong? creatorId = null
		) {
			if (messageId == null || messageChannelId == null) { return await CreateSearchMessageAsync(channelId, searches, creatorId); }
			var channel = await _client.GetChannelAsync((ulong)messageChannelId);
			if (channel is not IMessageChannel messageChannel) { return await CreateSearchMessageAsync(channelId, searches, creatorId); }
			var message = await messageChannel.GetMessageAsync((ulong)messageId);
			if (message is not IUserMessage userMessage) { return await CreateSearchMessageAsync(channelId, searches, creatorId); }

			return await CreateSearchMessageAsync(channelId, searches, creatorId, replyMessage: userMessage);
		}

		public async Task<SearchMessage> CreateSearchMessageAsync(
			IUserMessage message,
			IEnumerable<SessionDetails> searches,
			ulong? creatorId = null
		) {
			await _discordContext.AddSessionConnectionAsync(message.Channel.Id, message.Id, creatorId, searches.Select(s => s.SessionId).ToArray());
			var searchMessage = new SearchMessage(_client, _matchmaker, _discordContext, message, searches, creatorId, creatorId == null);
			SearchMessageCreated?.Invoke(searchMessage);
			return searchMessage;
		}

		public async Task CreatePreExistingSearchMessagesAsync() {
			var connections = await _discordContext.GetAllSessionConnectionsAsync();

			if (connections == null || !connections.Any()) { return; }

			var channels = connections
				.Select(c => c.ChannelId)
				.Distinct()
				.ToDictionary(id => id, id => (IMessageChannel?)_client.GetChannel(id));

			var messageTasks = connections
				.Select(c => (channel: channels[c.ChannelId], messageId: c.MessageId))
				.ToDictionary(
					pair => pair.messageId,
					pair => pair.channel == null
						? Task.FromResult<IMessage?>(null)
						: pair.channel.GetMessageAsync(pair.messageId)
				);

			if (messageTasks == null || !messageTasks.Any()) { return; }

			var activeSessions = (await _matchmaker.GetSessionsAsync(connections.SelectMany(c => c.SessionIds).Distinct().ToArray()))
				.ToDictionary(s => s.SessionId, s => s);

			var sessions = connections
				.SelectMany(c => c.SessionIds)
				.Distinct()
				.ToDictionary(id => id, id => activeSessions?.GetValueOrDefault(id));

			foreach (var connection in connections) {
				var message = (IUserMessage?)await messageTasks[connection.MessageId];
				if (message == null) {
					await _discordContext.DeleteSessionConnectionAsync(connection);
					continue;
				}

				var existingSessions = connection.SessionIds
					.Select(id => sessions.GetValueOrDefault(id))
					.Where(s => s != null)
					.Select(s => s!);

				var nonExistingSessions = connection.SessionIds
					.Where(id => sessions.GetValueOrDefault(id) == null);

				if (nonExistingSessions.Any()) {
					await _discordContext.DeleteSessionConnectionAsync(connection.MessageId, nonExistingSessions.ToArray());
				}

				if (!existingSessions.Any()) { continue; }

				var searchMessage = new SearchMessage(_client, _matchmaker, _discordContext, message, existingSessions, connection.UserId, deleteOnStop: connection.UserId == null);
				searchMessage.ForceMessageUpdate();
			}
		}
		public async void HandleSessionChanged(IEnumerable<SessionDetails> updated, Dictionary<Guid, SessionStopReason> stopped) {
			try {
				if (!stopped.Any()) { return; }

				await _discordContext.DeleteSessionConnectionAsync(stopped.Keys);
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
			}
		}
	}
}
