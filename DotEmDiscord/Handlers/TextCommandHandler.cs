using Discord.Commands;
using Discord.WebSocket;
using DotemDiscord.Utils;
using System.Reflection;

namespace DotemDiscord.Handlers {
	public class TextCommandHandler {
		private string[] prefixes = [ ".", "!" ];

		private readonly DiscordSocketClient _client;
		private readonly CommandService _commandService;
		private readonly IServiceProvider _serviceProvider;
		private readonly JokeHandler _jokeHandler;

		public TextCommandHandler(DiscordSocketClient client, CommandService commandService, IServiceProvider serviceProvider, JokeHandler jokeHandler) {
			_client = client;
			_commandService = commandService;
			_serviceProvider = serviceProvider;
			_jokeHandler = jokeHandler;
		}

		public async Task InstallTextCommandsAsync() {
			_client.MessageReceived += HandleTextCommand;
			await _commandService.AddModulesAsync(Assembly.GetEntryAssembly(), _serviceProvider);
		}

		private async Task HandleTextCommand(SocketMessage messageParam) {
			var message = messageParam as SocketUserMessage;
			if (message == null) return;
			if (message.Author.IsBot) return;

			int argPos = 0;

			try {
				var wasMuikea = await _jokeHandler.TryMuikeaAsync(message);
				if (wasMuikea) return;

				if (!message.HasMentionPrefix(_client.CurrentUser, ref argPos)) {
					foreach (var prefix in prefixes) {
						if (message.HasStringPrefix(prefix, ref argPos)) break;
					}
					if (argPos == 0) return;
				}

				await _commandService.ExecuteAsync(
					new SocketCommandContext(_client, message),
					argPos,
					_serviceProvider
				);
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
			}
		}
	}
}