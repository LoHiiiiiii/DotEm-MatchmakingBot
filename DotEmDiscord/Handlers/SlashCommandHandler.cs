using Discord.Interactions;
using Discord.WebSocket;
using System.Reflection;

namespace DotemDiscord.Handlers {
	public class SlashCommandHandler {
		private readonly DiscordSocketClient _client;
		private readonly InteractionService _interactionService;
		private readonly IServiceProvider _serviceProvider;

		public SlashCommandHandler(DiscordSocketClient client, InteractionService interactionService, IServiceProvider serviceProvider) {
			_client = client;
			_interactionService = interactionService;
			_serviceProvider = serviceProvider;
		}

		public async Task InstallSlashCommandsAsync() {
			_client.SlashCommandExecuted += HandleSlashCommand;
			await _interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), _serviceProvider);
		}

		private async Task HandleSlashCommand(SocketSlashCommand command) {
			await _interactionService.ExecuteCommandAsync(new SocketInteractionContext<SocketSlashCommand>(_client, command), _serviceProvider);
		}
	}
}
