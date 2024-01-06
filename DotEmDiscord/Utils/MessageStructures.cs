using Discord;
using DotemModel;

namespace DotemDiscord.Utils {

	public static class MessageStructures {
		//TODO: Move to Envs to allow translations

		public static (string? content, MessageComponent? components) GetWaitingStructure(IEnumerable<SessionDetails> waits, ulong userId) {
			if (!waits.Any()) { return ("No sessions! Shouldn't happen!", null); }
			var stringId = userId.ToString();

			var userExpires = waits
				.Where(w => w.UserExpires.ContainsKey(stringId))
				.Select(w => w.UserExpires[stringId]);

			var expireTimes = userExpires.Any()
				? userExpires
				: waits.SelectMany(w => w.UserExpires.Values);

			if (!expireTimes.Any()) { return ("No users waiting! Shouldn't happen!", null); }

			return (
				content: $"Searching expires <t:{expireTimes.Min().ToUnixTimeSeconds()}:R>.",
				components: GetJoinButtons(waits).Build()
			);
		}

		public static (string? content, MessageComponent? components) GetSuggestionStructure(IEnumerable<SessionDetails> joinables, ulong userId, Guid? searchId) {

			var builder = GetJoinButtons(joinables, userId.ToString());

			if (searchId != null) {
				builder.WithButton(
					label: $"Search",
					style: ButtonStyle.Success,
					customId: searchId.ToString(),
					row: 1
				);
			}

			return (
				content: $"Joinable searches:",
				components: builder.Build()
			);
		}

		private static ComponentBuilder GetJoinButtons(IEnumerable<SessionDetails> waits, string? disablerId = null) {
			var builder = new ComponentBuilder();

			foreach (var details in waits) {
				var description = details.Description != null
						? $" ({details.Description})"
						: "";
				var playerCount = details.MaxPlayerCount == 2
					? ""
					: $" - {details.UserExpires.Count}/{details.MaxPlayerCount}";
				var style = (details.MaxPlayerCount == details.UserExpires.Count + 1)
					? ButtonStyle.Primary
					: ButtonStyle.Secondary;
				builder.WithButton(
					label: $"{details.GameId}{description}{playerCount}",
					style: style,
					disabled: disablerId != null && details.UserExpires.ContainsKey(disablerId),
					customId: details.SessionId.ToString()

				 // Shared with different messages that search the same game,
				 // but the message that the button was pressed in is also checked
				 );
			}
			return builder;
		}

		public static (string? content, MessageComponent? components) GetMatchedStructure(string gameName, string[] playerIds, string? description) {
			if (!playerIds.Any()) { return ("Players missing!", null); }

			var mentions = $"<@{playerIds[0]}>";


			if (playerIds.Length > 1) {
				for (int i = 1; i < playerIds.Length - 1; ++i) {
					mentions += $", <@{playerIds[i]}>";
				}

				mentions += $" and <@{playerIds[playerIds.Length - 1]}>";
			}

			return (
				content: $"Match found! {mentions} on {gameName}{(description != null
				? $" ({description})"
				: "")}.",
				components: null
			);
		}

		public static (string? content, MessageComponent? components) GetStoppedStructure() {
			return ("No longer searching.", null);
		}

		public static (string? content, MessageComponent? components) GetSuggestionsFinishedStructure() {
			return ("Suggestions handled.", null);
		}
		public static (string? content, MessageComponent? components) GetSuggestionsWaitStructure() {
			return ("Waiting user to handle suggestions.", null);
		}

		public static (string? content, MessageComponent? components) GetStoppedStructure(string[] gameNames) {
			if (!gameNames.Any()) return GetStoppedStructure();
			var gameString = gameNames[0];
			if (gameNames.Length >= 2) {
				for (int i = 1; i < gameNames.Length - 1; ++i) {
					gameString += $", {gameNames[i]}";
				}
				gameString += $" and {gameNames[gameNames.Length - 1]}";
			}
			return ($"No longer searching for {gameString}.", null);
		}

		public static (string? content, MessageComponent? components) GetNoSearchStructure() {
			return ("No search.", null);
		}

		public static (string? content, MessageComponent? components) GetFailedJoinStructure() {
			return ("Failed to join.", null);
		}

	}
}
