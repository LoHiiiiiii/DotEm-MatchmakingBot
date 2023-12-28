using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace DotemDiscord.SlashCommands {
	public class TestSlashCommands : InteractionModuleBase<SocketInteractionContext<SocketSlashCommand>> {
		[SlashCommand("echo", "Echoes whatever you say")]
		public async Task EchoSlashCommand(string echo) {
			try {
				await RespondAsync(echo);
			}
			catch (Exception e) { Console.WriteLine(e); }	
		}

		[SlashCommand("test", "Does something")]
		public async Task TestSlashCommand() {
			Console.Write("Wha");
			await RespondAsync("Test", ephemeral: true);
			await Task.Delay(3000);
			await ModifyOriginalResponseAsync(x => {
				x.Content = "s";
				x.Flags = MessageFlags.None;
			});
			await DeleteOriginalResponseAsync();
		}
	}
}
