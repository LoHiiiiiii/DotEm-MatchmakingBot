using Discord.Commands;
using DotemDiscord.Utils;
using Discord;
using DotemMatchmaker;
using DotemMatchmaker.Context;

namespace DotemDiscord.SlashCommands {
	public class ListenTextCommands : ModuleBase<SocketCommandContext> {

		private readonly Matchmaker _matchmaker;
		private readonly MatchmakingContext _matchmakingContext;

		public ListenTextCommands(Matchmaker matchmaker, MatchmakingContext matchmakingContext) {
			_matchmaker = matchmaker;
			_matchmakingContext = matchmakingContext;
		}

		[Command("l", RunMode = RunMode.Async)]
		[Alias("listen")]
		public async Task ListenMatchesTextCommandAsync(params string[] commands) {
			try {
				if (Context.Guild == null) {
					await Context.Message.ReplyAsync(
						text: "This command cannot be used in a direct message!",
						allowedMentions: AllowedMentions.None
					);
					return;
				}

				var forbidden = ContentFilter.ContainsForbidden(commands);

				if (forbidden != null) {
					var forbiddenStructure = MessageStructures.GetForbiddenStructure(forbidden);

					await Context.Message.ReplyAsync(text: forbiddenStructure.content,
						components: forbiddenStructure.components,
						allowedMentions: AllowedMentions.None
					);

					return;
				}

				(var gameIds, var hours) = ParseCommands(commands);
				var serverId = Context.Guild.Id.ToString();
				var names = (await _matchmakingContext.GetGameNamesAsync(serverId, gameIds));

				DateTimeOffset? expireTime = hours != null ? DateTimeOffset.Now.AddHours((double)hours!) : null;
				await _matchmakingContext.AddMatchListenAsync(serverId, Context.User.Id.ToString(), expireTime, gameIds);

				var natural = MessageStructures.GetNaturalLanguageString(names.Values.ToArray());

				await Context.Message.ReplyAsync(
					text: $"Listening for {natural} {(hours == null ? "forever" : $"for {hours} hours")}."
				);
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportTextCommandExceptionAsync(Context.Message);
			}
		}

		[Command("lc", RunMode = RunMode.Async)]
		public async Task CancelMatchListensTextCommandAsync(params string[] commands) {
			try {
				if (Context.Guild == null) {
					await Context.Message.ReplyAsync(
						text: "This command cannot be used in a direct message!",
						allowedMentions: AllowedMentions.None
					);
					return;
				}

				var forbidden = ContentFilter.ContainsForbidden(commands);

				if (forbidden != null) {
					var forbiddenStructure = MessageStructures.GetForbiddenStructure(forbidden);

					await Context.Message.ReplyAsync(text: forbiddenStructure.content,
						components: forbiddenStructure.components,
						allowedMentions: AllowedMentions.None
					);

					return;
				}

				(var gameIds, var hours) = ParseCommands(commands);
				var serverId = Context.Guild.Id.ToString();
				var names = (await _matchmakingContext.GetGameNamesAsync(serverId, gameIds));

				await _matchmakingContext.DeleteMatchListensAsync(serverId, Context.User.Id.ToString(), gameIds);

				var natural = names.Any()
					? MessageStructures.GetNaturalLanguageString(names.Values.ToArray())
					: "everything";

				await Context.Message.ReplyAsync(
					text: $"Stopped listening {natural}."
				);
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportTextCommandExceptionAsync(Context.Message);
			}
		}

		(string[] gameIds, int? hours) ParseCommands(string[] split) {
			List<string> games = new List<string>();
			List<int> times = new List<int>();

			for (int i = 0; i < split.Length; i++) {
				if (int.TryParse(split[i], out var parsed)) {
					if (parsed > 0) times.Add(parsed);
					continue;
				}
				games.Add(split[i]);
			}

			return (
				gameIds: games?.ToArray() ?? [],
				hours: times.Any() ? times.Min() : null
			);
		}
	}
}