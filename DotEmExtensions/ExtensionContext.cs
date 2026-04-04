using Dapper;
using Microsoft.Data.Sqlite;

namespace DotemExtensions {
	public class ExtensionContext {

		public string DataSource { get; }

		public ExtensionContext(string dataSource = "dotemExtensions.db") {
			DataSource = dataSource;
		}

		public void Initialize() {
			EnsureDatabaseCreated();
		}

		private void EnsureDatabaseCreated() {
			using (var connection = GetOpenConnection()) {
				var command = connection.CreateCommand();
				command.CommandText =
				@"
					CREATE TABLE IF NOT EXISTS channelDefault (
						channelId TEXT PRIMARY KEY NOT NULL,
						serverId TEXT NOT NULL DEFAULT '',
						gameIds TEXT NOT NULL,
						maxPlayerCount INT,
						duration INT,
						description TEXT
					);

					CREATE TABLE IF NOT EXISTS userRematch (
						serverId TEXT NOT NULL,
						userId TEXT NOT NULL,
						gameIds TEXT NOT NULL,
						maxPlayerCount INT,
						duration INT,
						description TEXT,
                        UNIQUE(serverId, userId)
					);

					CREATE TABLE IF NOT EXISTS matchmakingBoard (
						channelId TEXT PRIMARY KEY NOT NULL,
						serverId TEXT NOT NULL
					);

					CREATE TABLE IF NOT EXISTS steamUser (
						userId TEXT PRIMARY KEY NOT NULL,
						steamId INT NOT NULL
					);
				";
				command.ExecuteNonQuery();
			}
		}

		#region Channel Default
		public async Task<(string[] gameIds, int? maxPlayerCount, int? duration, string? description)> GetChannelDefaultSearchParamatersAsync(string channelId) {
			using (var connection = GetOpenConnection()) {

				var sql = @"
					SELECT
						gameIds,
						maxPlayerCount,
						duration,
						description
					FROM
						channelDefault
					WHERE
						channelId = $channelId;
				";

				var result = await connection.QueryAsync(sql, new { channelId });

				if (!result.Any()) {
					return ([], null, null, null);
				}

				return result.Select(row => (
					((string?)row.gameIds)?.Split(" ") ?? [],
					(int?)row.maxPlayerCount,
					(int?)row.duration,
					(string?)row.description
				)).Single();
			}
		}

		public async Task<string[]> GetGameDefaultChannelsAsync(string gameId) {
			using (var connection = GetOpenConnection()) {

				var sql = @"
					SELECT
						channelId
					FROM
						channelDefault
					WHERE
						gameIds = $gameId
						OR gameIds LIKE $gameIdPrefix
						OR gameIds LIKE $gameIdSuffix
						OR gameIds LIKE $gameIdMiddle
				";

				var result = await connection.QueryAsync(sql, new {
					gameId,
					gameIdPrefix = $"{gameId} %",
					gameIdSuffix = $"% {gameId}",
					gameIdMiddle = $"% {gameId} %"
				});

				if (!result.Any()) {
					return [];
				}

				return result.Select(row => (string)row.channelId).ToArray();
			}
		}

		internal async Task SetChannelDefaultParametersAsync(string channelId, string serverId, string gameIds, int? maxPlayerCount, int? duration, string? description) {
			using (var connection = GetOpenConnection()) {
				var command = connection.CreateCommand();
				command.CommandText = @"
					INSERT INTO
						channelDefault
					VALUES ($channelId, $serverId, $gameIds, $maxPlayerCount, $duration, $description)
					ON CONFLICT (channelId)
					DO UPDATE SET
						serverId = excluded.serverId,
						gameIds = excluded.gameIds,
						maxPlayerCount = excluded.maxPlayerCount,
						duration = excluded.duration,
						description = excluded.description;
				";

				command.Parameters.AddWithValue("$channelId", channelId);
				command.Parameters.AddWithValue("$serverId", serverId);
				command.Parameters.AddWithValue("$gameIds", gameIds);
				command.Parameters.AddWithValue("$maxPlayerCount", maxPlayerCount == null ? DBNull.Value : maxPlayerCount);
				command.Parameters.AddWithValue("$duration", duration == null ? DBNull.Value : duration);
				command.Parameters.AddWithValue("$description", description == null ? DBNull.Value : description);

				await command.ExecuteNonQueryAsync();
			}
		}

		internal async Task MatchChannelDefaultIdsToAliasesAsync(IEnumerable<(string serverId, string gameId, string aliasGameId)> allAliases) {
			using var connection = GetOpenConnection();
			var byServer = allAliases
				.GroupBy(a => a.serverId)
				.ToDictionary(g => g.Key, g => g.ToDictionary(a => a.gameId.ToLowerInvariant(), a => a.aliasGameId));
			var rows = await connection.QueryAsync("SELECT channelId, serverId, gameIds FROM channelDefault");
			foreach (var row in rows) {
				var rowServerId = (string)row.serverId;
				if (!byServer.TryGetValue(rowServerId, out var aliases)) continue;
				var ids = ((string)row.gameIds).Split(" ");
				var updated = ids
					.Select(id => aliases.TryGetValue(id.ToLowerInvariant(), out var root) ? root : id)
					.Distinct()
					.ToArray();
				if (ids.ToHashSet().SetEquals(updated)) continue;
				await connection.ExecuteAsync(
					"UPDATE channelDefault SET gameIds = @gameIds WHERE channelId = @channelId",
					new { gameIds = string.Join(" ", updated), channelId = (string)row.channelId }
				);
			}
		}

		internal async Task<IEnumerable<(string channelId, string gameIds)>> UpdateChannelDefaultGameIdsAsync(string serverId, string newId, params string[] oldIds) {
			using var connection = GetOpenConnection();
			var rows = await connection.QueryAsync(
				"SELECT channelId, gameIds FROM channelDefault WHERE serverId = @serverId",
				new { serverId }
			);
			var affected = new List<(string channelId, string gameIds)>();
			foreach (var row in rows) {
				var ids = ((string)row.gameIds).Split(" ");
				if (!ids.Any(id => oldIds.Contains(id, StringComparer.OrdinalIgnoreCase))) continue;
				affected.Add(((string)row.channelId, (string)row.gameIds));
				var updated = ids
					.Select(id => oldIds.Contains(id, StringComparer.OrdinalIgnoreCase) ? newId : id)
					.Distinct();
				await connection.ExecuteAsync(
					"UPDATE channelDefault SET gameIds = @gameIds WHERE channelId = @channelId",
					new { gameIds = string.Join(" ", updated), channelId = (string)row.channelId }
				);
			}
			return affected;
		}

		internal async Task RestoreChannelDefaultGameIdsAsync(IEnumerable<(string channelId, string gameIds)> entries) {
			using var connection = GetOpenConnection();
			foreach (var (channelId, gameIds) in entries) {
				await connection.ExecuteAsync(
					"UPDATE channelDefault SET gameIds = @gameIds WHERE channelId = @channelId",
					new { gameIds, channelId }
				);
			}
		}

		public async Task DeleteChannelDefaultParametersAsync(string channelId) {
			using (var connection = GetOpenConnection()) {
				var command = connection.CreateCommand();
				command.CommandText = @"
					DELETE FROM
						channelDefault
					WHERE
						channelId = $channelId;
				";

				command.Parameters.AddWithValue("$channelId", channelId);

				await command.ExecuteNonQueryAsync();
			}
		}

		public async Task MigrateChannelDefaultServerIdsAsync(Func<string, Task<string?>> resolveServerId) {
			using var connection = GetOpenConnection();

			try {
				var alter = connection.CreateCommand();
				alter.CommandText = "ALTER TABLE channelDefault ADD COLUMN serverId TEXT NOT NULL DEFAULT '';";
				await alter.ExecuteNonQueryAsync();
				Console.WriteLine("Added serverId column.");
			} catch {
				Console.WriteLine("serverId column already exists.");
			}

			var channelIds = (await connection.QueryAsync<string>(
				"SELECT channelId FROM channelDefault WHERE serverId = ''"
			)).ToArray();

			Console.WriteLine($"Migrating {channelIds.Length} entries...");

			foreach (var channelId in channelIds) {
				var serverId = await resolveServerId(channelId);
				if (serverId == null) {
					Console.WriteLine($"  Could not resolve server for channel {channelId}, skipping.");
					continue;
				}
				await connection.ExecuteAsync(
					"UPDATE channelDefault SET serverId = @serverId WHERE channelId = @channelId",
					new { serverId, channelId }
				);
				Console.WriteLine($"  Channel {channelId} → server {serverId}");
			}

			Console.WriteLine("Migration complete.");
		}
		#endregion

		#region Rematch

		internal async Task<IEnumerable<(string serverId, string userId, string gameIds)>> UpdateRematchGameIdsAsync(string serverId, string newId, params string[] oldIds) {
			using var connection = GetOpenConnection();
			var rows = await connection.QueryAsync(
				"SELECT serverId, userId, gameIds FROM userRematch WHERE serverId = @serverId",
				new { serverId }
			);
			var affected = new List<(string serverId, string userId, string gameIds)>();
			foreach (var row in rows) {
				var ids = ((string)row.gameIds).Split(" ");
				if (!ids.Any(id => oldIds.Contains(id, StringComparer.OrdinalIgnoreCase))) continue;
				affected.Add(((string)row.serverId, (string)row.userId, (string)row.gameIds));
				var updated = ids
					.Select(id => oldIds.Contains(id, StringComparer.OrdinalIgnoreCase) ? newId : id)
					.Distinct();
				await connection.ExecuteAsync(
					"UPDATE userRematch SET gameIds = @gameIds WHERE serverId = @serverId AND userId = @userId",
					new { gameIds = string.Join(" ", updated), serverId = (string)row.serverId, userId = (string)row.userId }
				);
			}
			return affected;
		}

		internal async Task RestoreRematchGameIdsAsync(IEnumerable<(string serverId, string userId, string gameIds)> entries) {
			using var connection = GetOpenConnection();
			foreach (var (serverId, userId, gameIds) in entries) {
				await connection.ExecuteAsync(
					"UPDATE userRematch SET gameIds = @gameIds WHERE serverId = @serverId AND userId = @userId",
					new { gameIds, serverId, userId }
				);
			}
		}

		internal async Task MatchRematchIdsToAliasesAsync(IEnumerable<(string serverId, string gameId, string aliasGameId)> allAliases) {
			using var connection = GetOpenConnection();
			var byServer = allAliases
				.GroupBy(a => a.serverId)
				.ToDictionary(g => g.Key, g => g.ToDictionary(a => a.gameId.ToLowerInvariant(), a => a.aliasGameId));
			var rows = await connection.QueryAsync("SELECT serverId, userId, gameIds FROM userRematch");
			foreach (var row in rows) {
				var rowServerId = (string)row.serverId;
				if (!byServer.TryGetValue(rowServerId, out var aliases)) continue;
				var ids = ((string)row.gameIds).Split(" ");
				var updated = ids
					.Select(id => aliases.TryGetValue(id.ToLowerInvariant(), out var root) ? root : id)
					.Distinct()
					.ToArray();
				if (ids.ToHashSet().SetEquals(updated)) continue;
				await connection.ExecuteAsync(
					"UPDATE userRematch SET gameIds = @gameIds WHERE serverId = @serverId AND userId = @userId",
					new { gameIds = string.Join(" ", updated), serverId = rowServerId, userId = (string)row.userId }
				);
			}
		}

		public async Task<(string[] gameIds, int? maxPlayerCount, int? duration, string? description)?> GetUserRematchParameters(string serverId, string userId) {
			using (var connection = GetOpenConnection()) {

				var sql = @"
					SELECT
						gameIds,
						maxPlayerCount,
						duration,
						description
					FROM
						userRematch
					WHERE
						serverId = $serverId
						AND userId = $userId;
				";

				var result = await connection.QueryAsync(sql, new { serverId, userId });

				if (!result.Any()) { return null; }

				return result.Select(row => (
					((string?)row.gameIds)?.Split(" ") ?? [],
					(int?)row.maxPlayerCount,
					(int?)row.duration,
					(string?)row.description
				)).Single();
			}
		}

		public async Task SetUserRematchParameters(string serverId, string userId, string gameIds, int? maxPlayerCount, int? duration, string? description) {
			using (var connection = GetOpenConnection()) {
				var command = connection.CreateCommand();
				command.CommandText = @"
					INSERT INTO
						userRematch
					VALUES ($serverId, $userId, $gameIds, $maxPlayerCount, $duration, $description)
					ON CONFLICT (serverId, userId)
					DO UPDATE SET
						gameIds = excluded.gameIds,
						maxPlayerCount = excluded.maxPlayerCount,
						duration = excluded.duration,
						description = excluded.description;
				";

				command.Parameters.AddWithValue("$serverId", serverId);
				command.Parameters.AddWithValue("$userId", userId);
				command.Parameters.AddWithValue("$gameIds", gameIds);
				command.Parameters.AddWithValue("$maxPlayerCount", maxPlayerCount == null ? DBNull.Value : maxPlayerCount);
				command.Parameters.AddWithValue("$duration", duration == null ? DBNull.Value : duration);
				command.Parameters.AddWithValue("$description", description == null ? DBNull.Value : description);

				await command.ExecuteNonQueryAsync();
			}
		}
		#endregion

		#region Matchmaking Board
		public async Task AddMatchmakingBoardAsync(string serverId, string channelId) {
			using (var connection = GetOpenConnection()) {

				var command = connection.CreateCommand();
				command.CommandText = @$"
					INSERT OR IGNORE INTO
						matchmakingBoard
					VALUES
						($channelId, $serverId);
				";

				command.Parameters.AddWithValue("$channelId", channelId);
				command.Parameters.AddWithValue("$serverId", serverId);

				await command.ExecuteNonQueryAsync();
			}
		}

		public async Task<IEnumerable<(string serverId, string channelId)>> GetMatchmakingBoardsAsync() {
			using (var connection = GetOpenConnection()) {
				var sql = @$"
					SELECT
						serverId,
						channelId
					FROM
						matchmakingBoard;
				";

				return await connection.QueryAsync<(string serverId, string channelId)>(sql);
			}
		}

		public async Task<IEnumerable<string>> GetMatchmakingBoardsAsync(params string[] serverIds) {
			using (var connection = GetOpenConnection()) {
				var sql = @$"
					SELECT
						channelId
					FROM
						matchmakingBoard
					WHERE
						serverId IN $serverIds;
				";

				return await connection.QueryAsync<string>(sql, new { serverIds });
			}
		}

		public async Task DeleteMatchmakingBoardAsync(string channelId) {
			using (var connection = GetOpenConnection()) {

				var command = connection.CreateCommand();
				command.CommandText = @$"
					DELETE FROM
						matchmakingBoard
					WHERE
						channelId = $channelId
				";

				command.Parameters.AddWithValue("$channelId", channelId);
				await command.ExecuteNonQueryAsync();
			}
		}
		#endregion

		#region Steam User
		public async Task AddSteamUserAsync(string userId, ulong steamId) {
			using (var connection = GetOpenConnection()) {

				var command = connection.CreateCommand();
				command.CommandText = @$"
					INSERT INTO
						steamUser
					VALUES
						($userId, $steamId)
					ON CONFLICT (userId)
					DO UPDATE SET
						steamId = excluded.steamId;
				";

				command.Parameters.AddWithValue("$userId", userId);
				command.Parameters.AddWithValue("$steamId", steamId);

				await command.ExecuteNonQueryAsync();
			}
		}

		public async Task<ulong?> GetSteamUserAsync(string userId) {
			using (var connection = GetOpenConnection()) {
				var sql = @$"
					SELECT
						steamId
					FROM
						steamUser
					WHERE
						userId = $userId;
				";

				return (await connection.QueryAsync<ulong?>(sql, new { userId })).FirstOrDefault();
			}
		}

		public async Task DeleteSteamUserAsync(string userId) {
			using (var connection = GetOpenConnection()) {

				var command = connection.CreateCommand();
				command.CommandText = @$"
					DELETE FROM
						steamUser
					WHERE
						userId = $userId;
				";

				command.Parameters.AddWithValue("$userId", userId);
				await command.ExecuteNonQueryAsync();
			}
		}
		#endregion

		private SqliteConnection GetOpenConnection() {
			var connection = new SqliteConnection($"Data Source={DataSource}");
			connection.Open();
			return connection;
		}

	}
}
