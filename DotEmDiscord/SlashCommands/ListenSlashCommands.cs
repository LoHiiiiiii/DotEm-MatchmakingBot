using Discord.Interactions;
using Discord.WebSocket;
using DotemDiscord.Utils;
using DotemMatchmaker;
using DotemMatchmaker.Context;
using Discord;
using Discord.Net;

namespace DotemDiscord.SlashCommands {
	public class ListenSlashCommands : InteractionModuleBase<SocketInteractionContext<SocketSlashCommand>> {

		private readonly Matchmaker _matchmaker;
		private readonly MatchmakingContext _matchmakingContext;

		public ListenSlashCommands(Matchmaker matchmaker, MatchmakingContext matchmakingContext) {
			_matchmaker = matchmaker;
			_matchmakingContext = matchmakingContext;
		}

		[EnabledInDm(false)]
		[SlashCommand("listen", "Sends you messages when a player searches for these games.")]
		public async Task ListenMatchesSlashCommandAsync(string gameIds, int? hours = null) {
			try {
				await DeferAsync();

				if (ContentFilter.ContainsForbidden(gameIds)) {
					var forbiddenStructure = MessageStructures.GetForbiddenStructure(gameIds);

					await ModifyOriginalResponseAsync(x => {
						x.Content = forbiddenStructure.content;
						x.Components = forbiddenStructure.components;
						x.AllowedMentions = AllowedMentions.None;
					});

					return;
				}

				var idArray = ContentFilter.CapSymbolCount(gameIds.Split(' '));

				var serverId = Context.Guild.Id.ToString();

				var names = await _matchmaker.GetGameNamesAsync(serverId, idArray);

				DateTimeOffset? expireTime = hours != null ? DateTimeOffset.Now.AddHours((double)hours!) : null;
				await _matchmakingContext.AddMatchListenAsync(serverId, Context.User.Id.ToString(), expireTime, idArray);

				var natural = MessageStructures.GetNaturalLanguageString(names.Values.ToArray());

				await ModifyOriginalResponseAsync(x => {
					x.Content = $"Listening for {natural} {(hours == null
						? "forever"
						: $"until <t:{DateTimeOffset.Now.AddHours((int)hours).ToUnixTimeSeconds()}:f>")}.";
				});
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				if (e is HttpException unknown && unknown.DiscordCode == DiscordErrorCode.UnknownInteraction) return;
				if (e is HttpException acknowledged && acknowledged.DiscordCode == DiscordErrorCode.InteractionHasAlreadyBeenAcknowledged) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}

		[SlashCommand("show-listens", "Shows what games you are listening for.")]
		public async Task ShowListensSlashCommandAsync() {
			try {
				await DeferAsync(ephemeral: true);

				var userId = Context.User.Id.ToString();

				static string ExpiryString(DateTimeOffset? expireTime) => expireTime == null
					? "forever"
					: $"until <t:{expireTime.Value.ToUnixTimeSeconds()}:f>";

				if (Context.Guild != null) {
					var serverId = Context.Guild.Id.ToString();
					var listens = (await _matchmakingContext.GetUserServerListensAsync(serverId, userId)).ToArray();

					if (!listens.Any()) {
						await ModifyOriginalResponseAsync(x => { x.Content = "Not listening for any games in this server."; });
						return;
					}

					var names = await _matchmakingContext.GetGameNamesAsync(serverId, [.. listens.Select(l => l.gameId)]);
					var lines = listens.Select(l => {
						var name = names.GetValueOrDefault(l.gameId, l.gameId);
						var display = name != l.gameId ? $"{name} ({l.gameId})" : l.gameId;
						return $"- {display}: {ExpiryString(l.expireTime)}";
					});
					await ModifyOriginalResponseAsync(x => { x.Content = string.Join("\n", lines); });
				} else {
					var listens = (await _matchmakingContext.GetUserListensAsync(userId)).ToArray();

					if (!listens.Any()) {
						await ModifyOriginalResponseAsync(x => { x.Content = "Not listening for any games."; });
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

					await ModifyOriginalResponseAsync(x => { x.Content = string.Join("\n\n", sections); });
				}
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				if (e is HttpException unknown && unknown.DiscordCode == DiscordErrorCode.UnknownInteraction) return;
				if (e is HttpException acknowledged && acknowledged.DiscordCode == DiscordErrorCode.InteractionHasAlreadyBeenAcknowledged) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}

		[EnabledInDm(false)]
		[SlashCommand("cancel-listens-lc", "Cancels all or specific ids you are listening for")]
		public async Task CancelMatchListensSlashCommandAsync(string? gameIds = null) {
			try {
				await DeferAsync(ephemeral: true);

				if (gameIds != null && ContentFilter.ContainsForbidden(gameIds)) {
					var forbiddenStructure = MessageStructures.GetForbiddenStructure(gameIds);

					await ModifyOriginalResponseAsync(x => {
						x.Content = forbiddenStructure.content;
						x.Components = forbiddenStructure.components;
						x.AllowedMentions = AllowedMentions.None;
					});

					return;
				}

				var idArray = gameIds?.Split(' ') ?? [];
				var serverId = Context.Guild.Id.ToString();

				var names = await _matchmakingContext.GetGameNamesAsync(serverId, idArray);

				await _matchmakingContext.DeleteMatchListensAsync(serverId, Context.User.Id.ToString(), idArray);

				var natural = names.Any()
					? MessageStructures.GetNaturalLanguageString(names.Values.ToArray())
					: "everything";

				await ModifyOriginalResponseAsync(x => {
					x.Content = $"Stopped listening for {natural}.";
				});
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				if (e is HttpException unknown && unknown.DiscordCode == DiscordErrorCode.UnknownInteraction) return;
				if (e is HttpException acknowledged && acknowledged.DiscordCode == DiscordErrorCode.InteractionHasAlreadyBeenAcknowledged) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}
	}
}
