namespace Matchmaker.Model {
	public record SearchResult {
		public record Searching(SearchDetails searchDetails) : SearchResult;
		public record Found(string[] playerNames, string game) : SearchResult;
		// TODO: Add suggestions - public record Suggestions(???) : SearchResult;

		//private SearchResult() { }
	}
}
