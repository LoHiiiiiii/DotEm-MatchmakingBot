using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using DotemDiscord.Handlers;
using Matchmaker.Model;

namespace DotemDiscord.SlashCommands {
	public class MatchSlashCommands : InteractionModuleBase<SocketInteractionContext<SocketSlashCommand>> {

		private readonly SearchMessageHandler _searchMessageHandler;

		public MatchSlashCommands(SearchMessageHandler searchMessageHandler) {
			_searchMessageHandler = searchMessageHandler;
		}

		[SlashCommand("match", "Matches you for a games for a certain period of time")]
		[Alias("m")]
		public async Task SearchMatchSlashCommand(string gameIds, int duration = 30) {
			await DeferAsync();
			var idArray = gameIds.Split(' ');
			var expireTime = DateTimeOffset.Now.AddMinutes(duration);
			var searches = idArray.Select(id => new SearchDetails(id, id, expireTime)).ToArray();
			var message = await GetOriginalResponseAsync();
			var buttons = await _searchMessageHandler.AddMessage(message, searches);
			await ModifyOriginalResponseAsync(x => { 
				x.Content = $"Searching until {expireTime.ToLocalTime().ToString("HH.mm")}"; 
				x.Components = buttons; 
			});
		}
	}
}
