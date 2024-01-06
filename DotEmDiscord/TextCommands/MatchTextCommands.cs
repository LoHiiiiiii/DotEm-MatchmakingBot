using Discord.Commands;
using DotemChatMatchmaker;
using DotemDiscord.Utils;
using DotemModel;
using Discord;
using DotemDiscord.Handlers;

namespace DotemDiscord.TextCommands {
	public class MatchTextCommands : ModuleBase<SocketCommandContext> {

		private readonly ChatMatchmaker _chatMatchmaker;
		private readonly ButtonMessageHandler _buttonMessageHandler;

		public MatchTextCommands(ChatMatchmaker chatMatchmaker, ButtonMessageHandler buttonMessageHandler) {
			_chatMatchmaker = chatMatchmaker;
			_buttonMessageHandler = buttonMessageHandler;
		}

		[Command("m", RunMode = RunMode.Async)]
		public async Task SearchMatchTextCommands(params string[] commands) {
			try {
				if (Context.Guild == null) {
					await Context.Message.ReplyAsync(
						text: "This command cannot be used in a direct message!",
						allowedMentions: AllowedMentions.None
					);
					return;
				}

				(var games, var time, var playerCount, var description) = ParseCommand(commands);

				var result = await _chatMatchmaker.SearchSessionAsync(
					serverId: Context.Guild.Id.ToString(),
					userId: Context.User.Id.ToString(),
					channelId: Context.Channel.Id.ToString(),
					gameIds: games.ToArray(),
					durationMinutes: time,
					playerCount: playerCount,
					description: description,
					allowSuggestions: true
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
						matchmaker: _chatMatchmaker,
						user: Context.User,
						joinableSessions: suggestions.suggestedSessions,
						durationMinutes: time ?? _chatMatchmaker.DefaultDurationMinutes,
						searchParams: suggestions.allowWait
							? (gameIds: games,
								description,
								playerCount)
							: null
					);
				}
				if (result is SessionResult.Matched matched) {
					structure = MessageStructures.GetMatchedStructure(matched.matchedSession.GameId, matched.playerIds, matched.description);
				}

				if (result is SessionResult.Waiting waiting) {
					structure = MessageStructures.GetWaitingStructure(waiting.waits, Context.User.Id);
					var message = await Context.Message.ReplyAsync(
						text: structure.content,
						components: structure.components,
						allowedMentions: AllowedMentions.None
					);
					_buttonMessageHandler.CreateSearchMessage(Context.Client, _chatMatchmaker, message, waiting.waits, Context.User.Id);
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
		public async Task CancelMatchSlashCommand(params string[] gameIds) {
			try {
				if (Context.Guild == null) {
					await Context.Message.ReplyAsync("This command cannot be used in a direct message!");
					return;
				}

				var serverId = Context.Guild.Id.ToString();
				var userId = Context.User.Id.ToString();

				var structure = MessageStructures.GetStoppedStructure();
				if (!gameIds.Any()) {
					await _chatMatchmaker.LeaveAllPlayerSessionsAsync(serverId, userId);
				} else {
					var left = await _chatMatchmaker.LeaveSessionsAsync(serverId, userId, gameIds);
					structure = MessageStructures.GetStoppedStructure(
						left.Select(sd => _chatMatchmaker.GameNameHandler.GetGameIdFullName(sd.GameId)).ToArray()
					);
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
					description = string.Join(" ", split, i + 1, split.Length - i);
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