using Discord.WebSocket;
using Discord;
using Discord.Net;
using Discord.Commands;

namespace DotemDiscord.Handlers {
	public class JokeHandler {
		private readonly string[] muikeaPrefixes = new [] { ",m", "-m"};

		public async Task<bool> TryMuikeaAsync(SocketUserMessage message) {
			var hasMuikea = HasMuikeaPrefix(message);
			if (hasMuikea) {
				await ReactMuikeaAsync(message);
			}
			return hasMuikea;
		}

		private bool HasMuikeaPrefix(SocketUserMessage message) {
			var argPos = 0;
			foreach (var muikea in muikeaPrefixes) {
				if (message.HasStringPrefix(muikea, ref argPos)) {
					return true;
				}
			}
			return false;
		}

		private async Task ReactMuikeaAsync(SocketMessage message) {
			try {
				var muikea = Emote.Parse("<:muikea:296390386547556352>");
				await message.AddReactionAsync(muikea);
			} catch (HttpException exception) {
				if (exception.DiscordCode != DiscordErrorCode.UnknownEmoji) throw;
				var backupMuikea = new Emoji("😏");
				await message.AddReactionAsync(backupMuikea);
			}
		}
	}
}
