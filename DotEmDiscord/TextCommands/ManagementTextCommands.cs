using Discord.Commands;
using DotemDiscord.Utils;
using Discord;
using DotemExtensions;
using Discord.Interactions;
using Discord.Net;

namespace DotemDiscord.TextCommands {
	public class ManagementTextCommands : ModuleBase<SocketCommandContext> {

		private readonly InteractionService _interactionService;

		public ManagementTextCommands(InteractionService interactionService) {
			_interactionService = interactionService;
		}

		[Discord.Commands.RequireOwner]
		[Command("install-slash", RunMode = Discord.Commands.RunMode.Async)]
		public async Task GetSteamLobbyTextCommandAsync() {
			try {
				await _interactionService.AddCommandsGloballyAsync();
				await Context.User.SendMessageAsync(text: "Global registration done");
			} catch (Exception e) {
				if (e is HttpException http) {
					if (http.DiscordCode == DiscordErrorCode.CannotSendMessageToUser) { return; }
				}
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportTextCommandExceptionAsync(Context.Message);
			}
		}
	}
}