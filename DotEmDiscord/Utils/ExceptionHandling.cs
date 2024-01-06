using Discord;
using Discord.WebSocket;

namespace DotemDiscord.Utils {
	public static class ExceptionHandling {

		private static string internalError = "Internal server error!";

		public async static Task ReportInteractionException(SocketInteraction interaction) {
			try {
				if (!interaction.HasResponded) {
					await interaction.RespondAsync(text: internalError, ephemeral: true);
					return;
				}
				await interaction.FollowupAsync(text: internalError, ephemeral: true);
			}
			catch(Exception e) {
				Console.WriteLine(e);
			}
		}

		public async static Task ReportTextCommandException(SocketUserMessage message) {
			try {
				await message.ReplyAsync(
					text: internalError,
					allowedMentions: new()
				);
			} catch (Exception e) {
				Console.WriteLine(e);
			}
		}
	}
}
