using Discord.Commands;
using DotemDiscord.Utils;
using Discord;
using DotemMatchmaker;
using DotemExtensions;
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
		public async Task ListenMatchesTextCommandAsync(string gameIds, int? hours = null) {
			try {
				if (Context.Guild == null) {
					await Context.Message.ReplyAsync(
						text: "This command cannot be used in a direct message!",
						allowedMentions: AllowedMentions.None
					);
					return;
				}

				if (ContentFilter.ContainsForbidden(gameIds)) {
					var forbiddenStructure = MessageStructures.GetForbiddenStructure(gameIds);

					await Context.Message.ReplyAsync(text: forbiddenStructure.content,
						components: forbiddenStructure.components,
						allowedMentions: AllowedMentions.None
					);

					return;
				}

				var idArray = gameIds.Split(' ');
				var serverId = Context.Guild.Id.ToString();

				var names = (await _matchmakingContext.GetGameNamesAsync(serverId, idArray));

				DateTimeOffset? expireTime = hours != null ? DateTimeOffset.Now.AddHours((double)hours!) : null;
				await _matchmakingContext.AddMatchListenAsync(serverId, Context.User.Id.ToString(), expireTime, idArray);

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
		public async Task CancelMatchListensTextCommandAsync(string? gameIds = null) {
			try {
				if (Context.Guild == null) {
					await Context.Message.ReplyAsync(
						text: "This command cannot be used in a direct message!",
						allowedMentions: AllowedMentions.None
					);
					return;
				}

				if (gameIds != null && ContentFilter.ContainsForbidden(gameIds)) {
					var forbiddenStructure = MessageStructures.GetForbiddenStructure(gameIds);

					await Context.Message.ReplyAsync(text: forbiddenStructure.content,
						components: forbiddenStructure.components,
						allowedMentions: AllowedMentions.None
					);

					return;
				}

				var idArray = gameIds?.Split(' ') ?? [];
				var serverId = Context.Guild.Id.ToString();

				var names = (await _matchmakingContext.GetGameNamesAsync(serverId, idArray));

				await _matchmakingContext.DeleteMatchListensAsync(serverId, Context.User.Id.ToString(), idArray);

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
	}
}