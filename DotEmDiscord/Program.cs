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
using Microsoft.Extensions.Configuration;

namespace DotemDiscord
{
    public class Program {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _config;

		public Program() {
			var builder = new ConfigurationBuilder();
           _config = builder.SetBasePath(Directory.GetCurrentDirectory())
                   .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                   .AddEnvironmentVariables()
                   .Build();

			_serviceProvider = CreateProvider();
        }

        private static void Main(string[] args)
            => new Program().RunAsync(args).GetAwaiter().GetResult();

        private IServiceProvider CreateProvider() {
            var clientConfig = new DiscordSocketConfig { 
                GatewayIntents = GatewayIntents.MessageContent | GatewayIntents.AllUnprivileged & ~GatewayIntents.GuildScheduledEvents & ~GatewayIntents.GuildInvites,
                UseInteractionSnowflakeDate = false,
			};

            var interactionConfig = new InteractionServiceConfig() {
                AutoServiceScopes = true,
                ThrowOnError = true,
            };

			var steamApiKey = _config["STEAM_APIKEY"] ?? "";
			var lobbyPrefix = _config["LOBBY_PREFIX"] ?? "";

			var collection = new ServiceCollection()
                .AddSingleton<MatchmakingContext>()
                .AddSingleton<ExtensionContext>()
                .AddSingleton<DiscordContext>()
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
				.AddSingleton<ButtonCleanser>()
				.AddSingleton<MatchmakingBoardHandler>()
				.AddSingleton<MatchListenHandler>()
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
				_serviceProvider.GetRequiredService<MatchListenHandler>().Initialize();
				_serviceProvider.GetRequiredService<ButtonCleanser>().Initialize();
				await _serviceProvider.GetRequiredService<ButtonMessageHandler>().CreatePreExistingSearchMessagesAsync();
                await _serviceProvider.GetRequiredService<MatchExpirer>().StartClearingExpiredJoins();
				await _serviceProvider.GetRequiredService<SlashCommandHandler>().InstallSlashCommandsAsync();
                await _serviceProvider.GetRequiredService<TextCommandHandler>().InstallTextCommandsAsync();
            };

			client.Log += async (msg) => {
                Console.WriteLine(msg);
                await Task.CompletedTask;
            };

			var token = _config["BOT_TOKEN"];
            await client.LoginAsync(TokenType.Bot, token);
            await client.StartAsync();

			await Task.Delay(Timeout.Infinite);
        }
    }
}