using Discord;
using DotemModel;

namespace DotemDiscord.Utils {

	public static class MessageStructures {
		//TODO: Move to Envs to allow translations

		public const int BUTTON_LABEL_MAXLENGTH = 77;
		public const string CANCEL_ID = "cancel";

		public static (string? content, MessageComponent? components) GetWaitingStructure(IEnumerable<SessionDetails> waits, ulong? userId) {
			if (!waits.Any()) { return ("No sessions! Shouldn't happen!", null); }

			IEnumerable<DateTimeOffset> expireTimes;

			var stringId = userId?.ToString();

			var userExpires = stringId == null ? Enumerable.Empty<DateTimeOffset>()
				: waits
					.Where(w => w.UserExpires.ContainsKey(stringId))
					.Select(w => w.UserExpires[stringId]);
			
			expireTimes = userExpires.Any()
				? userExpires
				: waits.SelectMany(w => w.UserExpires.Values);

			if (!expireTimes.Any()) { return ("No users waiting! Shouldn't happen!", null); }

			return (
				content: $"Search expiration <t:{expireTimes.Min().ToUnixTimeSeconds()}:R>.",
				components: GetJoinButtons(waits).Build()
			);
		}

		public static (string? content, MessageComponent? components) GetSuggestionStructure(IEnumerable<SessionDetails> joinables, ulong userId, Guid? searchId, bool allowCancel) {

			var builder = GetJoinButtons(joinables, userId.ToString());

			if (searchId != null) {
				builder.WithButton(
					label: "Search",
					style: ButtonStyle.Success,
					customId: searchId.ToString(),
					row: 1
				);
			}

			if (allowCancel) {
				builder.WithButton(
					label: "Ignore",
					style: ButtonStyle.Danger,
					customId: CANCEL_ID
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

				var label = $"{details.GameName}{description}";

				if (label.Length > BUTTON_LABEL_MAXLENGTH - playerCount.Length) {
					label = $"{details.GameId}{description}";
					if (label.Length > BUTTON_LABEL_MAXLENGTH - playerCount.Length - 3) {
						label = label.Substring(0, BUTTON_LABEL_MAXLENGTH - playerCount.Length - 3) + "...";
					}
				}

				label += playerCount;

				builder.WithButton(
					label: label,
					style: style,
					disabled: disablerId != null && details.UserExpires.ContainsKey(disablerId),
					customId: details.SessionId.ToString()

				 // Shared with different messages that search the same game,
				 // but the message that the button was pressed in is also checked
				 );
			}
			return builder;
		}

		public static (string? content, MessageComponent? components) GetMatchedStructure(string gameName, IEnumerable<string> playerIds, string? description) {
			if (!playerIds.Any()) { return ("Players missing!", null); }

			var ids = playerIds.ToArray();

			var mentions = GetNaturalLanguageString(ids.Select(s => $"<@{s}>").ToArray());

			return (
				content: $"Match found! {mentions} on {gameName}{(description != null
				? $" ({description})"
				: "")}.",
				components: null
			);
		}

		public static (string? content, MessageComponent? components) GetStoppedStructure(params string[] gameNames) {
			if (!gameNames.Any()) return ("No longer searching.", null);
			return ($"No longer searching for {GetNaturalLanguageString(gameNames)}.", null);
		}

		public static (string? content, MessageComponent? components) GetCanceledStructure() {
			return ("Canceled searching for everything.", null);
		}

		public static (string? content, MessageComponent? components) GetSuggestionsFinishedStructure() {
			return ("Suggestions handled.", null);
		}

		public static (string? content, MessageComponent? components) GetSuggestionsWaitStructure(bool dms = false) {
			return ($"Waiting for user to handle suggestion{(dms ? " (Check your DMs)" : "")}.", null);
		}

		public static (string? content, MessageComponent? components) GetNoSearchStructure() {
			return ("No search.", null);
		}

		public static (string? content, MessageComponent? components) GetFailedJoinStructure() {
			return ("Failed to join. The session might have just expired or gotten canceled or filled.", null);
		}

		public static (string? content, MessageComponent? components) GetForbiddenStructure(string text) {
			return ($"Your input \"{text}\" contains forbidden formatting.", null);
		}

		public static string GetNaturalLanguageString(string[] strings) {
			var natural = strings.FirstOrDefault();
			if (strings.Length > 1) {
				for (int i = 1; i < strings.Length - 1; ++i) {
					natural += $", {strings[i]}";
				}

				natural += $" and {strings[strings.Length - 1]}";
			}
			return natural ?? "";
		}
	}
}
