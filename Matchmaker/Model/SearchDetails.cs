namespace Matchmaker.Model {
	public class SearchDetails {
		public string GameName { get; }
		public string GameId { get; }
		public DateTimeOffset EndTime { get; }

		public SearchDetails(string gameName, string gameId, DateTimeOffset endTime) {
			GameName = gameName;
			GameId = gameId;
			EndTime = endTime;
		}
	}
}
