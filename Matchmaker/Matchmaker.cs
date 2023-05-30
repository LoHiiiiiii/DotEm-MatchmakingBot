using Matchmaker.Model;

namespace Matchmaker {
	public class Matchmaker {


		public async Task<SearchResult> TryMatchAsync(string serverId, string playerId, string game, int expireTime) { //TODO: Add playercount and details
			await Task.CompletedTask;

			return new SearchResult();
		}
	}
}