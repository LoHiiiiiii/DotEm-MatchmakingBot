using DotemMatchmaker;
using DotemModel;

namespace DotemChatMatchmaker {
	public class ChatMatchmaker {

		private readonly ChatGameNameHandler _gameNameHandler;
		private readonly Matchmaker _matchmaker;
		private readonly int _defaultPlayerCount;
		private readonly int _defaultDurationMinutes;

		public event Matchmaker.SearchChangedEvent? SearchChanged;

		public ChatMatchmaker(ChatGameNameHandler gameNameHandler, Matchmaker? matchmaker = null,  int defaultPlayerCount = 2, int defaultDurationMinutes = 30) {
			_gameNameHandler = gameNameHandler;
			_defaultPlayerCount = defaultPlayerCount;
			_defaultDurationMinutes = defaultDurationMinutes;


			_matchmaker = matchmaker ?? new Matchmaker(1);
			_matchmaker.SearchChanged += (sender, added, updated, stopped) => SearchChanged?.Invoke(this, added, updated, stopped);
		}

		public async Task<SearchResult> SearchAsync(string serverId, string userId, string? channelId, int? durationMinutes, string[]? gameIds, int? playerCount, string? description, bool allowSuggestions = true) {

			(string gameId, int playerCount, string? description)[]? searchAttempts;

			if (gameIds != null) {
				searchAttempts = gameIds.Select(id => (_gameNameHandler.GetGameIdAlias(id), playerCount ?? _defaultPlayerCount, description)).ToArray();
			} else {
				if (channelId == null) return new SearchResult.NoSearch();

				var defaults = GetChannelDefaultSearchAttempts(channelId);
				searchAttempts = defaults?.Select(attempt => (_gameNameHandler.GetGameIdAlias(attempt.gameId), playerCount ?? attempt.playerCount, description ?? attempt.description)).ToArray();
			}

			if (searchAttempts == null) return new SearchResult.NoSearch();

			var expireTime = DateTime.Now.AddMinutes(durationMinutes ?? _defaultDurationMinutes);
			return await _matchmaker.SearchAsync(serverId, userId, expireTime, allowSuggestions, searchAttempts);
		}

		public async Task<SearchDetails[]> CancelSearchesAsync(params Guid[] searchIds) => await _matchmaker.CancelSearchesAsync(searchIds);

		public async Task<SearchResult> TryAcceptMatchAsync(string userId, Guid searchId) => await _matchmaker.TryAcceptMatchAsync(userId, searchId);

		private (string gameId, int playerCount, string? description)[]? GetChannelDefaultSearchAttempts(string channelId) => null;

	}
}