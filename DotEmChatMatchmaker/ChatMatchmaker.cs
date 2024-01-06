using DotemMatchmaker;
using DotemModel;
using SearchParameters = (string gameId, int playerCount, string? description);


namespace DotemChatMatchmaker {
	public class ChatMatchmaker {

		private readonly Matchmaker _matchmaker;
		
		private int DefaultPlayerCount { get; }
		public int DefaultDurationMinutes { get; }
		public ChatGameNameHandler GameNameHandler { get; }

		public event Matchmaker.SessionChangedEvent? SessionChanged;

		public ChatMatchmaker(ChatGameNameHandler gameNameHandler, Matchmaker? matchmaker = null,  int defaultPlayerCount = 2, int defaultDurationMinutes = 30) {
			GameNameHandler = gameNameHandler;
			DefaultPlayerCount = defaultPlayerCount;
			DefaultDurationMinutes = defaultDurationMinutes;

			_matchmaker = matchmaker ?? new Matchmaker(1);
			_matchmaker.SessionChanged += (added, updated, stopped) => SessionChanged?.Invoke(added, updated, stopped);
		}

		public async Task<SessionResult> SearchSessionAsync(string serverId, string userId, string? channelId, int? durationMinutes, string[]? gameIds, int? playerCount, string? description, bool allowSuggestions = true) {

			SearchParameters[]? searchAttempts;

			//TODO: Per game default playercount

			if (gameIds != null) {
				searchAttempts = gameIds.Select(id => (GameNameHandler.GetGameIdAlias(id), playerCount ?? DefaultPlayerCount, description)).ToArray();
			} else {
				if (channelId == null) return new SessionResult.NoAction();

				var defaults = GetChannelDefaultSearchAttempts(channelId);
				searchAttempts = defaults?.Select(attempt => (GameNameHandler.GetGameIdAlias(attempt.gameId), playerCount ?? attempt.playerCount, description ?? attempt.description)).ToArray();
			}

			if (searchAttempts == null) return new SessionResult.NoAction();

			var expireTime = DateTime.Now.AddMinutes(durationMinutes ?? DefaultDurationMinutes);
			return await _matchmaker.SearchSessionAsync(serverId, userId, expireTime, allowSuggestions, searchAttempts);
		}

		public async Task<SessionDetails[]> LeaveSessionsAsync(string userId, params Guid[] searchIds)
			=> await _matchmaker.LeaveSessionsAsync(userId, searchIds);
		public async Task<SessionDetails[]> LeaveSessionsAsync(string serverId, string userId, params string[] gameIds) 
			=> await _matchmaker.LeaveSessionsAsync(serverId, userId, gameIds);
		public async Task<SessionDetails[]> LeaveAllPlayerSessionsAsync(string serverId, string userId)
			=> await _matchmaker.LeaveAllPlayerSessionsAsync(serverId, userId);



		public async Task<SessionResult> TryJoinSessionAsync(string userId, Guid searchId, int? durationMinutes = null) {
			var expireTime = DateTime.Now.AddMinutes(durationMinutes ?? DefaultDurationMinutes);
			return await _matchmaker.TryJoinSessionAsync(userId, searchId, expireTime);
		}

		private (string gameId, int playerCount, string? description)[]? GetChannelDefaultSearchAttempts(string channelId) => null;

	}
}