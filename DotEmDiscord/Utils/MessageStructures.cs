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
					customId: details.SessionId.ToString()
				 // Shared with different messages that search the same game,
				 // but the message that the button was pressed in is also checked
				 );
			}

			return (
				content: $"Searching expires <t:{expireTimes.Min().ToUnixTimeSeconds()}:R>.",
				components: builder.Build()
			); ;
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

		public static (string? content, MessageComponent? components) GetNoSearchStructure() {
			return ("No search.", null);
		}

		public static (string? content, MessageComponent? components) GetFailedJoinStructure() {
			return ("Couldn't join.", null);
		}

	}
}
