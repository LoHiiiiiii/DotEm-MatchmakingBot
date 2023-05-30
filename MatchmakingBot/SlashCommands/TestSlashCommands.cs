using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace DotemDiscord.SlashCommands {
	public class TestSlashCommands : InteractionModuleBase<SocketInteractionContext<SocketSlashCommand>> {
		[SlashCommand("echo", "Echoes whatever you say")]
		public async Task EchoSlashCommand(string echo) => await RespondAsync(echo);

		[SlashCommand("test", "Does something")]
		public async Task TestSlashCommand() {
			var id = new Guid();
			var components = new ComponentBuilder().WithButton("Test", id.ToString()).Build();
			await DeferAsync();
			await ModifyOriginalResponseAsync(x => {
				x.Content = "Test";
			});
		}
	}
}
