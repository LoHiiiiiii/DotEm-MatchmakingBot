using DotemModel;
using SearchParameters = (string gameId, int playerCount, string? description);

namespace DotemMatchmaker {
	public class Matchmaker {
		private readonly int _expireClearIntervalMilliseconds;

		public Matchmaker(int expireClearIntervalMinutes) {
			_expireClearIntervalMilliseconds = expireClearIntervalMinutes * 1000 * 60;
			ExpireInterval();
		}

		public delegate void SearchChangedEvent(object sender, SearchDetails[] added, SearchDetails[] updated, SearchDetails[] stopped);

		public event SearchChangedEvent? SearchChanged;

		private Dictionary<Guid, SearchDetails> activeSearches = new ();
		private SemaphoreSlim searchSemaphore = new SemaphoreSlim(1, 1);

		public async Task<SearchResult> SearchAsync(string serverId, string userId, DateTimeOffset expireTime, 
			bool allowSuggestions = true, params SearchParameters[] searchAttempts) {
			if (!searchAttempts.Any()) return new SearchResult.NoSearch();

			await searchSemaphore.WaitAsync();
			var stoppedSearches = ClearExpiredSearches()?.ToList() ?? new List<SearchDetails>();

			var addedSearches = new List<SearchDetails>();
			var updatedSearches = new List<SearchDetails>();

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

					var playableCompleteMatches = new Dictionary<SearchParameters, List<SearchDetails>>();
					var playablePartialMatches = new List<SearchParameters>();
					var searchablePartialMatches = new List<SearchParameters>();

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
									stoppedSearches.Add(search);
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
					SearchDetails? existingSearch = userSearches.Where(search =>  search.GameId == attempt.gameId 
															&& search.PlayerCount == attempt.playerCount 
															&& search.Description == attempt.description)
													 .FirstOrDefault();
					if (existingSearch is SearchDetails existing) {
						existing.ExpireTime = expireTime;
						updatedSearches.Add(existing);
						searchesToReturn.Add(existing);
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
						addedSearches.Add(newSearch);
					}
				}

				return new SearchResult.Searching(searchesToReturn.ToArray());
			} finally { 
				if (addedSearches.Any() || stoppedSearches.Any() || updatedSearches.Any()) {
					SearchChanged?.Invoke(this, 
						added: addedSearches.ToArray(), 
						updated: updatedSearches.ToArray(), 
						stopped: stoppedSearches.ToArray()
					);
				}
				searchSemaphore.Release(); 
			}
		}

		public async Task<SearchDetails[]> CancelSearchesAsync(params Guid[] searchIds) {
			await searchSemaphore.WaitAsync();
			var canceledSearches = new List<SearchDetails>();
			try {
				foreach (var id in searchIds) {
					if (!activeSearches.ContainsKey(id)) continue;
					canceledSearches.Add(activeSearches[id]);
					activeSearches.Remove(id);
				}

				return canceledSearches.ToArray();
			} finally {
				if (canceledSearches.Any()) {
					SearchChanged?.Invoke(this, stopped: canceledSearches.ToArray(), added: [], updated: []);
				}
				searchSemaphore.Release(); 
			}
		}
		
		public async Task<SearchDetails[]> CancelSearchesAsync(string serverId, string userId, params SearchParameters[] searches) {
			await searchSemaphore.WaitAsync();

			var canceledSearches = new List<SearchDetails>();

			try {
				var userSearches = activeSearches.Values.Where(detail => detail.UserId == userId && detail.ServerId == serverId).ToArray();

				var gameSearches = new Dictionary<string, List<SearchDetails>>();

				foreach (var userSearch in userSearches) {
					if (!gameSearches.ContainsKey(userSearch.GameId)) {
						gameSearches.Add(userSearch.GameId, new List<SearchDetails>());
					}
					gameSearches[userSearch.GameId].Add(userSearch);
				}

				foreach (var search in searches) {
					if (!gameSearches.ContainsKey(search.gameId)) continue;

					SearchDetails canceledSearch;
					if (gameSearches[search.gameId].Count == 1) {
						canceledSearch = gameSearches[search.gameId][0];
					} else {
						SearchDetails? exactMatch = gameSearches[search.gameId].Where(details => details.PlayerCount == search.playerCount && details.Description == search.description)
																	.FirstOrDefault();
						if (exactMatch is SearchDetails existing) {
							canceledSearch = existing;
						} else {
							continue;
						}
					}

					canceledSearches.Add(canceledSearch);
					activeSearches.Remove(canceledSearch.SearchId);
				}

				return canceledSearches.ToArray();
			} finally {
				if (canceledSearches.Any()) {
					SearchChanged?.Invoke(this, stopped: canceledSearches.ToArray(), added: [], updated: []);
				}
				searchSemaphore.Release(); 
			}
		}

		public async Task<SearchDetails[]> CancelAllPlayerSearchesAsync(string serverId, string userId) {
			await searchSemaphore.WaitAsync();
			var canceledSearches = new List<SearchDetails>();
			try {
				var searches  = activeSearches.Values.Where(detail => detail.UserId == userId && detail.ServerId == serverId).ToArray();

				foreach (var search in searches) {
					canceledSearches.Add(search);
					activeSearches.Remove(search.SearchId);
				}
				return searches;
			} finally {
				if (canceledSearches.Any()) {
					SearchChanged?.Invoke(this, stopped: canceledSearches.ToArray(), added: [], updated: []);
				}
				searchSemaphore.Release(); 
			}
		}
		
		public async Task<SearchResult> TryAcceptMatchAsync(string userId, Guid searchId) {
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
					activeSearches.Remove(search.SearchId);
					
					SearchChanged?.Invoke(this, stopped: new SearchDetails[] {search}, added: null, updated: null);
					return new SearchResult.Found(players, search.GameId, search.Description);
				}
				return new SearchResult.NoSearch();
			} finally { searchSemaphore.Release(); }
		}

		// Be sure to await semaphore before entering
		private SearchDetails[]? ClearExpiredSearches() {
			var now = DateTime.Now;
			var expiredSearches = activeSearches.Values.Where(search => search.ExpireTime < now);
			if (expiredSearches == null || !expiredSearches.Any()) return null;
			activeSearches = activeSearches.Where(pair => pair.Value.ExpireTime >= now).ToDictionary(pair => pair.Key, pair => pair.Value);
			return expiredSearches.ToArray();
		}

		private async void ExpireInterval() {
			while (true) {
				await Task.Delay(_expireClearIntervalMilliseconds);
				await searchSemaphore.WaitAsync();
				try {
					var expiredSearches = ClearExpiredSearches();
					SearchChanged?.Invoke(this, stopped: expiredSearches, added: [], updated: []);
				} finally { searchSemaphore.Release(); }
			}
		}

		public SearchDetails? GetSearch(Guid searchId) {
			if (!activeSearches.TryGetValue(searchId, out var search)) return null;
			return search;
		}
	}
}