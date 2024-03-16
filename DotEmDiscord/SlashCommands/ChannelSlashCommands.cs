using Discord.Interactions;
using Discord.WebSocket;
using DotemExtensions;
using DotemDiscord.Utils;
using Discord;
using DotemDiscord.Handlers;

namespace DotemDiscord.SlashCommands {
	public class ChannelSlashCommands : InteractionModuleBase<SocketInteractionContext<SocketSlashCommand>> {

		private readonly ExtensionContext _extensionContext;
		private readonly MatchmakingBoardHandler _matchmakingBoardHandler;

		public ChannelSlashCommands(ExtensionContext extensionContext, MatchmakingBoardHandler matchmakingBoardHandler) {
			_extensionContext = extensionContext;
			_matchmakingBoardHandler = matchmakingBoardHandler;
		}

		[EnabledInDm(false)]
		[DefaultMemberPermissions(GuildPermission.ManageGuild)]
		[SlashCommand("set-default-search", "Sets default search parameters for the channel.")]
		public async Task SetChannelDefaultSearchParametersSlashCommandAsync(string gameIds, int? time = null, int? maxPlayerCount = null, string? description = null) {
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

				var split = gameIds.Split(" ");
				if (!split.Any(s => !string.IsNullOrWhiteSpace(s) && !string.IsNullOrWhiteSpace(s))) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = "Please give non-empty Game Ids.";
					});

					return;
				}

				await _extensionContext.SetChannelDefaultParametersAsync(
					Context.Channel.Id.ToString(),
					gameIds: string.Join(" ", ContentFilter.CapSymbolCount(split)),
					maxPlayerCount: maxPlayerCount != null ? ContentFilter.CapPlayerCount((int)maxPlayerCount) : maxPlayerCount,
					duration: time != null ? ContentFilter.CapSearchDuration((int)time) : time,
					description: description != null ? ContentFilter.CapSymbolCount(description) : description
				);

				await ModifyOriginalResponseAsync(x => {
					x.Content = "Set the defaults for channel.";
				});
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}

		[EnabledInDm(false)]
		[DefaultMemberPermissions(GuildPermission.ManageGuild)]
		[SlashCommand("remove-default-search", "Removes any default search parameters from the channel.")]
		public async Task DeleteChannelDefaultSearchParametersSlashCommandAsync() {
			try {
				await DeferAsync();

				await _extensionContext.DeleteChannelDefaultParametersAsync(Context.Channel.Id.ToString());

				await ModifyOriginalResponseAsync(x => {
					x.Content = "Removed the defaults.";
				});
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}


		[EnabledInDm(false)]
		[SlashCommand("get-default-search", "Gets default search parameters for the channel.")]
		public async Task GetChannelDefaultParametersSlashCommandAsync() {
			try {
				await DeferAsync(true);

				var channelDefaults = await _extensionContext.GetChannelDefaultSearchParamatersAsync(Context.Channel.Id.ToString());

				var response = "No defaults for the channel.";

				if (channelDefaults.gameIds.Any()) {
					response = $"Game IDs: {string.Join(", ", channelDefaults.gameIds)}";
					if (channelDefaults.maxPlayerCount != null) { response += $"\nMax Player Count: {channelDefaults.maxPlayerCount}"; }
					if (channelDefaults.duration != null) { response += $"\nSearch duration: {channelDefaults.duration}"; }
					if (channelDefaults.description != null) { response += $"\nDescription: {channelDefaults.description}"; }
				}

				await ModifyOriginalResponseAsync(x => {
					x.Content = response;
				});
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}

		[EnabledInDm(false)]
		[DefaultMemberPermissions(GuildPermission.ManageGuild)]
		[SlashCommand("set-board", "Makes the current channel not a matchmaking board.")]
		public async Task SetChannelAsMatchmakingBoardSlashCommandAsync() {
			try {
				await DeferAsync(true);
				await _extensionContext.AddMatchmakingBoardAsync(Context.Guild.Id.ToString(), Context.Channel.Id.ToString());
				await _matchmakingBoardHandler.PostActiveMatchesAsync(Context.Guild.Id, Context.Channel.Id);
				await ModifyOriginalResponseAsync(x => {
					x.Content = "Set the channel as a matchmaking board.";
				});
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}

		[EnabledInDm(false)]
		[DefaultMemberPermissions(GuildPermission.ManageGuild)]
		[SlashCommand("remove-board", "Makes the current channel not a matchmaking board.")]
		public async Task RemoveChannelAsMatchmakingBoardSlashCommandAsync() {
			try {
				await DeferAsync(true);
				await _extensionContext.DeleteMatchmakingBoardAsync(Context.Channel.Id.ToString());
				await ModifyOriginalResponseAsync(x => {
					x.Content = "Channel is no longer a matchmaking board.";
				});
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}
	}
}