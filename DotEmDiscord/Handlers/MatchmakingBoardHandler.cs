using DotemMatchmaker;
using DotemModel;
using DotemChatMatchmaker;

namespace DotemDiscord.Handlers {
	public class MatchmakingBoardHandler {

		public readonly ExtensionContext _extensionContext;
		public readonly Matchmaker _matchmaker;
		public readonly ButtonMessageHandler _buttonMessageHandler;

		public MatchmakingBoardHandler(ExtensionContext extensionContext, Matchmaker matchmaker, ButtonMessageHandler buttonMessageHandler) {
			_extensionContext = extensionContext;
			_matchmaker = matchmaker;
			_buttonMessageHandler = buttonMessageHandler;
			_matchmaker.SessionChanged += HandleNewSearchMessages;
		}

		public async Task PostActiveMatchesAsync(ulong serverId, ulong channelId) {
			var activeSearches = await _matchmaker.GetAllSessionsAsync();
			var stringServerId = serverId.ToString();
			foreach (var session in activeSearches) {
				if (session.ServerId != stringServerId) { continue; }
				await _buttonMessageHandler.CreateSearchMessageAsync(channelId, [session]);
			}
		}

		private async void HandleNewSearchMessages(IEnumerable<SessionDetails> added, IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped) {
			if (!added.Any()) { return; }

			var boards = (await _extensionContext.GetMatchmakingBoardsAsync())
				.Select<(string serverId, string channelId), (string serverId, ulong? channelId)> (pair => (
					pair.serverId,
					ulong.TryParse(pair.channelId, out var c) ? c : null
				))
				.Where(pair => pair.channelId != null)
				.GroupBy(pair => pair.serverId)
				.ToDictionary(grouping => grouping.Key, grouping => grouping.Select(pair => (ulong)pair.channelId!));

			if (boards == null || !boards.Any()) { return; }

			foreach(var session in added) {
				if (!boards.ContainsKey(session.ServerId)) { continue; }
				foreach (var channel in boards[session.ServerId]) {
					await _buttonMessageHandler.CreateSearchMessageAsync(channel, [session]);
				}
			}
		}
	}
}
