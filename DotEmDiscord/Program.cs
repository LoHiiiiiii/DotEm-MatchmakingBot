using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using DotemDiscord.Handlers;
using Discord.Interactions;
using DotemChatMatchmaker;
using DotemMatchmaker;
using DotemDiscord.Context;

namespace DotemDiscord
{
    public class Program {
        private readonly IServiceProvider _serviceProvider;

        public Program() {
            _serviceProvider = CreateProvider();
        }

        private static void Main(string[] args)
            => new Program().RunAsync(args).GetAwaiter().GetResult();

        private static IServiceProvider CreateProvider() {
            var clientConfig = new DiscordSocketConfig { 
                GatewayIntents = GatewayIntents.MessageContent | GatewayIntents.AllUnprivileged,
                UseInteractionSnowflakeDate = false,
			};

            var interactionConfig = new InteractionServiceConfig() {
                AutoServiceScopes = true,
                ThrowOnError = true,
            };

			var collection = new ServiceCollection()
				.AddSingleton<Matchmaker>()
				.AddSingleton<ExtensionContext>()
                .AddSingleton(clientConfig)
                .AddSingleton<DiscordSocketClient>()
                .AddSingleton<CommandServiceConfig>()
                .AddSingleton<CommandService>()
				.AddSingleton(interactionConfig)
                .AddSingleton<InteractionService>()
				.AddSingleton<DiscordContext>()
				.AddSingleton<ButtonMessageHandler>()
				.AddSingleton<TextCommandHandler>()
                .AddSingleton<SlashCommandHandler>()
                .AddSingleton<JokeHandler>();

			return collection.BuildServiceProvider();
        }

        public async Task RunAsync(string[] args) {
			var client = _serviceProvider.GetRequiredService<DiscordSocketClient>();

            var textCommandHandler = _serviceProvider.GetRequiredService<TextCommandHandler>();
			await textCommandHandler.InstallTextCommandsAsync();

			var idVar = Environment.GetEnvironmentVariable("TEST_GUILDID");
            if (idVar == null || !ulong.TryParse(idVar, out var guildId)) {
                Console.WriteLine("Missing guild id!");
                return;
            }

			client.Ready += async () => {
				_serviceProvider.GetRequiredService<DiscordContext>().Initialize();
				var matchmaker = _serviceProvider.GetRequiredService<Matchmaker>();
				matchmaker.Initialize();
				await _serviceProvider.GetRequiredService<ButtonMessageHandler>().CreatePreExistingSearchMessagesAsync(client, matchmaker);
				matchmaker.StartExpirationLoop();
				var slashCommandHandler = _serviceProvider.GetRequiredService<SlashCommandHandler>();
                await slashCommandHandler.InstallSlashCommandsAsync(guildId);
            };

			client.Log += async (msg) => {
                Console.WriteLine(msg);
                await Task.CompletedTask;
            };

			var token = Environment.GetEnvironmentVariable("BOT_TOKEN");
            await client.LoginAsync(TokenType.Bot, token);
            await client.StartAsync();

			await Task.Delay(Timeout.Infinite);
        }
    }
}