using Newtonsoft.Json.Linq;

namespace DotemExtensions {
	public class SteamHandler {
		public string LobbyLinkPrefix { get; set; } = "steam://joinlobby";
		public string ApiKey { get; }

		public SteamHandler(string apiKey, string? lobbyLinkPrefix = null) {
			ApiKey = apiKey;
			if (lobbyLinkPrefix != null) { LobbyLinkPrefix = lobbyLinkPrefix; }
		}

		public record SteamResult {
			public bool Successful { get; init; }
			public string? LobbyLink { get; init; }
			public string? GameName { get; init; }
			public bool SteamIdBad { get; init; }
			public bool ProbablyPrivate { get; init; }
		}

		public async Task<SteamResult> GetLobbyLink(ulong steamId) {
			using (var client = new HttpClient()) {
				var result = await client.GetAsync($"http://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={ApiKey}&steamids={steamId}");
				if (!result.IsSuccessStatusCode) {
					return new SteamResult();
				}

				var json = await result.Content.ReadAsStringAsync();

				var response = (JObject?)JObject.Parse(json)["response"];
				if (response == null) { return new SteamResult(); }
				var players = (JArray?)response.GetValue("players");
				if (players == null || !players.Any()) { return new SteamResult() { Successful = true, SteamIdBad = true }; }
				var summary = (JObject?) players.First();
				if (summary == null) { return new SteamResult(); }

				var hasName = summary.ContainsKey("personaname");
				var gameId = summary.ContainsKey("gameid") ? summary["gameid"] : null;
				var lobbySteamId = summary.ContainsKey("lobbysteamid") ? summary["lobbysteamid"]?.ToString() : null;
				var gameName = summary.ContainsKey("gameextrainfo") ? summary["gameextrainfo"]?.ToString() : null;

				if (hasName && gameId == null) {
					return new SteamResult() { Successful = true, ProbablyPrivate = true };
				}

				var lobbyLink = (gameId != null  && lobbySteamId != null) 
					? $"{LobbyLinkPrefix}/{gameId}/{lobbySteamId}/{steamId}"
					: null;

				return new SteamResult() {
					Successful = true,
					LobbyLink = lobbyLink,
					GameName = gameName,
				};
			}
		}
	}
}
