using Discord.WebSocket;

namespace DotemDiscord.Utils {
	public class ExceptionHandling {

		public async static Task ReportSlashCommandException(SocketInteraction interaction) {
			try {
				string response = "Internal server error!";
				if (!interaction.HasResponded) {
					await interaction.RespondAsync(text: response, ephemeral: true);
					return;
				}
				await interaction.FollowupAsync(text: response, ephemeral: true);
			}
			catch(Exception e) {
				Console.WriteLine(e);
			}
		}
	}
}
