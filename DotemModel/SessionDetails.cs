namespace DotemModel {
	public record SessionDetails {

		public Guid SessionId { get; } = Guid.NewGuid();
		public string GameId { get; }
		public string ServerId { get; }
		public int MaxPlayerCount { get; }
		public string? Description { get; }
		public Dictionary<string, DateTimeOffset> UserExpires { get; set; } = new Dictionary<string, DateTimeOffset>();

		public SessionDetails(string gameId, string userId, string serverId, int maxPlayerCount, string? description) {
			GameId = gameId;
			ServerId = serverId;
			MaxPlayerCount = maxPlayerCount;
			Description = description;
		}
	}
}