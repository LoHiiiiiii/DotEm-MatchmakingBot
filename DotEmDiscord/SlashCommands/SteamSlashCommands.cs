using Discord.Interactions;
using Discord.WebSocket;
using DotemDiscord.Utils;
using DotemExtensions;

namespace DotemDiscord.SlashCommands {
	public class SteamSlashCommands : InteractionModuleBase<SocketInteractionContext<SocketSlashCommand>> {

		private readonly ExtensionContext _extensionContext; 
		private readonly SteamHandler _steamHandler;

		public SteamSlashCommands(ExtensionContext extensionContext, SteamHandler steamHandler) {
			_extensionContext = extensionContext;
			_steamHandler = steamHandler;
		}

		[SlashCommand("lobby", "Gets the link to a Steam Lobby.")]
		public async Task GetSteamLobbySlashCommandAsync() {
			try {
				await DeferAsync();
				var id = await _extensionContext.GetSteamUserAsync(Context.User.Id.ToString());
				if (id == null) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = "First register your Steam Id.";
					});
					return;
				}
				var result = await _steamHandler.GetLobbyLink((ulong)id);
				if (!result.Successful) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = "Problem with retrieving data from Steam.";
					});
					return;
				}
				if (result.SteamIdBad) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = $"Your registered Steam Id ({id}) wasn't found on Steam.";
					});
					return;
				}
				if (result.ProbablyPrivate) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = $"Some info was found, but not the private info that is required for the link. Check your stema privacy settings.";
					});
					return;
				}
				if (result.LobbyLink == null) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = "No lobby found.";
					});
					return;
				}
				var prefix = result.GameName != null ? $"{result.GameName} - " : "";
				await ModifyOriginalResponseAsync(x => {
					x.Content = $"{prefix}{result.LobbyLink}";
				});

			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}

		[SlashCommand("add-steam-id", "Registers a steamid to this account")]
		public async Task AddSteamIdSlashCommandAsync(string steamId) {
			try {
				await DeferAsync();
				if (!ulong.TryParse(steamId, out var parsedId)) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = $"Please give a valid id.";
					});
					return;
				}
				await _extensionContext.AddSteamUserAsync(Context.User.Id.ToString(), parsedId);
				await ModifyOriginalResponseAsync(x => {
					x.Content = $"Added a steam id to this user.";
				});
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}

		[SlashCommand("remove-steam-id", "Removes a steamid from this account")]
		public async Task RemoveSteamIdSlashCommandAsync() {
			try {
				await DeferAsync();
				await _extensionContext.DeleteSteamUserAsync(Context.User.Id.ToString());
				await ModifyOriginalResponseAsync(x => {
					x.Content = $"Removed steam-id from this user.";
				});
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}
	}
}