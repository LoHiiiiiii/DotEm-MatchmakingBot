namespace DotemModel {
	public record SearchDetails {

		public Guid SearchId { get; } = Guid.NewGuid();
		public string GameId { get; }
		public string UserId { get; }
		public string ServerId { get; }
		public DateTimeOffset ExpireTime { get; set; }
		public int PlayerCount { get; }
		public string? Description { get; }

		public SearchDetails(string gameId, string userId, string serverId, DateTimeOffset expireTime, int playerCount, string? description) {
			GameId = gameId;
			UserId = userId;
			ExpireTime = expireTime;
			ServerId = serverId;
			PlayerCount = playerCount;
			Description = description;
		}
	}
}
