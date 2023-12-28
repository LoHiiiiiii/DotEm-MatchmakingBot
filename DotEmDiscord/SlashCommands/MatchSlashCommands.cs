using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using DotemChatMatchmaker;
using DotemDiscord.Utils;
using Discord.Rest;
using DotemDiscord.Messages;
using DotemModel;

namespace DotemDiscord.SlashCommands
{
    public class MatchSlashCommands : InteractionModuleBase<SocketInteractionContext<SocketSlashCommand>> {

		private readonly ChatMatchmaker _chatMatchmaker;

		public MatchSlashCommands(ChatMatchmaker chatMatchmaker) {
			_chatMatchmaker = chatMatchmaker;
		}

		[SlashCommand("match", "Matches you for games for a certain period of time")]
		[Alias("m")]
		public async Task SearchMatchSlashCommand(string? gameIds = null, int? time = null, int? playerCount = null, string? description = null) {
			try {
				if (Context.Guild == null) {
					await RespondAsync("This command cannot be used in a direct message!");
					return;
				}
				await DeferAsync();
				var idArray = gameIds?.Split(' ');
				var result = await _chatMatchmaker.SearchSessionAsync(
					serverId: Context.Guild.Id.ToString(),
					userId: Context.User.Id.ToString(),

					channelId: Context.Channel.Id.ToString(),
					gameIds: idArray,
					durationMinutes: time,
					playerCount: playerCount,
					description: description,
					allowSuggestions: false // TODO: Change
				);

				RestInteractionMessage message = (RestInteractionMessage)await GetOriginalResponseAsync();
				var structure = MessageStructures.GetNoSearchStructure();

				while (true) {
					if (result is SessionResult.Suggestions suggestions) { } else if (result is SessionResult.Found found) { } else break;
				}

				if (result is SessionResult.Matched matched) {
					MessageStructures.GetMatchedStructure(matched.gameId, matched.playerIds, matched.description);
				}

				if (result is SessionResult.Waiting waiting) {
					SearchMessage.Create(Context.Client, _chatMatchmaker, message, waiting.waits, Context.User.Id);
					structure = MessageStructures.GetWaitingStructure(waiting.waits, Context.User.Id);
				}

				await ModifyOriginalResponseAsync(x => {
					x.Content = structure.content;
					x.Components = structure.components;
				});
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportSlashCommandException(Context.Interaction);
			}
		}
	}
}