namespace DotemModel {
	public record SessionResult {
		public record Waiting(IEnumerable<SessionDetails> waits) : SessionResult;
		public record Matched(SessionDetails matchedSession) : SessionResult;
		public record Suggestions(IEnumerable<SessionDetails> suggestedSessions, bool allowWait) : SessionResult;
		public record FailedToJoin() : SessionResult;
		public record FailedToSuggest() : SessionResult;
		public record NoAction() : SessionResult;

		private SessionResult() { }
	}
}
