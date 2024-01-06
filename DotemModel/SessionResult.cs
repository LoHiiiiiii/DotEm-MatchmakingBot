namespace DotemModel {
	public record SessionResult {
		public record Waiting(SessionDetails[] waits) : SessionResult;
		public record Matched(string[] playerIds, SessionDetails matchedSession, string? description = null) : SessionResult;
		public record Suggestions(SessionDetails[] suggestedSessions, bool allowWait) : SessionResult;
		public record NoAction() : SessionResult;

		private SessionResult() { }
	}
}
