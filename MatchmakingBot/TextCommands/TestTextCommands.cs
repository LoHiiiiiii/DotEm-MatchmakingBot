using Discord;
using Discord.Commands;

namespace DotemDiscord.TextCommands {
	public class TestTextCommands : ModuleBase<SocketCommandContext> {
		[Command("echo")]
		public async Task EchoTextCommand(string echo) => await ReplyAsync(echo);
	}
}
