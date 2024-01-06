using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using DotemDiscord.Handlers;
using Discord.Interactions;
using DotemChatMatchmaker;

namespace DotemDiscord
{
    public class Program {
        private readonly IServiceProvider _serviceProvider;

        public Program() {
            _serviceProvider = CreateProvider();
        }

        static void Main(string[] args)
            => new Program().RunAsync(args).GetAwaiter().GetResult();

        static IServiceProvider CreateProvider() {
            var clientConfig = new DiscordSocketConfig { 
                GatewayIntents = GatewayIntents.MessageContent | GatewayIntents.AllUnprivileged,
                UseInteractionSnowflakeDate = false,
			};

            var interactionConfig = new InteractionServiceConfig() {
                AutoServiceScopes = true,
                ThrowOnError = true,
            };

			var timeOutEnv = Environment.GetEnvironmentVariable("MESSAGE_TIMEOUT_MINUTES");
			if (timeOutEnv == null || !int.TryParse(timeOutEnv, out var timeoutMinutes)) {
                timeoutMinutes = 14; //Default
			}

			var collection = new ServiceCollection()
				.AddSingleton<ChatGameNameHandler>()
				.AddSingleton<ChatMatchmaker>()
                .AddSingleton(clientConfig)
                .AddSingleton<DiscordSocketClient>()
                .AddSingleton(new CommandServiceConfig())
                .AddSingleton<CommandService>()
				.AddSingleton(interactionConfig)
                .AddSingleton<InteractionService>()
                .AddSingleton(new ButtonMessageHandler(timeoutMinutes))
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