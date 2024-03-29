﻿using DotemModel;
using Microsoft.Data.Sqlite;
using Dapper;
using DotemDiscord.Model;

namespace DotemDiscord.Context {
	public class DiscordContext {

		public string DataSource { get; }

		public DiscordContext(string dataSource = "dotemDiscord.db") {
			DataSource = dataSource;
		}

		public void Initialize() {
			SqlMapper.AddTypeHandler(new GuidHandler());
			EnsureDatabaseCreated();
		}

		private void EnsureDatabaseCreated() {
			using (var connection = GetOpenConnection()) {
				var command = connection.CreateCommand();
				command.CommandText =
				@"
					CREATE TABLE IF NOT EXISTS sessionConnection (
						channelId INT NOT NULL,
						messageId INT NOT NULL,
						userId INT,
						sessionId TEXT NOT NUll
					);
				";
				command.ExecuteNonQuery();
			}
		}

		#region Session Connections

		public async Task AddSessionConnectionAsync(ulong channelId, ulong messageId, ulong? userId, params Guid[] sessionIds) {
			if (sessionIds.Length == 0) return;
			using (var connection = GetOpenConnection()) {

				var command = connection.CreateCommand();
				command.CommandText = @$"
					INSERT INTO 
						sessionConnection
					VALUES 
						{string.Join(",", sessionIds.Select((_, i) => $"($channelId, $messageId, $userId, $g{i})"))};
				";

				command.Parameters.AddWithValue("$channelId", channelId);
				command.Parameters.AddWithValue("$messageId", messageId);
				command.Parameters.AddWithValue("$userId", userId == null ? DBNull.Value : userId);
				command.Parameters.AddRange(sessionIds.Select((guid, i) => new SqliteParameter("$g" + i, guid)));

				await command.ExecuteNonQueryAsync();
			}
		}

		public async Task<IEnumerable<SessionConnection>> GetAllSessionConnectionsAsync() {
			using (var connection = GetOpenConnection()) {
				var sql = @$"
					SELECT *
					FROM 
						sessionConnection;
				";

				return (await connection.QueryAsync<(ulong channelId, ulong messageId, ulong? userId, Guid sessionId)>(sql))
					.GroupBy(c => (c.channelId, c.messageId, c.userId))
					.Select(g => new SessionConnection() {
						ChannelId = g.Key.channelId,
						MessageId = g.Key.messageId,
						UserId = g.Key.userId,
						SessionIds = g.Select(g => g.sessionId)
					});
			}
		}

		public async Task<IEnumerable<(string channelId, string messageId)>> GetSearchMessagesForSessionAsync(string sessionId) {
			using (var connection = GetOpenConnection()) {
				var sql = @$"
					SELECT
						channelId,
						messageId
					FROM 
						sessionConnection
					WHERE
						sessionId = $sessionId;
				";

				return await connection.QueryAsync<(string channelId, string messageId)>(sql, new { sessionId });
			}
		}

		public async Task DeleteSessionConnectionAsync(SessionConnection sessionConnection)
			=> await DeleteSessionConnectionAsync(sessionConnection.MessageId, sessionConnection.SessionIds.ToArray());

		public async Task DeleteSessionConnectionAsync(ulong messageId, params Guid[] sessionIds) {
			using (var connection = GetOpenConnection()) {

				var command = connection.CreateCommand();
				command.CommandText = @$"
					DELETE FROM 
						sessionConnection
					WHERE
						messageId = $messageId
						AND sessionId IN ({string.Join(",", sessionIds.Select((_, i) => "$g" + i))});
				";

				command.Parameters.AddWithValue("$messageId", messageId);
				command.Parameters.AddRange(sessionIds.Select((id, i) => new SqliteParameter("$g" + i, id)));

				await command.ExecuteNonQueryAsync();
			}
		}
		public async Task DeleteSessionConnectionAsync(IEnumerable<Guid> sessionIds) {
			using (var connection = GetOpenConnection()) {

				var command = connection.CreateCommand();
				command.CommandText = @$"
					DELETE FROM 
						sessionConnection
					WHERE
						sessionId IN ({string.Join(",", sessionIds.Select((_, i) => "$g" + i))});
				";
				
				command.Parameters.AddRange(sessionIds.Select((id, i) => new SqliteParameter("$g" + i, id)));

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
