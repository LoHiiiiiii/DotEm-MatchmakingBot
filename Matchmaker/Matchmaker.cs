using Matchmaker.Model;


namespace Matchmaker {
	public class Matchmaker {
		private int ExpireClearIntervalMilliseconds { get; }

		public Matchmaker(int expireClearIntervalMinutes) {
			ExpireClearIntervalMilliseconds = expireClearIntervalMinutes * 1000 * 60;
			ExpireInterval();
		}

		public delegate void SearchEventHandler(object sender, Guid searchId);
		public event SearchEventHandler? SearchCanceled;

		private List<SearchDetails> activeSearches = new List<SearchDetails>();
		private SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

		public async Task<SearchResult> SearchAsync(string serverId, string userId, DateTimeOffset expireTime, bool allowSuggestions = true, params (string gameId, int playerCount, string? description)[] searchAttempts) {
			if (!searchAttempts.Any()) return new SearchResult.NoSearch();

			await semaphore.WaitAsync();
			ClearExpiredSearches();

			try {
				var gameIds = searchAttempts.Select(attempt => attempt.gameId).ToHashSet();

				var matchingSearches = activeSearches.FindAll(search => search.ServerId == serverId && search.UserId != userId && gameIds.Contains(search.GameId));

				// No matches
				if (!matchingSearches.Any()) {
					var searchesToReturn = new List<SearchDetails>();
					var userSearches = activeSearches.FindAll(search => search.UserId == userId);
					foreach (var attempt in searchAttempts) {
						var exists = userSearches.Find(search => search.ServerId == serverId && search.GameId == attempt.gameId &&
																search.PlayerCount == attempt.playerCount && search.Description == attempt.description);
						if (exists != null) {
							exists.ExpireTime = expireTime; // Should probably be an explicit event to extend expiretime
							searchesToReturn.Add(exists);
						} else {
							var newDetails = new SearchDetails(
								serverId: serverId,
								userId: userId,
								gameId: attempt.gameId,
								playerCount: attempt.playerCount,
								description: attempt.description,
								expireTime: expireTime);
							searchesToReturn.Add(newDetails);
							activeSearches.Add(newDetails);
						}
					}

					return new SearchResult.Searching(searchesToReturn);
				}

				var structuredMatches = new Dictionary<string, Dictionary<(int playerCount, string? description), List<SearchDetails>>>();
				foreach (var search in matchingSearches) {
					var secondaryKey = (search.PlayerCount, search.Description);
					if (!structuredMatches.ContainsKey(search.GameId)) {
						var dictionary = new Dictionary<(int, string?), List<SearchDetails>>();
						structuredMatches.Add(search.GameId, dictionary);
					}
					if (!structuredMatches[search.GameId].ContainsKey(secondaryKey)) {
						structuredMatches[search.GameId].Add(secondaryKey, new List<SearchDetails>());
					}
					structuredMatches[search.GameId][secondaryKey].Add(search);
				}

				var playableCompleteMatches = new Dictionary<(string gameId, int playerCount, string? description), List<SearchDetails>>();
				var playablePartialMatches = new List<List<SearchDetails>>();
				var searchablePartialMatches = new List<(string gameId, int playerCount, string? description)>();

				foreach (var attempt in searchAttempts) {
					if (structuredMatches.ContainsKey(attempt.gameId)) {
						foreach (var secondaryKey in structuredMatches[attempt.gameId].Keys) {
							var playable = attempt.playerCount == structuredMatches[attempt.gameId][secondaryKey].Count - 1;
							if (secondaryKey == (attempt.playerCount, attempt.description)) {
								if (playable) {
									playableCompleteMatches.Add(attempt, structuredMatches[attempt.gameId][secondaryKey]);
								}
							} else {
								if (playable) {
									playablePartialMatches.Add(structuredMatches[attempt.gameId][secondaryKey]);
								} else {
									searchablePartialMatches.Add((attempt.gameId, secondaryKey.playerCount, secondaryKey.description));
								}
							}
						}
					}

				}

				if (playableCompleteMatches.Count > 0) {
					if (playableCompleteMatches.Count == 1) {
						var key = playableCompleteMatches.First().Key;
						if (key.description == null) { // Immediately match only if no descriptions
							var players = playableCompleteMatches[key].Select(details => details.UserId).ToList();
							players.Add(userId);
							// TODO: Accept match -cleaning
							return new SearchResult.Found(players.ToArray(), key.gameId, key.description);
						}
					}
				}

				return new SearchResult.NoSearch();
			} finally { semaphore.Release(); }
		}

		// Be sure to await semaphore before entering
		private void ClearExpiredSearches() {
			var expiredSearches = activeSearches.FindAll(search => search.ExpireTime < DateTimeOffset.Now);
			if (SearchCanceled != null) foreach (var expiredSearch in expiredSearches) SearchCanceled?.Invoke(this, expiredSearch.SearchId);
			activeSearches = activeSearches.FindAll(search => search.ExpireTime >= DateTimeOffset.Now);
		}

		private async void ExpireInterval() {
			while (true) {
				await Task.Delay(ExpireClearIntervalMilliseconds);
				await semaphore.WaitAsync();
				try {
					ClearExpiredSearches();
				} finally { semaphore.Release(); }
			}
		}
	}
}