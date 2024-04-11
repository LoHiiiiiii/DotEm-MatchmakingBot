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
						serverId TEXY NOT NULL
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

		public async Task SetChannelDefaultParametersAsync(string channelId, string gameIds, int? maxPlayerCount, int? duration, string? description) {
			using (var connection = GetOpenConnection()) {
				var command = connection.CreateCommand();
				command.CommandText = @"
					INSERT INTO
						channelDefault
					VALUES ($channelId, $gameIds, $maxPlayerCount, $duration, $description)
					ON CONFLICT (channelId)
					DO UPDATE SET 
						gameIds = excluded.gameIds,
						maxPlayerCount = excluded.maxPlayerCount,
						duration = excluded.duration,
						description = excluded.description;
				";

				command.Parameters.AddWithValue("$channelId", channelId);
				command.Parameters.AddWithValue("$gameIds", gameIds);
				command.Parameters.AddWithValue("$maxPlayerCount", maxPlayerCount == null ? DBNull.Value : maxPlayerCount);
				command.Parameters.AddWithValue("$duration", duration == null ? DBNull.Value : duration);
				command.Parameters.AddWithValue("$description", description == null ? DBNull.Value : description);

				await command.ExecuteNonQueryAsync();
			}
		}

		public async Task DeleteChannelDefaultParametersAsync(string channelId) {
			using (var connection = GetOpenConnection()) {
				var command = connection.CreateCommand();
				command.CommandText = @"
					DELETE FROM
						channelDefault
					WHERE
						channelID = $channelId;
				";

				command.Parameters.AddWithValue("$channelId", channelId);

				await command.ExecuteNonQueryAsync();
			}
		}
		#endregion

		#region Rematch

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
						matchmakingBoard
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