using Matchmaker.Model;

namespace Matchmaker {
	public class Matchmaker {
		private int ExpireClearIntervalMilliseconds { get; }

		public Matchmaker(int expireClearIntervalMinutes) {
			ExpireClearIntervalMilliseconds = expireClearIntervalMinutes * 1000 * 60;
			ExpireInterval();
		}

		public delegate void SearchEventHandler(object sender, SearchDetails details);

		public event SearchEventHandler? SearchStopped;
		public event SearchEventHandler? SearchUpdated;
		public event SearchEventHandler? SearchAdded;

		private Dictionary<Guid, SearchDetails> activeSearches = new ();
		private SemaphoreSlim searchSemaphore = new SemaphoreSlim(1, 1);

		public async Task<SearchResult> SearchAsync(string serverId, string userId, DateTimeOffset expireTime, 
			bool allowSuggestions = true, params (string gameId, int playerCount, string? description)[] searchAttempts) {
			if (!searchAttempts.Any()) return new SearchResult.NoSearch();

			await searchSemaphore.WaitAsync();
			ClearExpiredSearches();

			try {
				var uniqueSearches = searchAttempts.ToHashSet();
				var gameIds = searchAttempts.Select(attempt => attempt.gameId).ToHashSet();

				var matchingGameSearches = activeSearches.Values.Where(search => search.ServerId == serverId && search.UserId != userId && gameIds.Contains(search.GameId));

				if (matchingGameSearches.Any()) {
					var structuredGameSearches = new Dictionary<string, Dictionary<(int playerCount, string? description), List<SearchDetails>>>();
					foreach (var search in matchingGameSearches) {
						var secondaryKey = (search.PlayerCount, search.Description);
						if (!structuredGameSearches.ContainsKey(search.GameId)) {
							var dictionary = new Dictionary<(int, string?), List<SearchDetails>>();
							structuredGameSearches.Add(search.GameId, dictionary);
						}
						if (!structuredGameSearches[search.GameId].ContainsKey(secondaryKey)) {
							structuredGameSearches[search.GameId].Add(secondaryKey, new List<SearchDetails>());
						}
						structuredGameSearches[search.GameId][secondaryKey].Add(search);
					}

					var playableCompleteMatches = new Dictionary<(string gameId, int playerCount, string? description), List<SearchDetails>>();
					var playablePartialMatches = new List<(string gameId, int playerCount, string? description)>();
					var searchablePartialMatches = new List<(string gameId, int playerCount, string? description)>();

					// Sort matches
					foreach (var attempt in uniqueSearches) {
						if (structuredGameSearches.ContainsKey(attempt.gameId)) {
							foreach (var secondaryKey in structuredGameSearches[attempt.gameId].Keys) {
								var playable = attempt.playerCount == structuredGameSearches[attempt.gameId][secondaryKey].Count - 1;
								if (secondaryKey == (attempt.playerCount, attempt.description)) {
									if (playable) {
										playableCompleteMatches.Add(attempt, structuredGameSearches[attempt.gameId][secondaryKey]);
									}
								} else {
									if (playable) {
										playablePartialMatches.Add((attempt.gameId, secondaryKey.playerCount, secondaryKey.description));
									} else {
										searchablePartialMatches.Add((attempt.gameId, secondaryKey.playerCount, secondaryKey.description));
									}
								}
							}
						}
					}

					if (playableCompleteMatches.Count > 0) {
						// One exact match
						if (playableCompleteMatches.Count == 1 && (playablePartialMatches.Count == 0 || !allowSuggestions)) {
							var key = playableCompleteMatches.First().Key;
							if (key.description == null || !allowSuggestions) { // Immediately match only if no descriptions
								var players = new List<string>() { userId };

								for(int i = 0; i < key.playerCount - 1; ++i) {
									var search = playableCompleteMatches[key][i];
									players.Add(search.UserId);
									SearchStopped?.Invoke(this, search);
									activeSearches.Remove(search.SearchId);
								}
								
								return new SearchResult.Found(players.ToArray(), key.gameId, key.description);
							}
						}

						// Give playable suggestions
						var totalPlayables = playableCompleteMatches.Keys
												.Concat(playablePartialMatches).ToArray();
						return new SearchResult.Suggestions(totalPlayables);
					} else if ((playablePartialMatches.Count > 0 || searchablePartialMatches.Count > 0) && allowSuggestions) {
						var playableSuggestions = playablePartialMatches.ToArray();
						var searchableSuggestions = searchablePartialMatches.ToArray();
						return new SearchResult.Suggestions(playableSuggestions, searchableSuggestions);
					}
				}

				// No suggestions or matches

				var searchesToReturn = new List<SearchDetails>();
				var userSearches = activeSearches.Values.Where(search => search.UserId == userId && search.ServerId == serverId);
				foreach (var attempt in uniqueSearches) {
					var existingSearch = userSearches.Where(search =>  search.GameId == attempt.gameId 
															&& search.PlayerCount == attempt.playerCount 
															&& search.Description == attempt.description)
													 .FirstOrDefault();
					if (existingSearch != null) {
						existingSearch.ExpireTime = expireTime;
						SearchUpdated?.Invoke(this, existingSearch);
						searchesToReturn.Add(existingSearch);
					} else {
						var newSearch = new SearchDetails(
							serverId: serverId,
							userId: userId,
							gameId: attempt.gameId,
							playerCount: attempt.playerCount,
							description: attempt.description,
							expireTime: expireTime);
						searchesToReturn.Add(newSearch);
						activeSearches.Add(newSearch.SearchId, newSearch);
						SearchAdded?.Invoke(this, newSearch);
					}
				}

				return new SearchResult.Searching(searchesToReturn.ToArray());
			} finally { searchSemaphore.Release(); }
		}

		public async Task<SearchDetails[]> CancelSearches(params Guid[] searchIds) {
			await searchSemaphore.WaitAsync();
			try {
				var canceledSearches = new List<SearchDetails>();
				foreach (var id in searchIds) {
					if (!activeSearches.ContainsKey(id)) continue;
					SearchStopped?.Invoke(this, activeSearches[id]);
					canceledSearches.Add(activeSearches[id]);
					activeSearches.Remove(id);
				}

				return canceledSearches.ToArray();
			} finally { searchSemaphore.Release(); }
		}
		
		public async Task<SearchDetails[]> CancelSearches(string serverId, string userId, params (string gameId, int playerCount, string? description)[] searches) {
			await searchSemaphore.WaitAsync();

			try {
				var userSearches = activeSearches.Values.Where(detail => detail.UserId == userId && detail.ServerId == serverId).ToArray();

				var gameSearches = new Dictionary<string, List<SearchDetails>>();

				foreach (var userSearch in userSearches) {
					if (!gameSearches.ContainsKey(userSearch.GameId)) {
						gameSearches.Add(userSearch.GameId, new List<SearchDetails>());
					}
					gameSearches[userSearch.GameId].Add(userSearch);
				}

				var canceledSearches = new List<SearchDetails>();

				foreach (var search in searches) {
					if (!gameSearches.ContainsKey(search.gameId)) continue;

					SearchDetails canceledSearch;
					if (gameSearches[search.gameId].Count == 1) {
						canceledSearch = gameSearches[search.gameId][0];
					} else {
						var exactMatch = gameSearches[search.gameId].Where(details => details.PlayerCount == search.playerCount && details.Description == search.description)
																	.FirstOrDefault();
						if (exactMatch != null) {
							canceledSearch = exactMatch;
						} else {
							continue;
						}
					}

					canceledSearches.Add(canceledSearch);
					SearchStopped?.Invoke(this, canceledSearch);
					activeSearches.Remove(canceledSearch.SearchId);
				}

				return canceledSearches.ToArray();
			} finally { searchSemaphore.Release(); }
		}

		public async Task<SearchDetails[]> CancelAllPlayerSearches(string serverId, string userId) {
			await searchSemaphore.WaitAsync();
			try {
				var searches  = activeSearches.Values.Where(detail => detail.UserId == userId && detail.ServerId == serverId).ToArray();

				foreach (var search in searches) {
					SearchStopped?.Invoke(this, search);
					activeSearches.Remove(search.SearchId);
				}
				return searches;
			} finally { searchSemaphore.Release(); }
		}
		
		public async Task<SearchResult> TryAcceptMatch(string userId, Guid searchId) {
			var handle = searchSemaphore.AvailableWaitHandle;

			if (!activeSearches.ContainsKey(searchId)) {
				searchSemaphore.Release();
				return new SearchResult.NoSearch();
			}

			var search = activeSearches[searchId];
			
			if (search.PlayerCount > 2) {
				searchSemaphore.Release();
				return await SearchAsync(search.ServerId, search.UserId, search.ExpireTime, false, (search.GameId, search.PlayerCount, search.Description));
			}
			
			try {
				if (activeSearches.ContainsKey(search.SearchId)) {
					var players = new string[] { userId, search.UserId };
					SearchStopped?.Invoke(this, search);
					activeSearches.Remove(search.SearchId);
					return new SearchResult.Found(players, search.GameId, search.Description);
				}
				return new SearchResult.NoSearch();
			} finally { searchSemaphore.Release(); }
		}

		// Be sure to await semaphore before entering
		private IEnumerable<SearchDetails>? ClearExpiredSearches() {
			var now = DateTime.Now;
			var expiredSearches = activeSearches.Values.Where(search => search.ExpireTime < now);
			if (SearchStopped != null) foreach (var expiredSearch in expiredSearches) SearchStopped.Invoke(this, expiredSearch);
			activeSearches = activeSearches.Where(pair => pair.Value.ExpireTime >= now).ToDictionary(pair => pair.Key, pair => pair.Value);
			return expiredSearches;
		}

		private async void ExpireInterval() {
			while (true) {
				await Task.Delay(ExpireClearIntervalMilliseconds);
				await searchSemaphore.WaitAsync();
				try {
					ClearExpiredSearches();
				} finally { searchSemaphore.Release(); }
			}
		}

		public SearchDetails? GetSearch(Guid searchId) {
			if (!activeSearches.TryGetValue(searchId, out var search)) return null;
			return search;
		}
	}
}