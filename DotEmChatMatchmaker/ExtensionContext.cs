using Dapper;
using Microsoft.Data.Sqlite;

namespace DotemChatMatchmaker
{
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
				";
				command.ExecuteNonQuery();
			}
		}

		#region Channel Default
		public async Task<(string[] gameIds, int? maxPlayerCount, int? duration, string? description)> GetChannelDefaultSearchParamaters(string channelId) {
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

		public async Task AddChannelDefaultParameters(string channelId, string gameIds, int? maxPlayerCount, int? duration, string? description) {
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

		public async Task DeleteChannelDefaultParameters(string channelId) {
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

		private SqliteConnection GetOpenConnection() {
			var connection = new SqliteConnection($"Data Source={DataSource}");
			connection.Open();
			return connection;
		}

	}
}