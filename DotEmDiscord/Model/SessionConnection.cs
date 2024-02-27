namespace DotemDiscord.Model {
	public record SessionConnection {
		public ulong ChannelId { get; init; }
		public ulong MessageId { get; init; }
		public ulong? UserId { get; init; }
		public IEnumerable<Guid> SessionIds { get; init; } = Enumerable.Empty<Guid>();

		public SessionConnection() { }
	}
}
