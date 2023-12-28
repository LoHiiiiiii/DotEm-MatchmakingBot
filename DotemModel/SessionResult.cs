namespace DotemModel {
	public record SessionResult {
		public record Waiting(SessionDetails[] waits) : SessionResult;
		public record Matched(string[] playerIds, string gameId, string? description = null) : SessionResult;
		public record Found(SessionDetails[] founds, bool allowWait) : SessionResult;
		public record Suggestions(SessionDetails[] playableSuggestions, SessionDetails[] waitableSuggestions) : SessionResult;
		public record NoAction() : SessionResult;

		private SessionResult() { }
	}
}
