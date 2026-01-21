using DotemMatchmaker;
using DotemModel;
using DotemExtensions;
using DotemDiscord.ButtonMessages;
using DotemDiscord.Context;
using DotemDiscord.Utils;
using Discord.WebSocket;
using Discord.Commands;
using Discord;

namespace DotemDiscord.Handlers {
	public class SearchPropagationHandler {

		public readonly DiscordContext _discordContext;
		public readonly ExtensionContext _extensionContext;
		public readonly Matchmaker _matchmaker;
		public readonly ButtonMessageHandler _buttonMessageHandler;
		public readonly DiscordSocketClient _client;

		private Dictionary<Guid, SessionDetails> sessionsToHandle = new();
		private SemaphoreSlim propagationSemaphore = new SemaphoreSlim(1, 1);

		public SearchPropagationHandler(DiscordContext discordContext, ExtensionContext extensionContext, Matchmaker matchmaker, ButtonMessageHandler buttonMessageHandler, DiscordSocketClient client) {
			_discordContext = discordContext;
			_extensionContext = extensionContext;
			_matchmaker = matchmaker;
			_matchmaker.SessionAdded += HandleSessionAdded;
			_buttonMessageHandler = buttonMessageHandler;
			_buttonMessageHandler.SearchMessageCreated += HandleNewSearchMessages;
			_client = client;
		}

		public async Task PostActiveMatchesAsync(ulong serverId, ulong channelId) {
			var activeSearches = await _matchmaker.GetAllSessionsAsync();
			var stringServerId = serverId.ToString();
			var serverMessages = await _discordContext.GetAllSessionConnectionsAsync();
			var boards = (await _extensionContext.GetMatchmakingBoardsAsync(serverId.ToString()))
				.Distinct()
				.ToHashSet();
			serverMessages = serverMessages.Where(sm => !boards.Contains(sm.ChannelId.ToString()));
			foreach (var session in activeSearches) {
				if (session.ServerId != stringServerId) { continue; }
				var serverMessage = serverMessages
					.Where(sm => sm.SessionIds.ToHashSet().Contains(session.SessionId))
					.FirstOrDefault();
				await _buttonMessageHandler.CreateSearchMessageAsync(channelId, [session], messageId: serverMessage?.MessageId, serverMessage?.ChannelId);
			}
		}

		private async void HandleNewSearchMessages(SearchMessage searchMessage) {
			await propagationSemaphore.WaitAsync();
			try {
				if (!sessionsToHandle.Any()) { return; }

				var existing = await _matchmaker.GetSessionsAsync(sessionsToHandle.Keys.ToArray());

				sessionsToHandle = existing.ToDictionary(sd => sd.SessionId);

				var postables = searchMessage.Searches.Values.Where(sd => sessionsToHandle.ContainsKey(sd.SessionId));

				if (!postables.Any()) { return; }
				await PostToMatchmakingBoardsAsync(searchMessage.Message, postables);
				await PostToDefaultChannelsAsync(searchMessage.Message, postables, searchMessage.CreatorId);
				foreach (var session in postables) {
					if (sessionsToHandle.ContainsKey(session.SessionId)) {
						sessionsToHandle.Remove(session.SessionId);
					}
				}
			}
			catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
			} finally {
				propagationSemaphore.Release();
			}
		}

		private async Task PostToMatchmakingBoardsAsync(IUserMessage message, IEnumerable<SessionDetails> postables) {

			var boards = (await _extensionContext.GetMatchmakingBoardsAsync())
				.Select<(string serverId, string channelId), (string serverId, ulong? channelId)>(pair => (
					pair.serverId,
					ulong.TryParse(pair.channelId, out var c) ? c : null
				))
				.Where(pair => pair.channelId != null)
				.GroupBy(pair => pair.serverId)
				.ToDictionary(grouping => grouping.Key, grouping => grouping.Select(pair => (ulong)pair.channelId!));

			if (boards == null || !boards.Any()) { return; }

			foreach (var session in postables) {
				if (!boards.ContainsKey(session.ServerId)) { continue; }
				foreach (var channel in boards[session.ServerId]) {
					await _buttonMessageHandler.CreateSearchMessageAsync(channel, [session], replyMessage: message);
				}
			}
		}
		private async Task PostToDefaultChannelsAsync(IUserMessage message, IEnumerable<SessionDetails> postables, ulong? creatorId) {
			if (creatorId == null) { return;  }
			if (message.Channel is not IGuildChannel searchChannel) { return; }
			foreach (var session in postables) {
				var channelIds = await _extensionContext.GetGameDefaultChannelsAsync(session.GameId);

				foreach (var channelId in channelIds) {
					if (!ulong.TryParse(channelId, out var id)) { continue; }
					if (id == message.Channel.Id) { continue; }

					var channel = await _client.GetChannelAsync(id);
					if (channel is not IGuildChannel guildChannel) { continue; }
					if (guildChannel.GuildId != searchChannel.GuildId) { continue; }
					if (guildChannel is not IMessageChannel messageChannel) { continue; }
					await _buttonMessageHandler.CreateSearchMessageAsync(messageChannel, [session], creatorId: creatorId);
				}
			}
		}

		public async void HandleSessionAdded(IEnumerable<SessionDetails> added) {
			await propagationSemaphore.WaitAsync();
			try {
				if (!added.Any()) { return; }

				foreach (var session in added) {
					if (sessionsToHandle.ContainsKey(session.SessionId)) { continue; }
					sessionsToHandle.Add(session.SessionId, session);
				}
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
			}
			finally {
				propagationSemaphore.Release();
			}
		}
	}
}
