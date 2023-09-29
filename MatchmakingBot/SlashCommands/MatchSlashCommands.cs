using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using DotemDiscord.Handlers;
using DotemChatMatchmaker;
using DotemSearchResult = DotemModel.SearchResult;

namespace DotemDiscord.SlashCommands {
	public class MatchSlashCommands : InteractionModuleBase<SocketInteractionContext<SocketSlashCommand>> {

		private readonly SearchMessageHandler _searchMessageHandler; 
		private readonly ChatMatchmaker _chatMatchmaker;

		public MatchSlashCommands(SearchMessageHandler searchMessageHandler, ChatMatchmaker chatMatchmaker) {
			_searchMessageHandler = searchMessageHandler;
			_chatMatchmaker = chatMatchmaker;
		}

		[SlashCommand("match", "Matches you for games for a certain period of time")]
		[Alias("m")]
		public async Task SearchMatchSlashCommand(string? gameIds = null, int? duration = null, int? playerCount = null, string? description = null) {
			if (Context.Guild == null) {
				await RespondAsync("This command cannot be used in a direct message!");
				return;
			}
			await DeferAsync();
			var idArray = gameIds?.Split(' ');
			var result = await _chatMatchmaker.SearchAsync(
				serverId: Context.Guild.Id.ToString(),
				userId: Context.User.Id.ToString(),
				channelId: Context.Channel.Id.ToString(),
				gameIds: idArray,
				durationMinutes: duration,
				playerCount: playerCount,
				description: description
			);

			if (result is DotemSearchResult.Suggestions) {
				// Handle suggestion
			}

			if (result is DotemSearchResult.Found) {

			}

			if (result is DotemSearchResult.Searching) { }

				//var buttons = await _searchMessageHandler.AddMessage(message, searches);


			await ModifyOriginalResponseAsync(x => {
				x.Content = "No search.";
			});
		}
	}
}