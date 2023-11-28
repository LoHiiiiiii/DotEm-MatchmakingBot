namespace DotemModel {
	public record SearchResult {
		public record Searching(SearchDetails[] searches) : SearchResult;
		public record Found(string[] playerIds, string gameId, string? description = null) : SearchResult;
		public record Suggestions((string gameId, int playercount, string? description)[]? playableSuggestions = null, 
			(string gameId, int playercount, string? description)[]? searchSuggestions = null) : SearchResult;
		public record NoSearch() : SearchResult;

		private SearchResult() { }
	}
}
