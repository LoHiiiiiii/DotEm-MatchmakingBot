using DotemModel;
using SearchParameters = (string gameId, int playerCount, string? description);

namespace DotemMatchmaker {
	public class Matchmaker {
		private readonly int _expireClearIntervalMilliseconds;

		public Matchmaker(int expireClearIntervalMinutes) {
			_expireClearIntervalMilliseconds = expireClearIntervalMinutes * 1000 * 60;
			ExpireIntervalLoop();
		}

		public delegate void SessionChangedEvent(SessionDetails[] added, SessionDetails[] updated, SessionDetails[] stopped);

		public event SessionChangedEvent? SessionChanged;

		private Dictionary<Guid, SessionDetails> joinableSessions = new();

		private SemaphoreSlim sessionSemaphore = new SemaphoreSlim(1, 1);

		public async Task<SessionResult> SearchSessionAsync(string serverId, string userId, DateTimeOffset expireTime,
			bool allowSuggestions = true, params SearchParameters[] searchAttempts
		) {
			if (!searchAttempts.Any()) return new SessionResult.NoAction();

			await sessionSemaphore.WaitAsync();
			try {
				(var stoppedSessions, var updatedSessions) = ClearExpiredUserJoins();

				var addedSessions = new List<SessionDetails>();

				try {
					var uniqueSearches = searchAttempts.ToHashSet();
					var uniqueGames = searchAttempts.Select(attempt => attempt.gameId).ToHashSet();
					var matchingGames = joinableSessions.Values.Where(
						match => match.ServerId == serverId
						&& !match.UserExpires.ContainsKey(userId)
						&& uniqueGames.Contains(match.GameId)
					);

					if (matchingGames.Any()) {
						var playableExactMatches = matchingGames.Where(match => 
							match.UserExpires.Count == match.MaxPlayerCount - 1
							&& uniqueSearches.Contains((match.GameId, match.MaxPlayerCount, match.Description))
						);

						if (playableExactMatches.Any()) {
							var exact = playableExactMatches.First();


							var restOfPlayables = matchingGames.Where(match =>
								match.UserExpires.Count == match.MaxPlayerCount - 1
								&& !uniqueSearches.Contains((match.GameId, match.MaxPlayerCount, match.Description))
							);

							if (playableExactMatches.All(match =>
									match.GameId == exact.GameId
									&& match.MaxPlayerCount == exact.MaxPlayerCount
									&& match.Description == exact.Description
								)
								&& (!restOfPlayables.Any() && exact.Description == null)
							) {
								// Immediately match only if no descriptions and one type of exact match
								exact.UserExpires.Add(userId, expireTime);

								stoppedSessions.Add(exact);
								joinableSessions.Remove(exact.SessionId);
								return new SessionResult.Matched(exact.UserExpires.Keys.ToArray(), exact, exact.Description);
							}

							var hasExactDescriptionless = playableExactMatches.Any(match => match.Description == null);

							if (allowSuggestions || hasExactDescriptionless) {
								SessionDetails[] suggestions = playableExactMatches
														.Concat(restOfPlayables).ToArray();
								return new SessionResult.Suggestions(
									suggestedSessions: suggestions, 
									allowWait: !hasExactDescriptionless
								);
							}
						} else if (allowSuggestions) {
							return new SessionResult.Suggestions(
								suggestedSessions: matchingGames.ToArray(),
								allowWait: true
							);
						}
					}

					// No suggestions or exact descriptionless matches

					var waitingSessions = new List<SessionDetails>();
					var joinedSessions = joinableSessions.Values.Where(search => search.UserExpires.ContainsKey(userId) && search.ServerId == serverId);
					foreach (var attempt in uniqueSearches) {
						var existingSessions = joinedSessions.Where(match => match.GameId == attempt.gameId
																&& match.MaxPlayerCount == attempt.playerCount
																&& match.Description == attempt.description);
						if (existingSessions.Any()) {
							foreach (var session in existingSessions) {
								session.UserExpires[userId] = expireTime;
								updatedSessions.Add(session);
								waitingSessions.Add(session);
							}
						} else {
							var newSearch = new SessionDetails(
								serverId: serverId,
								userId: userId,
								gameId: attempt.gameId,
								maxPlayerCount: attempt.playerCount,
								description: attempt.description);
							newSearch.UserExpires.Add(userId, expireTime);
							waitingSessions.Add(newSearch);
							joinableSessions.Add(newSearch.SessionId, newSearch);
							addedSessions.Add(newSearch);
						}
					}

					return new SessionResult.Waiting(waitingSessions.ToArray());
				} finally {
					if (addedSessions.Any() || stoppedSessions.Any() || updatedSessions.Any()) {
						SessionChanged?.Invoke(
							added: addedSessions.ToArray(),
							updated: updatedSessions.ToArray(),
							stopped: stoppedSessions.ToArray()
						);
					}
				}
			} finally { sessionSemaphore.Release(); }
		}

		public async Task<SessionDetails[]> LeaveSessionsAsync(string userId, params Guid[] sessionIds) {
			await sessionSemaphore.WaitAsync();
			try {
				(var stoppedMatches, var updatedMatches) = ClearExpiredUserJoins();
				try {
					foreach (var id in sessionIds) {
						if (!joinableSessions.TryGetValue(id, out var match)) continue;
						if (!match.UserExpires.Remove(userId, out _)) continue;
						if (match.UserExpires.Count == 0) {
							stoppedMatches.Add(match);
							joinableSessions.Remove(id);
						} else {
							updatedMatches.Add(match);
						}
					}

					return stoppedMatches.Concat(updatedMatches).ToArray();
				} finally {
					if (stoppedMatches.Any() || updatedMatches.Any()) {
						SessionChanged?.Invoke(
							added: [],
							updated: updatedMatches.ToArray(),
							stopped: stoppedMatches.ToArray()
						);
					}
				}
			} finally { sessionSemaphore.Release(); }
		}

		public async Task<SessionDetails[]> LeaveSessionsAsync(string serverId, string userId, params string[] gameIds) {
			await sessionSemaphore.WaitAsync();
			try {
				(var stoppedSessions, var updatedSessions) = ClearExpiredUserJoins();

				try {
					var joinedMatches = joinableSessions.Values.Where(match => match.UserExpires.ContainsKey(userId)
							&& match.ServerId == serverId
							&& gameIds.Contains(match.GameId)
						).ToArray();

					foreach (var match in joinedMatches) {
						if (!match.UserExpires.Remove(userId, out _)) continue;
						if (match.UserExpires.Count == 0) {
							stoppedSessions.Add(match);
							joinableSessions.Remove(match.SessionId);
						} else {
							updatedSessions.Add(match);
						}
					}

					return stoppedSessions.Concat(updatedSessions).ToArray();
				} finally {
					if (stoppedSessions.Any() || updatedSessions.Any()) {
						SessionChanged?.Invoke(
							added: [],
							updated: updatedSessions.ToArray(),
							stopped: stoppedSessions.ToArray()
						);
					}
				}
			} finally { sessionSemaphore.Release(); }
		}

		public async Task<SessionDetails[]> LeaveAllPlayerSessionsAsync(string serverId, string userId) {
			await sessionSemaphore.WaitAsync();
			try {
				(var stoppedSessions, var updatedSessions) = ClearExpiredUserJoins();

				try {
					var matches = joinableSessions.Values.Where(match => match.UserExpires.ContainsKey(userId) && match.ServerId == serverId).ToArray();

					foreach (var match in matches) {
						match.UserExpires.Remove(userId);
						if (match.UserExpires.Count == 0) {
							stoppedSessions.Add(match);
							joinableSessions.Remove(match.SessionId);
						} else {
							updatedSessions.Add(match);
						}
					}
					return matches;
				} finally {
					if (stoppedSessions.Any()) {
						SessionChanged?.Invoke(
							added: [], updated:
							updatedSessions.ToArray(),
							stopped: stoppedSessions.ToArray()
						);
					}
				}
			} finally { sessionSemaphore.Release(); }
		}

		public async Task<SessionResult> TryJoinSessionAsync(string userId, Guid matchId, DateTimeOffset expireTime) {
			await sessionSemaphore.WaitAsync();
			try {
				(var stoppedMatches, var updatedMatches) = ClearExpiredUserJoins();
				try {

					if (!joinableSessions.TryGetValue(matchId, out var match)) { return new SessionResult.NoAction(); }
					if (match.UserExpires.ContainsKey(userId)) { return new SessionResult.NoAction(); }

					match.UserExpires.Add(userId, expireTime);

					if (match.UserExpires.Count == match.MaxPlayerCount) {
						stoppedMatches.Add(match);
						joinableSessions.Remove(match.SessionId);
						return new SessionResult.Matched(match.UserExpires.Keys.ToArray(), match, match.Description);
					}

					updatedMatches.Add(match);

					return new SessionResult.Waiting([match]);

				} finally {
					if (stoppedMatches.Any() || updatedMatches.Any()) {
						SessionChanged?.Invoke(
							added: [],
							updated: updatedMatches.ToArray(),
							stopped: stoppedMatches.ToArray()
						);
					}
				}
			} finally { sessionSemaphore.Release(); }
		}

		// Be sure to await semaphore before entering
		private (List<SessionDetails> expired, List<SessionDetails> updated) ClearExpiredUserJoins() {
			var expired = new List<SessionDetails>();
			var updated = new List<SessionDetails>();
			var now = DateTime.Now;
			foreach (var match in joinableSessions.Values.ToArray()) {
				var prevCount = match.UserExpires.Count;
				match.UserExpires = match.UserExpires.Where(pair => pair.Value >= now).ToDictionary(pair => pair.Key, pair => pair.Value);
				if (match.UserExpires.Any()) {
					if (match.UserExpires.Count < prevCount) { updated.Add(match); }
				} else {
					expired.Add(match);
					joinableSessions.Remove(match.SessionId);
				}
			}
			return (expired, updated);
		}

		private async void ExpireIntervalLoop() {
			while (true) {
				await Task.Delay(_expireClearIntervalMilliseconds);
				await sessionSemaphore.WaitAsync();
				try {
					var (stoppedMatches, updatedSearches) = ClearExpiredUserJoins();
					if (!stoppedMatches.Any() && !updatedSearches.Any()) { continue; }
					SessionChanged?.Invoke(
						added: [], 
						updated: updatedSearches.ToArray(),
						stopped: stoppedMatches.ToArray()
					);
				} finally { sessionSemaphore.Release(); }
			}
		}

		public SessionDetails? GetMatch(Guid matchId) {
			if (!joinableSessions.TryGetValue(matchId, out var match)) return null;
			return match;
		}
	}
}