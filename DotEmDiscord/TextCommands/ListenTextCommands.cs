using Discord.Commands;
using DotemDiscord.Utils;
using Discord;
using DotemMatchmaker.Context;
using DotemExtensions;

namespace DotemDiscord.SlashCommands {
	public class ListenTextCommands : ModuleBase<SocketCommandContext> {

		private readonly MatchmakingContext _matchmakingContext;
		private readonly ExtensionContext _extensionContext;

		public ListenTextCommands(MatchmakingContext matchmakingContext, ExtensionContext extensionContext) {
			_matchmakingContext = matchmakingContext;
			_extensionContext = extensionContext;
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

				(var rawIds, var hours) = ParseCommands(commands);
				var gameIds = ContentFilter.CapSymbolCount(rawIds)
					.Where(s => !string.IsNullOrWhiteSpace(s))
					.ToArray();

				if (gameIds.Length == 0) {
					var channelDefaults = await _extensionContext.GetChannelDefaultSearchParamatersAsync(Context.Channel.Id.ToString());
					gameIds = channelDefaults.gameIds;
				}

				if (gameIds.Length == 0) {
					await Context.Message.ReplyAsync(
						text: "Please give non-empty Game Ids.",
						allowedMentions: AllowedMentions.None
					);
					return;
				}

				var serverId = Context.Guild.Id.ToString();
				var names = await _matchmakingContext.GetGameNamesAsync(serverId, gameIds);

				DateTimeOffset? expireTime = hours != null ? DateTimeOffset.Now.AddHours((double)hours) : null;
				await _matchmakingContext.AddMatchListenAsync(serverId, Context.User.Id.ToString(), expireTime, gameIds);

				var natural = MessageStructures.GetNaturalLanguageString([.. names.Values]);

				await Context.Message.ReplyAsync(
					text: $"Listening for {natural} {(hours == null ? "forever" : $"for {hours} hours")}."
				);
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportTextCommandExceptionAsync(Context.Message);
			}
		}

		[Command("sl", RunMode = RunMode.Async)]
		[Alias("show-listens")]
		public async Task ListListensTextCommandAsync() {
			try {
				static string ExpiryString(DateTimeOffset? expireTime) => expireTime == null
					? "forever"
					: $"until <t:{expireTime.Value.ToUnixTimeSeconds()}:f>";

				var userId = Context.User.Id.ToString();

				if (Context.Guild != null) {
					var serverId = Context.Guild.Id.ToString();
					var listens = (await _matchmakingContext.GetUserServerListensAsync(serverId, userId)).ToArray();

					if (!listens.Any()) {
						await Context.Message.ReplyAsync(text: "Not listening for any games in this server.");
						return;
					}

					var names = await _matchmakingContext.GetGameNamesAsync(serverId, listens.Select(l => l.gameId).ToArray());
					var lines = listens.Select(l => {
						var name = names.GetValueOrDefault(l.gameId, l.gameId);
						var display = name != l.gameId ? $"{name} ({l.gameId})" : l.gameId;
						return $"- {display}: {ExpiryString(l.expireTime)}";
					});
					await Context.Message.ReplyAsync(text: string.Join("\n", lines));
				} else {
					var listens = (await _matchmakingContext.GetUserListensAsync(userId)).ToArray();

					if (!listens.Any()) {
						await Context.Message.ReplyAsync(text: "Not listening for any games.");
						return;
					}

					var sections = new List<string>();

					foreach (var serverGroup in listens.GroupBy(l => l.serverId)) {
						var guild = Context.Client.GetGuild(ulong.Parse(serverGroup.Key));
						var serverName = guild?.Name ?? serverGroup.Key;
						var serverGameIds = serverGroup.Select(l => l.gameId).ToArray();
						var names = await _matchmakingContext.GetGameNamesAsync(serverGroup.Key, serverGameIds);
						var gameLines = serverGroup.Select(l => {
							var name = names.GetValueOrDefault(l.gameId, l.gameId);
							var display = name != l.gameId ? $"{name} ({l.gameId})" : l.gameId;
							return $"- {display}: {ExpiryString(l.expireTime)}";
						});
						sections.Add($"**{serverName}**\n{string.Join("\n", gameLines)}");
					}

					await Context.Message.ReplyAsync(text: string.Join("\n\n", sections));
				}
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
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
					text: $"Stopped listening for {natural}."
				);
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportTextCommandExceptionAsync(Context.Message);
			}
		}

		(string[] gameIds, int? hours) ParseCommands(string[] split) {
			List<string> games = new List<string>();
			List<int> times = new List<int>();

			for (int i = 0; i < split.Length; i++) {
				if (string.IsNullOrWhiteSpace(split[i])) continue;
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
