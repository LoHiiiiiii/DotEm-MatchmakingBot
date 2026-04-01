namespace DotemModel {
	public record SessionDetails {
		public Guid SessionId { get; }
		public string GameId { get; }
		public string GameName { get; }
		public string ServerId { get; }
		public int MaxPlayerCount { get; }
		public string? Description { get; }
		public Dictionary<string, DateTimeOffset> UserExpires { get; set; } = new();

		// Dapper constructor
		public SessionDetails(string sessionId, string gameId, string? name, string serverId, long maxPlayerCount, string? description) {
			SessionId = Guid.Parse(sessionId);
			ServerId = serverId;
			GameId = gameId;
			MaxPlayerCount = (int) maxPlayerCount;
			Description = description;
			GameName = string.IsNullOrEmpty(name) ? gameId : name;
		}

	}
}