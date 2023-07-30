namespace Matchmaker.Model {
	public record SearchResult {
		public record Searching(List<SearchDetails> searchDetails) : SearchResult;
		public record Found(string[] playerIds, string gameId, string? description = null) : SearchResult;
		public record Suggestions(List<SearchDetails> suggestions, bool playable) : SearchResult;
		public record NoSearch() : SearchResult;

		private SearchResult() { }
	}
}
