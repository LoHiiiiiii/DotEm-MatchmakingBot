using DotemMatchmaker;
using DotemMatchmaker.Context;

namespace DotemExtensions {
	public class ExtendedMatchmaker : Matchmaker {

		private readonly ExtensionContext _extensionContext;

		public ExtendedMatchmaker(
			MatchmakingContext context,
			ExtensionContext extensionContext,
			int expireClearIntervalMinutes = 1,
			int defaultMaxPlayerCount = 2,
			int defaultDurationMinutes = 30
		) : base(context, expireClearIntervalMinutes, defaultMaxPlayerCount, defaultDurationMinutes) {
			_extensionContext = extensionContext;
		}

		public override async Task InitializeAsync() {
			var aliasesWithServer = await GetAllGameAliasesWithServerAsync();
			await _extensionContext.MatchChannelDefaultIdsToAliasesAsync(aliasesWithServer);
			await _extensionContext.MatchRematchIdsToAliasesAsync(aliasesWithServer);
		}

		public async Task SetChannelDefaultAsync(string serverId, string channelId, string[] gameIds, int? maxPlayerCount, int? duration, string? description) {
			var aliases = await GetGameAliasesAsync(serverId, gameIds);
			var resolvedIds = aliases.Values.Distinct().ToArray();
			await _extensionContext.SetChannelDefaultParametersAsync(
				channelId,
				serverId: serverId,
				gameIds: string.Join(" ", resolvedIds),
				maxPlayerCount: maxPlayerCount,
				duration: duration,
				description: description
			);
		}

		public override async Task AddGameAliasAsync(string serverId, string aliasId, params string[] gameIds) {
			var affectedChannels = await _extensionContext.UpdateChannelDefaultGameIdsAsync(serverId, aliasId, gameIds);
			var affectedRematches = await _extensionContext.UpdateRematchGameIdsAsync(serverId, aliasId, gameIds);
			try {
				await base.AddGameAliasAsync(serverId, aliasId, gameIds);
			} catch {
				await _extensionContext.RestoreChannelDefaultGameIdsAsync(affectedChannels);
				await _extensionContext.RestoreRematchGameIdsAsync(affectedRematches);
				throw;
			}
		}
	}
}
