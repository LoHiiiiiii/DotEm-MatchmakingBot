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
			(string[]? gameIds, string? description, int? playerCount)? searchParams
		) {
			var guid = Guid.NewGuid();
			var structure = MessageStructures.GetSuggestionStructure(
				joinables: joinableSessions,
				userId: interaction.User.Id,
				searchId: searchParams != null
					? guid
					: null
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
				id: guid
			);
			suggestion.MessageSemaphore.Release();

			var cts = new CancellationTokenSource();
			SuggestionTimeout(timeOutMinutes, cts, suggestion);

			await suggestion.SuggestionSignal.WaitAsync(cts.Token);
			return suggestion.ExitResult ?? new SessionResult.NoAction();
		}

		public async void SuggestionTimeout(int durationMinutes, CancellationTokenSource tokenSource, SuggestionMessage suggestionMessage) {
			await Task.Delay(durationMinutes * 60 * 1000);
			if (suggestionMessage.Released) { return; }
			tokenSource.Cancel();
		}

		public async Task<SessionResult> GetSuggestionResultAsync(
			IUser user,
			IEnumerable<SessionDetails> joinableSessions,
			int? durationMinutes,
			(string[]? gameIds, string? description, int? playerCount)? searchParams
		) {
			var guid = Guid.NewGuid();
			var structure = MessageStructures.GetSuggestionStructure(
				joinables: joinableSessions,
				userId: user.Id,
				searchId: searchParams != null
					? guid
					: null
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
				id: guid
			);
			suggestion.MessageSemaphore.Release();

			await suggestion.SuggestionSignal.WaitAsync();
			return suggestion.ExitResult ?? new SessionResult.NoAction();
		}


		public async Task<SearchMessage> CreateSearchMessageAsync(
			IUserMessage message,
			IEnumerable<SessionDetails> searches,
			ulong creatorId
		) {
			await _discordContext.AddSessionConnectionAsync(message.Channel.Id, message.Id, creatorId, searches.Select(s => s.SessionId).ToArray());
			return new SearchMessage(_client, _matchmaker, _discordContext, message, searches, creatorId);
		}

		public async Task CreatePreExistingSearchMessagesAsync() {
			var connections = await _discordContext.GetSessionConenctionsAsync();

			if (connections == null || !connections.Any()) { return; }

			var channels = connections
				.Select(c => c.ChannelId)
				.ToDictionary(id => id, id => (IMessageChannel?)_client.GetChannel(id));

			var messageTasks = connections
				.Select(c => (channel: channels[c.ChannelId], messageId: c.MessageId))
				.ToDictionary(
					pair => pair.messageId,
					pair => pair.channel == null
						? Task.FromResult<IMessage?>(null)
						: pair.channel.GetMessageAsync(pair.messageId)
				);

			if (messageTasks == null) { return; }

			var activeSessions = (await _matchmaker.GetSessionsAsync(connections.SelectMany(c => c.SessionIds).ToArray()))
				.ToDictionary(s => s.SessionId, s => s);

			var sessions = connections
				.SelectMany(c => c.SessionIds)
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
					.Where(id => sessions.GetValueOrDefault(id) == null)
					.ToArray();

				if (nonExistingSessions.Any()) {
					await _discordContext.DeleteSessionConnectionAsync(connection.MessageId, nonExistingSessions.ToArray());
				}

				if (!existingSessions.Any()) { continue; }

				new SearchMessage(_client, _matchmaker, _discordContext, message, existingSessions, connection.UserId);
			}
		}
	}
}
