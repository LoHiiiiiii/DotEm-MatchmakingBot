namespace DotemModel {
	public enum SessionStopReason {
		None,
		Joined,
		JoinedOther,
		Expired,
		Canceled
	}

	public static class SessionStopReasonExtensions {

		public static SessionStopReason? GetHigherPriorityReason(this SessionStopReason? a, SessionStopReason? other) {
			if (other == null) return a;
			if (a == null) return other;
			if (a == SessionStopReason.Joined || other == SessionStopReason.Joined) return SessionStopReason.Joined;
			if (a == SessionStopReason.JoinedOther || other == SessionStopReason.JoinedOther) return SessionStopReason.JoinedOther;
			if (a == SessionStopReason.Canceled || other == SessionStopReason.Canceled) return SessionStopReason.Canceled;
			if (a == SessionStopReason.Expired || other == SessionStopReason.Expired) return SessionStopReason.Expired;
			return other;
		}
	}
}
