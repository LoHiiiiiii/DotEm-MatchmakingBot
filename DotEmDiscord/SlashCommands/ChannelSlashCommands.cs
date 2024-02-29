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
		public async Task SetChannelDefaultSearchParameters(string gameIds, int? time = null, int? maxPlayerCount = null, string? description = null) {
			try {
				await DeferAsync();

				await _extensionContext.SetChannelDefaultParametersAsync(
					Context.Channel.Id.ToString(),
					gameIds: gameIds,
					maxPlayerCount: maxPlayerCount,
					duration: time,
					description
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
		public async Task DeleteChannelDefaultSearchParameters() {
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
		public async Task GetChannelDefaultParameters() {
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
		[SlashCommand("set-board", "Makes the current channel not a matchmaking board.")]
		public async Task SetChannelAsMatchmakingBoardAsync() {
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
		[SlashCommand("remove-board", "Makes the current channel not a matchmaking board.")]
		public async Task RemoveChannelAsMatchmakingBoardAsync() {
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