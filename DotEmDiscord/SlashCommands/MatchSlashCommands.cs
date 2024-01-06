using Discord.Interactions;
using Discord.WebSocket;
using DotemChatMatchmaker;
using DotemDiscord.Utils;
using DotemModel;
using Discord;
using DotemDiscord.Handlers;

namespace DotemDiscord.SlashCommands {
	public class MatchSlashCommands : InteractionModuleBase<SocketInteractionContext<SocketSlashCommand>> {

		private readonly ChatMatchmaker _chatMatchmaker;
		private readonly ButtonMessageHandler _buttonMessageHandler;

		public MatchSlashCommands(ChatMatchmaker chatMatchmaker, ButtonMessageHandler buttonMessageHandler) {
			_chatMatchmaker = chatMatchmaker;
			_buttonMessageHandler = buttonMessageHandler;
		}

		[SlashCommand("match", "Searches for match in games for a certain period of time")]
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
					allowSuggestions: true
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
						client: Context.Client,
						matchmaker: _chatMatchmaker,
						interaction: Context.Interaction,
						joinableSessions: suggestions.suggestedSessions,
							durationMinutes: time ?? _chatMatchmaker.DefaultDurationMinutes,
							searchParams: suggestions.allowWait
								? (gameIds: idArray,
									description: description,
									playerCount: playerCount)
								: null
					);
				}

				if (result is SessionResult.Matched matched) {
					structure = MessageStructures.GetMatchedStructure(matched.matchedSession.GameId, matched.playerIds, matched.description);
				}

				if (result is SessionResult.Waiting waiting) {
					_buttonMessageHandler.CreateSearchMessage(Context.Client, _chatMatchmaker, message, waiting.waits, Context.User.Id);
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
				await ExceptionHandling.ReportInteractionException(Context.Interaction);
			}
		}

		[SlashCommand("cancel-matches-mc", "Cancels all or specific searches you are in")]
		public async Task CancelMatchSlashCommand(string? gameIds = null) {
			try {
				if (Context.Guild == null) {
					await RespondAsync("This command cannot be used in a direct message!");
					return;
				}
				await DeferAsync(ephemeral: true);
				var serverId = Context.Guild.Id.ToString();
				var userId = Context.User.Id.ToString();

				var idArray = gameIds?.Split(' ') ?? [];
				var structure = MessageStructures.GetStoppedStructure();
				if (!idArray.Any()) {
					await _chatMatchmaker.LeaveAllPlayerSessionsAsync(serverId, userId);
				} else {
					var left = await _chatMatchmaker.LeaveSessionsAsync(serverId, userId, idArray);
					structure = MessageStructures.GetStoppedStructure(
						left.Select(sd => _chatMatchmaker.GameNameHandler.GetGameIdFullName(sd.GameId)).ToArray()
					);
				}

				await ModifyOriginalResponseAsync(x => {
					x.Content = structure.content;
					x.Components = structure.components;
				});
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionException(Context.Interaction);
			}
		}
	}
}