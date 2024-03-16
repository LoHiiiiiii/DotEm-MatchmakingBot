using Discord.Interactions;
using Discord.WebSocket;
using DotemDiscord.Utils;
using DotemMatchmaker;
using DotemMatchmaker.Context;
using Discord;

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

				var names = (await _matchmaker.GetGameNamesAsync(serverId, idArray));

				DateTimeOffset? expireTime = hours != null ? DateTimeOffset.Now.AddHours((double)hours!) : null;
				await _matchmakingContext.AddMatchListenAsync(serverId, Context.User.Id.ToString(), expireTime, idArray);

				var natural = MessageStructures.GetNaturalLanguageString(names.Values.ToArray());

				await ModifyOriginalResponseAsync(x => {
					x.Content = $"Listening for {natural} {(hours == null 
						? "forever" 
						: $"until <t:{DateTimeOffset.Now.AddHours((int)hours).ToUnixTimeSeconds()}:f>")
					}.";
				});
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
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
					x.Content = $"Stopped listening {natural}.";
				});
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}
	}
}