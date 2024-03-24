using Discord;
using Discord.WebSocket;

namespace DotemDiscord.Utils {
	public static class ExceptionHandling {

		private const string INTERNAL_ERROR = "Internal server error!";
		private const string OUTPUT_FILE = "exceptions.txt";

		public async static Task ReportInteractionExceptionAsync(SocketInteraction interaction) {
			try {
				if (!interaction.HasResponded) {
					await interaction.RespondAsync(text: INTERNAL_ERROR, ephemeral: true);
					return;
				}
				await interaction.FollowupAsync(text: INTERNAL_ERROR, ephemeral: true);
			}
			catch(Exception e) {
				ReportExceptionToFile(e);
			}
		}

		public async static Task ReportTextCommandExceptionAsync(SocketUserMessage message) {
			try {
				await message.ReplyAsync(
					text: INTERNAL_ERROR,
					allowedMentions: new()
				);
			} catch (Exception e) {
				ReportExceptionToFile(e);
			}
		}

		public static void ReportExceptionToFile(Exception e) {
			var output = $"{e.GetType().Name}: {e.Message}{e.StackTrace}";
			Console.WriteLine(output);
			using (var writer = File.AppendText(OUTPUT_FILE)) {
				writer.WriteLine(output);
			}
		}
	}
}
