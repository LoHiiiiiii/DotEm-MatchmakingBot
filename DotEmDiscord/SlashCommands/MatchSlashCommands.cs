using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using DotemDiscord.Handlers;
using DotemChatMatchmaker;
using DotemSearchResult = DotemModel.SearchResult;
using DotemDiscord.Utils;
using Discord.Rest;

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
				description: description,
				allowSuggestions: false // TODO: Change
			);

			RestInteractionMessage message = (RestInteractionMessage) await GetOriginalResponseAsync();
			var structure = result.GetFailedAcceptStructure();


			while (result is DotemSearchResult.Suggestions) {
				// TODO: Handle suggestion
				break;
			}

			if (result is DotemSearchResult.Found) {

			}

			if (result is DotemSearchResult.Searching searching) {
				_searchMessageHandler.AddMessage(message, searching.searches);
				structure = searching.GetStructure();

			}

			await message.ModifyAsync(x => {
				x.Content = structure.content;
				x.Components = structure.components;
			});
		}
	}
}