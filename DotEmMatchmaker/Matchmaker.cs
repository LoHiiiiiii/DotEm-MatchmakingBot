using DotemMatchmaker.Context;
using DotemModel;
using DotemMatchmaker.Utils;
using SearchParameters = (string gameId, int playerCount, string? description);
using System.Data;

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

		public delegate void SessionChangedEvent(IEnumerable<SessionDetails> updated, Dictionary<Guid, SessionStopReason> stopped);

		public event SessionChangedEvent? SessionChanged;

		public delegate void SessionAddedEvent(IEnumerable<SessionDetails> added);

		public event SessionAddedEvent? SessionAdded;

		private SemaphoreSlim sessionSemaphore = new SemaphoreSlim(1, 1);

		private async Task<SessionResult> SearchSessionAsync(string serverId, string userId, DateTimeOffset expireTime,
			bool allowSuggestions = true, params SearchParameters[] searchParameters
		) {
			if (!searchParameters.Any()) return new SessionResult.NoAction();

			await sessionSemaphore.WaitAsync();
			try {
				var clear = await _context.ClearExpiredJoinsAsync();

				var addedSessions = new List<SessionDetails>();
				var updatedSessions = clear.updated.ToList();
				var stoppedSessions = clear.stopped.ToDictionary(id => id, id => SessionStopReason.Expired);

				try {
					var aliases = await _context.GetGameAliasesAsync(serverId, searchParameters.Select(s => s.gameId).ToArray());
					var uniqueSearches = searchParameters
						.Select(attempt => (gameId: aliases[attempt.gameId.ToLowerInvariant()], attempt.playerCount, attempt.description))
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

							var exact = playableExactMatches.First();

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
									if (stopped.Any()) stoppedSessions.AddRange(stopped.Select(id =>
										(id, joined.SessionId == id ? SessionStopReason.Joined : SessionStopReason.JoinedOther))
									);

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

							var hasSuggestable = false;

							// Do not suggest if matching games have no descriptions and search has a description
							foreach (var search in uniqueSearches) {
								hasSuggestable = matchingGames
								   .Any(sd => sd.GameId == search.gameId
									   && (search.description == null || sd.Description != null));

								if (hasSuggestable) { break; }
							}

							if (hasSuggestable) {
								return new SessionResult.Suggestions(
									suggestedSessions: matchingGames,
									allowWait: true
								);
							}
						}
					}

					// No suggestions or exact descriptionless playable matches

					var waitingSessions = new List<SessionDetails>();
					var joinedSessions = await _context.GetUserExistingSessionsAsync(serverId, userId);
					var exactDescriptionlessNotPlayable = matchingGames
						.Where(match =>
							match.UserExpires.Count < match.MaxPlayerCount - 1
							&& match.Description == null
							&& uniqueSearches.Contains((match.GameId, match.MaxPlayerCount, match.Description)))
						.Distinct();

					foreach (var attempt in uniqueSearches) {

						var existingSession = joinedSessions
							.Where(match =>
								match.GameId == attempt.gameId
								&& match.MaxPlayerCount == attempt.playerCount
								&& match.Description == attempt.description)
							.FirstOrDefault();


						if (existingSession == null) {
							existingSession = exactDescriptionlessNotPlayable
							.Where(match =>
								match.GameId == attempt.gameId
								&& match.MaxPlayerCount == attempt.playerCount
								&& match.Description == attempt.description)
							.FirstOrDefault();
						}

						if (existingSession != null) {
							var updated = await _context.JoinSessionAsync(existingSession.SessionId, userId, expireTime);
							if (updated == null) continue;
							waitingSessions.Add(updated);
							updatedSessions.Add(updated);
							continue;
						}

						var newSearch = await _context.CreateSessionAsync(
							serverId: serverId,
							userId: userId,
							gameId: attempt.gameId,
							maxPlayerCount: attempt.playerCount,
							description: attempt.description,
							expireTime: expireTime
						);

						if (newSearch == null) {
							throw new NoNullAllowedException("Created session was null.");
						}

						waitingSessions.Add(newSearch);
						addedSessions.Add(newSearch);

					}

					return new SessionResult.Waiting(waitingSessions);
				} finally {
					if (addedSessions.Any()) {
						SessionAdded?.Invoke(addedSessions);
					}
					if (updatedSessions.Any() || stoppedSessions.Any()) {
						SessionChanged?.Invoke(
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
				var stoppedSessions = clear.stopped.ToDictionary(id => id, id => SessionStopReason.Expired);
				try {

					var session = await _context.JoinSessionAsync(sessionId, userId, expireTime);
					if (session == null) return new SessionResult.FailedToJoin();

					if (session.UserExpires.Count < session.MaxPlayerCount) {
						updatedSessions.Add(session);
						return new SessionResult.Waiting([session]);
					}

					(var updated, var stopped) = await _context.LeaveAllSessionsAsync(session.UserExpires.Keys.ToArray());
					if (updated.Any()) updatedSessions.AddRange(updated);
					if (stopped.Any()) stoppedSessions.AddRange(stopped.Select(id => (id, id == sessionId ? SessionStopReason.Joined : SessionStopReason.JoinedOther)));
					return new SessionResult.Matched(session);
				} finally {
					if (updatedSessions.Any() || stoppedSessions.Any()) {
						SessionChanged?.Invoke(
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
				var stopped = stoppedSessions.ToDictionary(id => id, id => SessionStopReason.Expired);
				if (!updatedSessions.Any() && !stopped.Any()) { return; }
				SessionChanged?.Invoke(
					updated: updatedSessions,
					stopped: stopped
				);
			} finally { sessionSemaphore.Release(); }
		}

		#region Leaving Sessions
		public async Task<(IEnumerable<SessionDetails> updated, Dictionary<Guid, SessionStopReason> stopped)> LeaveSessionsAsync(string userId, params Guid[] sessionIds) {
			await sessionSemaphore.WaitAsync();
			try {
				(var updatedSessions, var stoppedSessions) = await _context.LeaveSessionsAsync(userId, sessionIds);
				var stopped = stoppedSessions.ToDictionary(id => id, id => SessionStopReason.Canceled);
				try {
					return (updatedSessions, stopped);
				} finally {
					if (updatedSessions.Any() || stopped.Any()) {
						SessionChanged?.Invoke(
							updated: updatedSessions ?? [],
							stopped: stopped ?? []
						);
					}
				}
			} finally { sessionSemaphore.Release(); }
		}

		public async Task<(IEnumerable<SessionDetails> updated, Dictionary<Guid, SessionStopReason> stopped)> LeaveGamesAsync(string serverId, string userId, params string[] gameIds) {
			await sessionSemaphore.WaitAsync();
			try {
				(var updatedSessions, var stoppedSessions) = await _context.LeaveGamesAsync(userId, serverId, gameIds);
				var stopped = stoppedSessions.ToDictionary(id => id, id => SessionStopReason.Canceled);
				try {
					return (updatedSessions, stopped);
				} finally {
					if (updatedSessions.Any() || stopped.Any()) {
						SessionChanged?.Invoke(
							updated: updatedSessions ?? [],
							stopped: stopped ?? []
						);
					}
				}
			} finally { sessionSemaphore.Release(); }
		}

		public async Task<(IEnumerable<SessionDetails> updated, Dictionary<Guid, SessionStopReason> stopped)> LeaveAllPlayerSessionsAsync(string serverId, string userId) {
			await sessionSemaphore.WaitAsync();
			try {
				(var updatedSessions, var stoppedSessions) = await _context.LeaveAllSessionsAsync(userId);
				var stopped = stoppedSessions.ToDictionary(id => id, id => SessionStopReason.Canceled);
				try {
					return (updatedSessions, stopped);
				} finally {
					if (updatedSessions.Any() || stopped.Any()) {
						SessionChanged?.Invoke(
							updated: updatedSessions ?? [],
							stopped: stopped ?? []
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
			var gameDefaults = await _context.GetGameDefaultsAsync(serverId, gameIds);

			var searchParams = gameIds
				.Select(id => (
					id,
					maxPlayerCount ?? (gameDefaults.ContainsKey(id) ? (gameDefaults[id].maxPlayerCount ?? DefaultMaxPlayerCount) : DefaultMaxPlayerCount),
					description ?? (gameDefaults.ContainsKey(id) ? gameDefaults[id].description : null)
				))
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


		public async Task<SessionResult> TryJoinSessionAsync(string userId, Guid sessionId, DateTimeOffset? expireTime) { 
			if (expireTime == null) {
				return await TryJoinSessionAsync(userId, sessionId);
			}

			return await TryJoinSessionAsync(userId, sessionId, expireTime.Value);
		}
		#endregion
	}
}