using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace DotemDiscord.SlashCommands {
	public class TestSlashCommands : InteractionModuleBase<SocketInteractionContext<SocketSlashCommand>> {
		[SlashCommand("echo", "Echoes whatever you say")]
		public async Task EchoSlashCommand(string echo) => await RespondAsync(echo);
	}
}
