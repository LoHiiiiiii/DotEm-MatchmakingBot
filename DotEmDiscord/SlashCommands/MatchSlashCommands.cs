using Discord.Interactions;
using Discord.WebSocket;
using DotemExtensions;
using DotemDiscord.Utils;
using DotemModel;
using Discord;
using DotemDiscord.Handlers;
using DotemMatchmaker;

namespace DotemDiscord.SlashCommands {
	public class MatchSlashCommands : InteractionModuleBase<SocketInteractionContext<SocketSlashCommand>> {

		private readonly Matchmaker _matchmaker;
		private readonly ExtensionContext _extensionContext;
		private readonly ButtonMessageHandler _buttonMessageHandler;

		public MatchSlashCommands(Matchmaker matchmaker, ExtensionContext extensionContext, ButtonMessageHandler buttonMessageHandler) {
			_matchmaker = matchmaker;
			_extensionContext = extensionContext;
			_buttonMessageHandler = buttonMessageHandler;
		}

		[EnabledInDm(false)]
		[SlashCommand("match", "Searches for match in games for a certain period of time")]
		public async Task SearchMatchSlashCommandAsync(string? gameIds = null, int? time = null, int? maxPlayerCount = null, string? description = null) {
			try {
				await DeferAsync();

				if (gameIds != null && ContentFilter.ContainsForbidden(gameIds)) {
					var forbiddenStructure = MessageStructures.GetForbiddenStructure(gameIds);

					await ModifyOriginalResponseAsync(x => {
						x.Content = forbiddenStructure.content;
						x.Components = forbiddenStructure.components;
						x.AllowedMentions = AllowedMentions.None;
					});

					return;
				}

				if (description != null && ContentFilter.ContainsForbidden(description)) {
					var forbiddenStructure = MessageStructures.GetForbiddenStructure(description);

					await ModifyOriginalResponseAsync(x => {
						x.Content = forbiddenStructure.content;
						x.Components = forbiddenStructure.components;
						x.AllowedMentions = AllowedMentions.None;
					});

					return;
				}

				var idArray = gameIds?.Split(' ') ?? [];

				var channelDefaults = await _extensionContext.GetChannelDefaultSearchParamatersAsync(Context.Channel.Id.ToString());
				IUserMessage message = await GetOriginalResponseAsync();

				idArray = ContentFilter.CapSymbolCount(idArray);
				if (time != null) time = ContentFilter.CapSearchDuration((int)time);
				if (maxPlayerCount != null) maxPlayerCount = ContentFilter.CapPlayerCount((int)maxPlayerCount);
				if (description != null) description = ContentFilter.CapSymbolCount(description);

				var customParams = idArray.Any(s => !string.IsNullOrWhiteSpace(s) && !string.IsNullOrWhiteSpace(s));

				if (customParams) {
					await _extensionContext.SetUserRematchParameters(
						serverId: Context.Guild.Id.ToString(),
						userId: Context.User.Id.ToString(),
						gameIds: string.Join(" ", idArray),
						maxPlayerCount: maxPlayerCount,
						duration: time,
						description: description
					);
				}

				(var waitedForInput, var content, var components) = await HandleSearchAsync(
					message: message,
					gameIds: customParams ? idArray : channelDefaults.gameIds,
					duration: customParams ? time : time ?? channelDefaults.duration,
					maxPlayerCount: customParams ? maxPlayerCount : maxPlayerCount ?? channelDefaults.maxPlayerCount,
					description: description
				);

				if (waitedForInput) {
					await FollowupAsync(
						text: content,
						components: components
					);
					return;
				}

				await ModifyOriginalResponseAsync(x => {
					x.Content = content;
					x.Components = components;
				});
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}

		[EnabledInDm(false)]
		[SlashCommand("rematch", "Uses your previous search in this server again.")]
		public async Task RematchSlashCommandAsync() {
			try {
				await DeferAsync();

				var result = (await _extensionContext.GetUserRematchParameters(
					Context.Guild.Id.ToString(),
					Context.User.Id.ToString()
				));

				if (!result.HasValue) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = "No previous search stored.";
					});
					return;
				}

				IUserMessage message = await GetOriginalResponseAsync();

				(var waitedForInput, var content, var components) = await HandleSearchAsync(
					message: message,
					gameIds: result.Value.gameIds,
					duration: result.Value.duration,
					maxPlayerCount: result.Value.maxPlayerCount,
					description: result.Value.description
				);

				if (waitedForInput) {
					await FollowupAsync(
						text: content,
						components: components
					);
					return;
				}
				await ModifyOriginalResponseAsync(x => {
					x.Content = content;
					x.Components = components;
				});
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}

		private async Task<(bool waitedForInput, string? content, MessageComponent? components)> HandleSearchAsync(
			IUserMessage message,
			string[] gameIds,
			int? duration,
			int? maxPlayerCount,
			string? description
		) {

			if (duration != null) duration = ContentFilter.CapSearchDuration((int)duration);
			gameIds = ContentFilter.CapSymbolCount(gameIds);
			if (maxPlayerCount != null) maxPlayerCount = ContentFilter.CapPlayerCount((int)maxPlayerCount);
			if (description != null) description = ContentFilter.CapSymbolCount(description);

			var result = await _matchmaker.SearchSessionAsync(
				serverId: Context.Guild.Id.ToString(),
				userId: Context.User.Id.ToString(),
				gameIds: gameIds,
				joinDuration: duration,
				maxPlayerCount: maxPlayerCount,
				description: description
			);

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
						durationMinutes: duration ?? _matchmaker.DefaultJoinDurationMinutes,
						searchParams: suggestions.allowWait
							? (gameIds: gameIds,
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
			return (waitedForInput, structure.content, structure.components);
		}

		[EnabledInDm(false)]
		[SlashCommand("cancel-matches-mc", "Cancels all or specific searches you are in")]
		public async Task CancelMatchSlashCommandAsync(string? gameIds = null) {
			try {

				await DeferAsync(ephemeral: true);
				var serverId = Context.Guild.Id.ToString();
				var userId = Context.User.Id.ToString();

				if (gameIds != null && ContentFilter.ContainsForbidden(gameIds)) {
					var forbiddenStructure = MessageStructures.GetForbiddenStructure(gameIds);

					await ModifyOriginalResponseAsync(x => {
						x.Content = forbiddenStructure.content;
						x.Components = forbiddenStructure.components;
						x.AllowedMentions = AllowedMentions.None;
					});

					return;
				}

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