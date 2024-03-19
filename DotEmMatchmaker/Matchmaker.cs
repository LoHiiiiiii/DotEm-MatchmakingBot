using DotemMatchmaker.Context;
using DotemModel;
using SearchParameters = (string gameId, int playerCount, string? description);

namespace DotemMatchmaker {
	public class Matchmaker {

		public int ExpireClearIntervalMilliseconds { get; }
		public int DefaultMaxPlayerCount { get; }
		public int DefaultJoinDurationMinutes { get; }

		private readonly MatchmakingContext _context;

		public Matchmaker(
			MatchmakingContext context,
			int expireClearIntervalMinutes = 1,
			int defaultMaxPlayerCount = 2,
			int defaultDurationMinutes = 30
		) {
			_context = context;
			DefaultMaxPlayerCount = defaultMaxPlayerCount;
			DefaultJoinDurationMinutes = defaultDurationMinutes;
			ExpireClearIntervalMilliseconds = expireClearIntervalMinutes * 1000 * 60;
		}

		public delegate void SessionChangedEvent(IEnumerable<SessionDetails> added, IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped);

		public event SessionChangedEvent? SessionChanged;

		private SemaphoreSlim sessionSemaphore = new SemaphoreSlim(1, 1);

		public async Task<SessionResult> SearchSessionAsync(string serverId, string userId, DateTimeOffset expireTime,
			bool allowSuggestions = true, params SearchParameters[] searchParameters
		) {
			if (!searchParameters.Any()) return new SessionResult.NoAction();

			await sessionSemaphore.WaitAsync();
			try {
				var clear = await _context.ClearExpiredJoinsAsync();

				var addedSessions = new List<SessionDetails>();
				var updatedSessions = clear.updated.ToList();
				var stoppedSessions = clear.stopped.ToList();

				try {
					var aliases = await _context.GetGameAliasesAsync(serverId, searchParameters.Select(s => s.gameId).ToArray());
					var uniqueSearches = searchParameters
						.Select(attempt => (gameId: aliases[attempt.gameId], attempt.playerCount, attempt.description))
						.Distinct()
						.ToHashSet();
					var matchingGames = await _context.GetUserJoinableSessionsAsync(serverId, userId, aliases.Values);
					if (matchingGames.Any()) {
						var playableExactMatches = matchingGames.Where(match =>
							match.UserExpires.Count == match.MaxPlayerCount - 1
							&& uniqueSearches.Contains((match.GameId, match.MaxPlayerCount, match.Description))
						);

						if (playableExactMatches.Any()) {
							var restOfPlayables = matchingGames.Where(match =>
								match.UserExpires.Count == match.MaxPlayerCount - 1
								&& !uniqueSearches.Contains((match.GameId, match.MaxPlayerCount, match.Description)));

							var exact = playableExactMatches.First(); // Not single because there might be two exact with a description

							if (playableExactMatches.All(match =>
									match.GameId == exact.GameId
									&& match.MaxPlayerCount == exact.MaxPlayerCount
									&& match.Description == exact.Description
								)
								&& !restOfPlayables.Any() && exact.Description == null
							) {
								// Immediately match only if no descriptions and one type of exact match
								var joined = await _context.JoinSessionAsync(exact.SessionId, userId, expireTime);
								if (joined != null) {

									(var updated, var stopped) = await _context.LeaveAllSessionsAsync(joined.UserExpires.Keys.ToArray());
									if (updated.Any()) updatedSessions.AddRange(updated);
									if (stopped.Any()) stoppedSessions.AddRange(stopped);

									return new SessionResult.Matched(joined);
								}
							}

							var hasExactDescriptionless = playableExactMatches.Any(match => match.Description == null);

							if (allowSuggestions || hasExactDescriptionless) {
								return new SessionResult.Suggestions(
									suggestedSessions: playableExactMatches.Concat(restOfPlayables),
									allowWait: !hasExactDescriptionless
								);
							}
						} else if (allowSuggestions) {
							return new SessionResult.Suggestions(
								suggestedSessions: matchingGames,
								allowWait: true
							);
						}
					}

					// No suggestions or exact descriptionless matches

					var waitingSessions = new List<SessionDetails>();
					var joinedSessions = await _context.GetUserExistingSessionsAsync(serverId, userId);

					foreach (var attempt in uniqueSearches) {
						var existingSession = joinedSessions
							.Where(match =>
								match.GameId == attempt.gameId
								&& match.MaxPlayerCount == attempt.playerCount
								&& match.Description == attempt.description)
							.SingleOrDefault();

						if (existingSession != null) {
							var updated = await _context.JoinSessionAsync(existingSession.SessionId, userId, expireTime);
							if (updated == null) continue;
							waitingSessions.Add(updated);
							updatedSessions.Add(updated);
						} else {
							var newSearch = await _context.CreateSessionAsync(
								serverId: serverId,
								userId: userId,
								gameId: attempt.gameId,
								maxPlayerCount: attempt.playerCount,
								description: attempt.description,
								expireTime: expireTime
							);
							waitingSessions.Add(newSearch);
							addedSessions.Add(newSearch);
						}
					}

					return new SessionResult.Waiting(waitingSessions);
				} finally {
					if (addedSessions.Any() || updatedSessions.Any() || stoppedSessions.Any()) {
						SessionChanged?.Invoke(
							added: addedSessions,
							updated: updatedSessions,
							stopped: stoppedSessions
						);
					}
				}
			} finally { sessionSemaphore.Release(); }
		}

		public async Task<SessionResult> TryJoinSessionAsync(string userId, Guid sessionId, DateTimeOffset expireTime) {
			await sessionSemaphore.WaitAsync();
			try {
				var clear = await _context.ClearExpiredJoinsAsync();

				var updatedSessions = clear.updated.ToList();
				var stoppedSessions = clear.stopped.ToList();
				try {

					var session = await _context.JoinSessionAsync(sessionId, userId, expireTime);
					if (session == null) return new SessionResult.FailedToJoin();

					if (session.UserExpires.Count < session.MaxPlayerCount) {
						updatedSessions.Add(session);
						return new SessionResult.Waiting([session]);
					}

					(var updated, var stopped) = await _context.LeaveAllSessionsAsync(session.UserExpires.Keys.ToArray());
					if (updated.Any()) updatedSessions.AddRange(updated);
					if (stopped.Any()) stoppedSessions.AddRange(stopped);
					return new SessionResult.Matched(session);
				} finally {
					if (updatedSessions.Any() || stoppedSessions.Any()) {
						SessionChanged?.Invoke(
							added: [],
							updated: updatedSessions,
							stopped: stoppedSessions
						);
					}
				}
			} finally { sessionSemaphore.Release(); }
		}

		public async Task ClearExpiredJoinsAsync() {
			await sessionSemaphore.WaitAsync();
			try {
				var (updatedSessions, stoppedSessions) = await _context.ClearExpiredJoinsAsync();
				if (!updatedSessions.Any() && !stoppedSessions.Any()) { return; }
				SessionChanged?.Invoke(
					added: [],
					updated: updatedSessions,
					stopped: stoppedSessions
				);
			} finally { sessionSemaphore.Release(); }
		}

		#region Leaving Sessions
		public async Task<(IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped)> LeaveSessionsAsync(string userId, params Guid[] sessionIds) {
			await sessionSemaphore.WaitAsync();
			try {
				(var updatedMatches, var stoppedMatches) = await _context.LeaveSessionsAsync(userId, sessionIds);
				try {
					return (updatedMatches, stoppedMatches);
				} finally {
					if (updatedMatches.Any() || stoppedMatches.Any()) {
						SessionChanged?.Invoke(
							added: [],
							updated: updatedMatches ?? [],
							stopped: stoppedMatches ?? []
						);
					}
				}
			} finally { sessionSemaphore.Release(); }
		}

		public async Task<(IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped)> LeaveGamesAsync(string serverId, string userId, params string[] gameIds) {
			await sessionSemaphore.WaitAsync();
			try {
				(var updatedMatches, var stoppedMatches) = await _context.LeaveGamesAsync(userId, serverId, gameIds);
				try {
					return (updatedMatches, stoppedMatches);
				} finally {
					if (updatedMatches.Any() || stoppedMatches.Any()) {
						SessionChanged?.Invoke(
							added: [],
							updated: updatedMatches ?? [],
							stopped: stoppedMatches ?? []
						);
					}
				}
			} finally { sessionSemaphore.Release(); }
		}

		public async Task<(IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped)> LeaveAllPlayerSessionsAsync(string serverId, string userId) {
			await sessionSemaphore.WaitAsync();
			try {
				(var updatedMatches, var stoppedMatches) = await _context.LeaveAllSessionsAsync(userId);
				try {
					return (updatedMatches, stoppedMatches);
				} finally {
					if (updatedMatches.Any() || stoppedMatches.Any()) {
						SessionChanged?.Invoke(
							added: [],
							updated: updatedMatches ?? [],
							stopped: stoppedMatches ?? []
						);
					}
				}
			} finally { sessionSemaphore.Release(); }
		}

		#endregion

		#region Session Getting
		public async Task<IEnumerable<SessionDetails>> GetSessionsAsync(params Guid[] ids) {
			await sessionSemaphore.WaitAsync();
			try { return await _context.GetSessionsAsync(ids); } finally { sessionSemaphore.Release(); }
		}

		public async Task<IEnumerable<SessionDetails>> GetAllSessionsAsync() {
			await sessionSemaphore.WaitAsync();
			try { return await _context.GetAllSessionsAsync(); } finally { sessionSemaphore.Release(); }
		}

		public async Task<IEnumerable<SessionDetails>> GetUserSessionsAsync(string serverId, string userId) {
			await sessionSemaphore.WaitAsync();
			try { return await _context.GetUserExistingSessionsAsync(serverId, userId); } finally { sessionSemaphore.Release(); }
		}
		#endregion

		#region Alias Handling
		public async Task AddGameAliasAsync(string serverId, string aliasId, params string[] gameIds) {
			await sessionSemaphore.WaitAsync();
			try {
				var updated = await _context.AddGameAliasAsync(serverId, aliasId, gameIds);
				if (!updated.Any()) { return; }
				SessionChanged?.Invoke(
					added: [],
					updated: updated,
					stopped: []
					);
			} finally { sessionSemaphore.Release(); }
		}

		public async Task<Dictionary<string, string>> GetAllGameAliasesAsync(string serverId) {
			await sessionSemaphore.WaitAsync();
			try { return await _context.GetAllGameAliasesAsync(serverId); } finally { sessionSemaphore.Release(); }
		}

		public async Task<Dictionary<string, string>> GetGameAliasesAsync(string serverId, params string[] gameIds) {
			await sessionSemaphore.WaitAsync();
			try { return await _context.GetGameAliasesAsync(serverId, gameIds); } finally { sessionSemaphore.Release(); }
		}
		public async Task DeleteGameAliasesAsync(string serverId, params string[] gameIds) {
			await sessionSemaphore.WaitAsync();
			try { await _context.DeleteGameAliasesAsync(serverId, gameIds); } finally { sessionSemaphore.Release(); }
		}

		#endregion

		#region Game Name Handling
		public async Task AddGameNameAsync(string serverId, string gameName, string gameId) {
			await sessionSemaphore.WaitAsync();
			try {
				var updated = await _context.AddGameNameAsync(serverId, gameName, gameId);
				if (!updated.Any()) { return; }
				SessionChanged?.Invoke(
					added: [],
					updated: updated,
					stopped: []
					);
			} finally { sessionSemaphore.Release(); }
		}

		public async Task<Dictionary<string, string>> GetAllGameNamesAsync(string serverId) {
			await sessionSemaphore.WaitAsync();
			try { return await _context.GetAllGameNamesAsync(serverId); } finally { sessionSemaphore.Release(); }
		}

		public async Task<Dictionary<string, string>> GetGameNamesAsync(string serverId, params string[] gameIds) {
			await sessionSemaphore.WaitAsync();
			try { return await _context.GetGameNamesAsync(serverId, gameIds); } finally { sessionSemaphore.Release(); }
		}

		public async Task DeleteGameNamesAsync(string serverId, params string[] gameIds) {
			await sessionSemaphore.WaitAsync();
			try { await _context.DeleteGameNamesAsync(serverId, gameIds); } finally { sessionSemaphore.Release(); }
		}

		#endregion

		#region Overloads
		public async Task<SessionResult> SearchSessionAsync(
			string serverId,
			string userId,
			int? joinDuration = null,
			int? maxPlayerCount = null,
			string? description = null,
			bool allowSuggestions = true,
			params string[] gameIds
		) {
			var searchParams = gameIds
				.Select(id => (id, maxPlayerCount ?? DefaultMaxPlayerCount, description))
				.ToArray();

			return await SearchSessionAsync(
				serverId: serverId,
				userId: userId,
				expireTime: DateTime.Now.AddMinutes(joinDuration ?? DefaultJoinDurationMinutes),
				allowSuggestions: allowSuggestions,
				searchParameters: searchParams
			);
		}


		public async Task<SessionResult> TryJoinSessionAsync(string userId, Guid sessionId, int? joinDuration = null)
			=> await TryJoinSessionAsync(userId, sessionId, DateTimeOffset.Now.AddMinutes(joinDuration ?? DefaultJoinDurationMinutes));
		#endregion
	}
}