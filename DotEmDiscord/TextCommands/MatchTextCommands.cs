using Discord.Commands;
using DotemDiscord.Utils;
using DotemModel;
using Discord;
using DotemDiscord.Handlers;
using DotemMatchmaker;
using DotemChatMatchmaker;

namespace DotemDiscord.TextCommands {
	public class MatchTextCommands : ModuleBase<SocketCommandContext> {

		private readonly Matchmaker _matchmaker;
		private readonly ExtensionContext _chatContext;
		private readonly ButtonMessageHandler _buttonMessageHandler;

		public MatchTextCommands(Matchmaker matchmaker, ExtensionContext chatContext, ButtonMessageHandler buttonMessageHandler) {
			_matchmaker = matchmaker;
			_chatContext = chatContext;
			_buttonMessageHandler = buttonMessageHandler;
		}

		[Command("m", RunMode = RunMode.Async)]
		public async Task SearchMatchTextCommandsAsync(params string[] commands) {
			try {
				if (Context.Guild == null) {
					await Context.Message.ReplyAsync(
						text: "This command cannot be used in a direct message!",
						allowedMentions: AllowedMentions.None
					);
					return;
				}

				(var gameIds, var time, var maxPlayerCount, var description) = ParseCommand(commands);

				var channelDefaults = _chatContext.GetChannelDefaultSearchParamaters(Context.Channel.ToString() ?? "");

				var result = await _matchmaker.SearchSessionAsync(
					serverId: Context.Guild.Id.ToString(),
					userId: Context.User.Id.ToString(),
					gameIds: gameIds ?? channelDefaults.gameIds,
					joinDuration: time ?? channelDefaults.duration,
					maxPlayerCount: maxPlayerCount ?? channelDefaults.maxPlayerCount,
					description: description
				);


				var structure = MessageStructures.GetNoSearchStructure(); 
				IUserMessage? responseMessage = null;

				while (result is SessionResult.Suggestions suggestions) {
					if (responseMessage == null) {
						var inputStructure = MessageStructures.GetSuggestionsWaitStructure();
						responseMessage = await Context.Message.ReplyAsync(
							text: inputStructure.content,
							components: inputStructure.components,
							allowedMentions: AllowedMentions.None
						);
					}

					result = await _buttonMessageHandler.GetSuggestionResultAsync(
						client: Context.Client,
						matchmaker: _matchmaker,
						user: Context.User,
						joinableSessions: suggestions.suggestedSessions,
						durationMinutes: time ?? _matchmaker.DefaultJoinDurationMinutes,
						searchParams: suggestions.allowWait
							? (gameIds: gameIds,
								description,
								maxPlayerCount)
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
					structure = MessageStructures.GetWaitingStructure(waiting.waits, Context.User.Id);
					var message = await Context.Message.ReplyAsync(
						text: structure.content,
						components: structure.components,
						allowedMentions: AllowedMentions.None
					);
					await _buttonMessageHandler.CreateSearchMessageAsync(Context.Client, _matchmaker, message, waiting.waits, Context.User.Id);
					return;
				}

				if (responseMessage != null) {
					try {
						await responseMessage.DeleteAsync();
					} catch { }
				}

				await Context.Message.ReplyAsync(
					text: structure.content,
					components: structure.components,
					allowedMentions: result is SessionResult.Matched
						? null
						: AllowedMentions.None
				);
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportTextCommandException(Context.Message);
			}
		}

		[Command("mc", RunMode = RunMode.Async)]
		[Alias("c")]
		public async Task CancelMatchTextCommandAsync(params string[] gameIds) {
			try {
				if (Context.Guild == null) {
					await Context.Message.ReplyAsync("This command cannot be used in a direct message!");
					return;
				}

				var serverId = Context.Guild.Id.ToString();
				var userId = Context.User.Id.ToString();

				var structure = MessageStructures.GetCanceledStructure();
				if (!gameIds.Any()) {
					await _matchmaker.LeaveAllPlayerSessionsAsync(serverId, userId);
				} else {
					var userSessions = await _matchmaker.GetUserSessionsAsync(serverId, userId);
					(var updated, var stopped) = await _matchmaker.LeaveGamesAsync(serverId, userId, gameIds);
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

				await Context.Message.ReplyAsync(
					text: structure.content,
					components: structure.components,
					allowedMentions: AllowedMentions.None
				);
			} catch (Exception e) {
				Console.WriteLine(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportTextCommandException(Context.Message);
			}
		}

		enum CommandType {
			None,
			Description,
			Time,
			PlayerCount
		}

		Dictionary<string, CommandType> commandMap = new() {
			{ "-d", CommandType.Description },
			{ "-t", CommandType.Time },
			{ "-p", CommandType.PlayerCount },
			{ "-c", CommandType.PlayerCount }
		};

		(string[] gameIds, int? time, int? playerCount, string? description) ParseCommand(string[] split) {
			List<string> games = new List<string>();
			List<int> playerCounts = new List<int>();
			List<int> times = new List<int>();
			string? description = null;

			var currentType = CommandType.None;
			for (int i = 0; i < split.Length; i++) {
				if (commandMap.TryGetValue(split[i], out var type)) {
					currentType = type;
					continue;
				}
				if (currentType == CommandType.Description) {
					description = string.Join(" ", split, i, split.Length - i);
					break;
				} else if (currentType == CommandType.PlayerCount) {
					if (!int.TryParse(split[i], out var parsed)) { continue; }
					playerCounts.Add(parsed);
				} else if (currentType == CommandType.Time) {
					if (!int.TryParse(split[i], out var parsed)) { continue; }
					times.Add(parsed);
				} else {
					if (int.TryParse(split[i], out var parsed)) {
						times.Add(parsed);
						continue;
					}
					games.Add(split[i]);
				}
			}

			return (
				gameIds: games.ToArray(),
				time: times.Any()
					 ? times.Max()
					: null,
				playerCount: playerCounts.Any()
					? playerCounts.Max()
					: null,
				description: description
			);
		}
	}
}