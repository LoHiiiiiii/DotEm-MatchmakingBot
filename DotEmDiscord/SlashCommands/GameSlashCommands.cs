
using Discord.Interactions;
using Discord.WebSocket;
using DotemDiscord.Utils;
using Discord;
using DotemMatchmaker.Context;

namespace DotemDiscord.SlashCommands {
	public class GameSlashCommands : InteractionModuleBase<SocketInteractionContext<SocketSlashCommand>> {

		private readonly MatchmakingContext _matchmakingContext;

		public GameSlashCommands(MatchmakingContext matchmakingContext) {
			_matchmakingContext = matchmakingContext;
		}

		[EnabledInDm(false)]
		[DefaultMemberPermissions(GuildPermission.ManageGuild)]
		[SlashCommand("set-game-defaults", "Sets default search parameters for a Game ID.")]
		public async Task SetGameDefaultSearchParametersSlashCommandAsync(string gameId, int? maxPlayerCount = null, string? description = null) {
			try {
				await DeferAsync();

				if (ContentFilter.ContainsForbidden(gameId)) {
					var forbiddenStructure = MessageStructures.GetForbiddenStructure(gameId);

					await ModifyOriginalResponseAsync(x => {
						x.Content = forbiddenStructure.content;
						x.Components = forbiddenStructure.components;
						x.AllowedMentions = AllowedMentions.None;
					});

					return;
				}

				if (string.IsNullOrEmpty(gameId) || string.IsNullOrWhiteSpace(gameId)) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = "Game ID cannot be empty.";
					});
					return;
				}
				var split = gameId.Split(' ');
				if (split.Length > 1) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = "Game ID cannot contain a space.";
					});
					return;
				}

				await _matchmakingContext.SetGameDefaultAsync(
					Context.Guild.Id.ToString(),
					gameId: ContentFilter.CapSymbolCount(gameId),
					maxPlayerCount: maxPlayerCount != null ? ContentFilter.CapPlayerCount((int)maxPlayerCount) : null,
					description: description != null ? ContentFilter.CapSymbolCount(description) : null
				);

				await ModifyOriginalResponseAsync(x => {
					x.Content = $"Set the defaults for Game ID {gameId}.";
				});
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}

		[EnabledInDm(false)]
		[DefaultMemberPermissions(GuildPermission.ManageGuild)]
		[SlashCommand("remove-game-defaults", "Removes any default search parameters from the Game IDs.")]
		public async Task DeleteGameDefaultSearchParametersSlashCommandAsync(string gameIds) {
			try {
				await DeferAsync();

				var split = gameIds.Split(' ');

				await _matchmakingContext.DeletGameDefaultsAsync(Context.Channel.Id.ToString());

				await ModifyOriginalResponseAsync(x => {
					x.Content = $"Removed the defaults from {MessageStructures.GetNaturalLanguageString(split)}.";
				});
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}


		[EnabledInDm(false)]
		[SlashCommand("get-game-defaults", "Gets default search parameters for all games.")]
		public async Task GetGameDefaultSearchParametersSlashCommandAsync() {
			try {
				await DeferAsync(true);

				var gameDefaults = await _matchmakingContext.GetAllGameDefaultsAsync(Context.Channel.Id.ToString());

				var response = "No defaults for Game IDs in this server";

				if (gameDefaults.Any()) {
					var rows = gameDefaults.
						Select(pair =>
							$"Game ID: {pair.Key}" +
							$"{(pair.Value.maxPlayerCount != null ? $", Max Playercount: {pair.Value.maxPlayerCount})" : "")}" +
							$"{(pair.Value.description != null ? $", Description: {pair.Value.description})" : "")}"
						);
					response = string.Join("\n", rows);
				}

				await ModifyOriginalResponseAsync(x => {
					x.Content = response;
				});
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}
	}
}