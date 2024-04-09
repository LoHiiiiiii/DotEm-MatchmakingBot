using Discord.Commands;
using DotemDiscord.Utils;
using Discord;
using DotemExtensions;

namespace DotemDiscord.TextCommands {
	public class SteamTextCommands : ModuleBase<SocketCommandContext> {

		private readonly ExtensionContext _extensionContext;
		private readonly SteamHandler _steamHandler;

		public SteamTextCommands(ExtensionContext extensionContext, SteamHandler steamHandler) {
			_extensionContext = extensionContext;
			_steamHandler = steamHandler;
		}

		[Command("lobby", RunMode = RunMode.Async)]
		public async Task GetSteamLobbyTextCommandAsync(string? _ = null) { // Param so recognized even if extra stuff
			try {
				var id = await _extensionContext.GetSteamUserAsync(Context.User.Id.ToString());
				if (id == null) {
					await Context.Message.ReplyAsync(text: "First register your Steam Id.");
					return;
				}
				var result = await _steamHandler.GetLobbyLink((ulong)id);
				if (!result.Successful) {
					await Context.Message.ReplyAsync(text: "Problem with retrieving data from Steam.");
					return;
				}
				if (result.SteamIdBad) {
					await Context.Message.ReplyAsync(text: $"Your registered Steam Id ({id}) wasn't found on Steam.");
					return;
				}
				if (result.ProbablyPrivate) {
					await Context.Message.ReplyAsync(text: 
						$"Some info was found, but not the private info that is required for the link. Check your steam privacy settings."
					);
					return;
				}
				if (result.LobbyLink == null) {
					await Context.Message.ReplyAsync(text:"No lobby found.");
					return;
				}
				var prefix = result.GameName != null ? $"{result.GameName} - " : "";
				await Context.Message.ReplyAsync(text: $"{prefix}{result.LobbyLink}");
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportTextCommandExceptionAsync(Context.Message);
			}
		}
	}
}