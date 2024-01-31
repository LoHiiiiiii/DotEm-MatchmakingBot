using DotemMatchmaker.Context;
using DotemModel;
using SearchParameters = (string gameId, int playerCount, string? description);

namespace DotemMatchmaker
{
    public class Matchmaker
    {
        private readonly int _expireClearIntervalMilliseconds;
        private readonly MatchmakingContext _context;

        public Matchmaker(int expireClearIntervalMinutes = 1)
        {
            _expireClearIntervalMilliseconds = expireClearIntervalMinutes * 1000 * 60;
            _context = new MatchmakingContext("dotemMatchmaking.db");
        }

        public delegate void SessionChangedEvent(IEnumerable<SessionDetails> added, IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped);

        public event SessionChangedEvent? SessionChanged;

        private SemaphoreSlim sessionSemaphore = new SemaphoreSlim(1, 1);

        public void Initialize()
        {
            _context.Initialize();
        }

        public void StartExpirationLoop()
        {
            ExpireIntervalLoop();
        }

        public async Task<SessionResult> SearchSessionAsync(string serverId, string userId, DateTimeOffset expireTime,
            bool allowSuggestions = true, params SearchParameters[] searchAttempts
        )
        {
            if (!searchAttempts.Any()) return new SessionResult.NoAction();

            await sessionSemaphore.WaitAsync();
            try
            {
                var clear = await _context.ClearExpiredJoins();

                var addedSessions = new List<SessionDetails>();
                var updatedSessions = clear.updated.ToList();
                var stoppedSessions = clear.stopped.ToList();

                try
                {
                    var uniqueSearches = searchAttempts
                        .Select(attempt => (gameId: attempt.gameId.ToLower(), attempt.playerCount, attempt.description))
                        .ToHashSet();
                    var uniqueGames = uniqueSearches.Select(attempt => attempt.gameId);
                    var matchingGames = await _context.GetUserJoinableSessionsAsync(serverId, userId, uniqueGames);
                    if (matchingGames.Any())
                    {
                        var playableExactMatches = matchingGames.Where(match =>
                            match.UserExpires.Count == match.MaxPlayerCount - 1
                            && uniqueSearches.Contains((match.GameId, match.MaxPlayerCount, match.Description))
                        );

                        if (playableExactMatches.Any())
                        {
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
                            )
                            {
                                // Immediately match only if no descriptions and one type of exact match
                                var joined = await _context.JoinSessionAsync(exact.SessionId, userId, expireTime);
                                if (joined != null) {

                                    (var updated, var stopped) = await _context.LeaveAllSessionsAsync(joined.UserExpires.Keys.ToArray());
                                    if (updated != null && updated.Any()) updatedSessions.AddRange(updated);
									if (stopped != null && stopped.Any()) stoppedSessions.AddRange(stopped);

									return new SessionResult.Matched(joined);
								}
							}

                            var hasExactDescriptionless = playableExactMatches.Any(match => match.Description == null);

                            if (allowSuggestions || hasExactDescriptionless)
                            {
                                return new SessionResult.Suggestions(
                                    suggestedSessions: playableExactMatches.Concat(restOfPlayables),
                                    allowWait: !hasExactDescriptionless
                                );
                            }
                        }
                        else if (allowSuggestions)
                        {
                            return new SessionResult.Suggestions(
                                suggestedSessions: matchingGames,
                                allowWait: true
                            );
                        }
                    }

                    // No suggestions or exact descriptionless matches

                    var waitingSessions = new List<SessionDetails>();
                    var joinedSessions = await _context.GetUserExistingSessionsAsync(serverId, userId);

                    foreach (var attempt in uniqueSearches)
                    {
                        var existingSession = joinedSessions
                            .Where(match =>
                                match.GameId == attempt.gameId
                                && match.MaxPlayerCount == attempt.playerCount
                                && match.Description == attempt.description)
                            .SingleOrDefault();

                        if (existingSession != null)
                        {
                            var  updated = await _context.JoinSessionAsync(existingSession.SessionId, userId, expireTime);
                            if (updated == null) continue;
                            waitingSessions.Add(updated);
                            updatedSessions.Add(updated);
                        }
                        else
                        {
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
                }
                finally
                {
                    if (addedSessions.Any() || stoppedSessions.Any() || updatedSessions.Any())
                    {
                        SessionChanged?.Invoke(
                            added: addedSessions,
                            updated: updatedSessions,
                            stopped: stoppedSessions
                        );
                    }
                }
            }
            finally { sessionSemaphore.Release(); }
        }

        public async Task<(IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped)> LeaveSessionsAsync(string userId, params Guid[] sessionIds)
        {
            await sessionSemaphore.WaitAsync();
            try
            {
                (var updatedMatches, var stoppedMatches) = await _context.LeaveSessionsAsync(userId, sessionIds);
                try
                {
                    return (updatedMatches, stoppedMatches);
                }
                finally
                {
                    if (updatedMatches.Any() || stoppedMatches.Any())
                    {
                        SessionChanged?.Invoke(
                            added: [],
                            updated: updatedMatches ?? [],
                            stopped: stoppedMatches ?? []
                        );
                    }
                }
            }
            finally { sessionSemaphore.Release(); }
        }

        public async Task<(IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped)> LeaveGamesAsync(string serverId, string userId, params string[] gameIds)
        {
            await sessionSemaphore.WaitAsync();
            try
            {
                (var updatedMatches, var stoppedMatches) = await _context.LeaveGamesAsync(userId, gameIds);
                try
                {
                    return (updatedMatches, stoppedMatches);
                }
                finally
                {
                    if (updatedMatches.Any() || stoppedMatches.Any())
                    {
                        SessionChanged?.Invoke(
                            added: [],
                            updated: updatedMatches ?? [],
                            stopped: stoppedMatches ?? []
                        );
                    }
                }
            }
            finally { sessionSemaphore.Release(); }
        }

        public async Task<(IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped)> LeaveAllPlayerSessionsAsync(string serverId, string userId)
        {
            await sessionSemaphore.WaitAsync();
            try
            {
                (var updatedMatches, var stoppedMatches) = await _context.LeaveAllSessionsAsync(userId);
                try
                {
                    return (updatedMatches, stoppedMatches);
                }
                finally
                {
                    if (updatedMatches.Any() || stoppedMatches.Any())
                    {
                        SessionChanged?.Invoke(
                            added: [],
                            updated: updatedMatches ?? [],
                            stopped: stoppedMatches ?? []
                        );
                    }
                }
            }
            finally { sessionSemaphore.Release(); }
        }

        public async Task<SessionResult> TryJoinSessionAsync(string userId, Guid sessionId, DateTimeOffset expireTime)
        {
            await sessionSemaphore.WaitAsync();
            try
            {
                var clear = await _context.ClearExpiredJoins();

                var updatedSessions = clear.updated.ToList();
                var stoppedSessions = clear.stopped.ToList();
                try
                {

                    var session = await _context.JoinSessionAsync(sessionId, userId, expireTime);
                    if (session == null) return new SessionResult.NoAction();

                    if (session.UserExpires.Count < session.MaxPlayerCount)
                    {
                        updatedSessions.Add(session);
                        return new SessionResult.Waiting([session]);
                    }

                    await _context.StopSessionsAsync(session.SessionId);
                    stoppedSessions.Add(session.SessionId);
                    return new SessionResult.Matched(session);
                }
                finally
                {
                    if (updatedSessions.Any() || stoppedSessions.Any())
                    {
                        SessionChanged?.Invoke(
                            added: [],
                            updated: updatedSessions,
                            stopped: stoppedSessions
                        );
                    }
                }
            }
            finally { sessionSemaphore.Release(); }
        }

        private async void ExpireIntervalLoop()
        {
            while (true)
            {
                await Task.Delay(_expireClearIntervalMilliseconds);
                await sessionSemaphore.WaitAsync();
                try
                {
                    var (updatedSessions, stoppedSessions) = await _context.ClearExpiredJoins();
                    if (!updatedSessions.Any() && !stoppedSessions.Any()) { continue; }
                    SessionChanged?.Invoke(
                        added: [],
                        updated: updatedSessions,
                        stopped: stoppedSessions
                    );
                }
                finally { sessionSemaphore.Release(); }
            }
        }

        public async Task<IEnumerable<SessionDetails>> GetSessionsAsync(params Guid[] ids) => await _context.GetSessionsAsync(ids);

        public async Task<IEnumerable<SessionDetails>> GetUserSessionsAsync(string serverId, string userId)
            => await _context.GetUserExistingSessionsAsync(serverId, userId);
    }
}