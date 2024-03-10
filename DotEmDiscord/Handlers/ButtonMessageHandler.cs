using Discord.WebSocket;
using Discord;
using DotemMatchmaker;
using DotemDiscord.ButtonMessages;
using DotemDiscord.Utils;
using DotemModel;
using DotemDiscord.Context;

namespace DotemDiscord.Handlers {
	public class ButtonMessageHandler {

		private readonly int timeOutMinutes = 15;

		public readonly DiscordContext _discordContext;
		public readonly Matchmaker _matchmaker;
		public readonly DiscordSocketClient _client;

		public ButtonMessageHandler(DiscordContext discordContext, Matchmaker matchmaker, DiscordSocketClient client) {
			_discordContext = discordContext;
			_matchmaker = matchmaker;
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
			var suggestion = new SuggestionMessage(
				client: _client,
				matchmaker: _matchmaker,
				message: followup,
				joinableSessions: joinableSessions,
				creatorId: interaction.User.Id,
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
			ulong userId,
			IEnumerable<SessionDetails> joinableSessions,
			int? durationMinutes,
			(string[]? gameIds, string? description, int? playerCount)? searchParams,
			bool allowCancel = false
		) {
			var user = await _client.GetUserAsync(userId);
			if (user == null) return new SessionResult.NoAction();
			return await GetSuggestionResultAsync(user, joinableSessions, durationMinutes, searchParams, allowCancel);
		}

		public async Task<SessionResult> GetSuggestionResultAsync(
			IUser user,
			IEnumerable<SessionDetails> joinableSessions,
			int? durationMinutes,
			(string[]? gameIds, string? description, int? playerCount)? searchParams,
			bool allowCancel = false
		) {
			var guid = Guid.NewGuid();
			var structure = MessageStructures.GetSuggestionStructure(
				joinables: joinableSessions,
				userId: user.Id,
				searchId: searchParams != null
					? guid
					: null,
				allowCancel: allowCancel
			);
			var dm = await user.SendMessageAsync(
				text: structure.content,
				components: structure.components
			);
			var suggestion = new SuggestionMessage(
				client: _client,
				matchmaker: _matchmaker,
				message: dm,
				joinableSessions: joinableSessions,
				creatorId: user.Id,
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
			ulong? creatorId = null
		) {
			var channel = await _client.GetChannelAsync(channelId);
			if (channel is not IMessageChannel) { return null; }

			var structure = MessageStructures.GetWaitingStructure(searches, null);
			var message = await ((IMessageChannel)channel).SendMessageAsync(structure.content, components: structure.components);
			if (message == null) { return null; }
			
			return await CreateSearchMessageAsync(message, searches, creatorId);
		}

		public async Task<SearchMessage> CreateSearchMessageAsync(
			IUserMessage message,
			IEnumerable<SessionDetails> searches,
			ulong? creatorId = null
		) {
			await _discordContext.AddSessionConnectionAsync(message.Channel.Id, message.Id, creatorId, searches.Select(s => s.SessionId).ToArray());
			return new SearchMessage(_client, _matchmaker, _discordContext, message, searches, creatorId, creatorId == null);
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

				new SearchMessage(_client, _matchmaker, _discordContext, message, existingSessions, connection.UserId, deleteOnStop: connection.UserId == null);
			}
		}
	}
}
