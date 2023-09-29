
namespace DotemChatMatchmaker {
	public class ChatGameNameHandler {
		public string GetGameIdAlias(string gameId) => gameId.ToLowerInvariant();
		public string GetGameIdFullName(string gameId) => gameId.ToLowerInvariant();
	}
}
