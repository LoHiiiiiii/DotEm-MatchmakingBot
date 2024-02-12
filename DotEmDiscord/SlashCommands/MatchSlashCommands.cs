using Discord.Interactions;
using Discord.WebSocket;
using DotemChatMatchmaker;
using DotemDiscord.Utils;
using DotemModel;
using Discord;
using DotemDiscord.Handlers;
using DotemMatchmaker;

namespace DotemDiscord.SlashCommands {
	public class MatchSlashCommands : InteractionModuleBase<SocketInteractionContext<SocketSlashCommand>> {

		private readonly Matchmaker _matchmaker;
		private readonly ExtensionContext _chatContext;
		private readonly ButtonMessageHandler _buttonMessageHandler;

		public MatchSlashCommands(Matchmaker matchmaker, ExtensionContext chatContext, ButtonMessageHandler buttonMessageHandler) {
			_matchmaker = matchmaker;
			_chatContext = chatContext;
			_buttonMessageHandler = buttonMessageHandler;
		}

		[SlashCommand("match", "Searches for match in games for a certain period of time")]
		public async Task SearchMatchSlashCommandAsync(string? gameIds = null, int? time = null, int? maxPlayerCount = null, string? description = null) {
			try {
				if (Context.Guild == null) {
					await RespondAsync("This command cannot be used in a direct message!");
					return;
				}
				await DeferAsync();
				var idArray = gameIds?.Split(' ') ?? [];

				var channelDefaults = _chatContext.GetChannelDefaultSearchParamaters(Context.Channel.ToString() ?? "");

				var result = await _matchmaker.SearchSessionAsync(
					serverId: Context.Guild.Id.ToString(),
					userId: Context.User.Id.ToString(),
					gameIds: idArray ?? channelDefaults.gameIds,
					joinDuration: time ?? channelDefaults.duration,
					maxPlayerCount: maxPlayerCount ?? channelDefaults.maxPlayerCount,
					description: description
				);

				IUserMessage message = await GetOriginalResponseAsync();
				var structure = MessageStructures.GetNoSearchStructure();
				var waitedForInput = false;

				while (result is SessionResult.Suggestions suggestions) {
					if (!waitedForInput) {
						var inputStructure = MessageStructures.GetSuggestionsWaitStructure();
						await ModifyOriginalResponseAsync(x => {
							x.Content = inputStructure.content;
							x.Components = inputStructure.components;
						});
						waitedForInput = true;
					}

					result = await _buttonMessageHandler.GetSuggestionResultAsync(
						interaction: Context.Interaction,
						joinableSessions: suggestions.suggestedSessions,
							durationMinutes: time ?? _matchmaker.DefaultJoinDurationMinutes,
							searchParams: suggestions.allowWait
								? (gameIds: idArray,
									description: description,
									playerCount: maxPlayerCount)
								: null
					);
				}

				if (result is SessionResult.Matched matched) {
					structure = MessageStructures.GetMatchedStructure(
						matched.matchedSession.GameId, 
						matched.matchedSession.UserExpires.Keys, 
						matched.matchedSession.Description);
				}

				if (result is SessionResult.Waiting waiting) {
					await _buttonMessageHandler.CreateSearchMessageAsync(message, waiting.waits, Context.User.Id);
					structure = MessageStructures.GetWaitingStructure(waiting.waits, Context.User.Id);
				}

				if (waitedForInput) {
					await FollowupAsync(
						text: structure.content,
						components: structure.components
					);
					return;
				}
				await ModifyOriginalResponseAsync(x => {
					x.Content = structure.content;
					x.Components = structure.components;
				});
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}

		[SlashCommand("cancel-matches-mc", "Cancels all or specific searches you are in")]
		public async Task CancelMatchSlashCommandAsync(string? gameIds = null) {
			try {
				if (Context.Guild == null) {
					await RespondAsync("This command cannot be used in a direct message!");
					return;
				}
				await DeferAsync(ephemeral: true);
				var serverId = Context.Guild.Id.ToString();
				var userId = Context.User.Id.ToString();

				var idArray = gameIds?.Split(' ') ?? [];

				var structure = MessageStructures.GetCanceledStructure();
				if (!idArray.Any()) {
					await _matchmaker.LeaveAllPlayerSessionsAsync(serverId, userId);
				} else {
					var userSessions = await _matchmaker.GetUserSessionsAsync(serverId, userId);
					(var updated, var stopped) = await _matchmaker.LeaveGamesAsync(serverId, userId, idArray);

					var leftIds = updated
						.Select(s => s.SessionId)
						.Concat(stopped)
						.ToHashSet();

					var gameNames = userSessions
						.Where(s => leftIds.Contains(s.SessionId))
						.Select(s => s.GameName)
						.ToArray();

					structure = MessageStructures.GetStoppedStructure(gameNames);
				}

				await ModifyOriginalResponseAsync(x => {
					x.Content = structure.content;
					x.Components = structure.components;
				});
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}
	}
}