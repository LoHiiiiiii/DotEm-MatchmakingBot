using DotemMatchmaker;
using DotemModel;
using DotemExtensions;
using DotemDiscord.ButtonMessages;
using DotemDiscord.Context;
using DotemDiscord.Utils;

namespace DotemDiscord.Handlers {
	public class MatchmakingBoardHandler {

		public readonly DiscordContext _discordContext;
		public readonly ExtensionContext _extensionContext;
		public readonly Matchmaker _matchmaker;
		public readonly ButtonMessageHandler _buttonMessageHandler;

		private Dictionary<Guid, SessionDetails> sessionsToPostToBoard = new();
		private SemaphoreSlim postBoardSemaphore = new SemaphoreSlim(1, 1);

		public MatchmakingBoardHandler(DiscordContext discordContext, ExtensionContext extensionContext, Matchmaker matchmaker, ButtonMessageHandler buttonMessageHandler) {
			_discordContext = discordContext;
			_extensionContext = extensionContext;
			_matchmaker = matchmaker;
			_matchmaker.SessionAdded += HandleSessionAdded;
			_buttonMessageHandler = buttonMessageHandler;
			_buttonMessageHandler.SearchMessageCreated += HandleNewSearchMessages;
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
			await postBoardSemaphore.WaitAsync();
			try {

				if (!sessionsToPostToBoard.Any()) { return; }

				if (!searchMessage.Searches.Values.Any(sd => sessionsToPostToBoard.ContainsKey(sd.SessionId))) { return; }


				var existing = await _matchmaker.GetSessionsAsync(sessionsToPostToBoard.Keys.ToArray());

				sessionsToPostToBoard = existing.ToDictionary(sd => sd.SessionId);

				var postables = searchMessage.Searches.Values.Where(sd => sessionsToPostToBoard.ContainsKey(sd.SessionId));

				if (!postables.Any()) { return; }


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
					sessionsToPostToBoard.Remove(session.SessionId);
					if (!boards.ContainsKey(session.ServerId)) { continue; }
					foreach (var channel in boards[session.ServerId]) {
						await _buttonMessageHandler.CreateSearchMessageAsync(channel, [session], replyMessage: searchMessage.Message);
					}
				}
			}
			catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
			} finally {
				postBoardSemaphore.Release();
			}
		}

		public async void HandleSessionAdded(IEnumerable<SessionDetails> added) {
			await postBoardSemaphore.WaitAsync();
			try {
				if (!added.Any()) { return; }

				foreach (var session in added) {
					if (sessionsToPostToBoard.ContainsKey(session.SessionId)) { continue; }
					sessionsToPostToBoard.Add(session.SessionId, session);
				}
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
			}
			finally {
				postBoardSemaphore.Release();
			}
		}
	}
}
