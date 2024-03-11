using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using DotemDiscord.Handlers;
using Discord.Interactions;
using DotemExtensions;
using DotemMatchmaker;
using DotemDiscord.Context;
using DotemMatchmaker.Context;

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
                GatewayIntents = GatewayIntents.MessageContent | GatewayIntents.AllUnprivileged & ~GatewayIntents.GuildScheduledEvents & ~GatewayIntents.GuildInvites,
                UseInteractionSnowflakeDate = false,
			};

            var interactionConfig = new InteractionServiceConfig() {
                AutoServiceScopes = true,
                ThrowOnError = true,
            };

			var steamApiKey = Environment.GetEnvironmentVariable("STEAM_APIKEY") ?? "";
			var lobbyPrefix = Environment.GetEnvironmentVariable("LOBBY_PREFIX") ?? "";

			var collection = new ServiceCollection()
                .AddSingleton<MatchmakingContext>()
                .AddSingleton<DiscordContext>()
                .AddSingleton<ExtensionContext>()
                .AddSingleton<Matchmaker>()
                .AddSingleton<MatchExpirer>()
                .AddSingleton(new SteamHandler(steamApiKey, lobbyPrefix))
                .AddSingleton(clientConfig)
                .AddSingleton<DiscordSocketClient>()
                .AddSingleton<CommandServiceConfig>()
                .AddSingleton<CommandService>()
				.AddSingleton(interactionConfig)
                .AddSingleton<InteractionService>()
				.AddSingleton<ButtonMessageHandler>()
				.AddSingleton<MatchmakingBoardHandler>()
				.AddSingleton<TextCommandHandler>()
                .AddSingleton<SlashCommandHandler>()
                .AddSingleton<JokeHandler>();

			return collection.BuildServiceProvider();
        }

        public async Task RunAsync(string[] args) {
			var client = _serviceProvider.GetRequiredService<DiscordSocketClient>();

			client.Ready += async () => {
				_serviceProvider.GetRequiredService<DiscordContext>().Initialize();
				_serviceProvider.GetRequiredService<MatchmakingContext>().Initialize();
				_serviceProvider.GetRequiredService<ExtensionContext>().Initialize();
				await _serviceProvider.GetRequiredService<ButtonMessageHandler>().CreatePreExistingSearchMessagesAsync();
                await _serviceProvider.GetRequiredService<MatchExpirer>().StartClearingExpiredJoins();
				await _serviceProvider.GetRequiredService<SlashCommandHandler>().InstallSlashCommandsAsync();
                await _serviceProvider.GetRequiredService<TextCommandHandler>().InstallTextCommandsAsync();
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