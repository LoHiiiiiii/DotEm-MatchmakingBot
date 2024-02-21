using DotemModel;
using Microsoft.Data.Sqlite;
using Dapper;

namespace DotemMatchmaker.Context {
	public class MatchmakingContext {

		public string DataSource { get; }

		public MatchmakingContext(string dataSource = "dotemMatchmaking.db") {
			DataSource = dataSource;
		}

		public void Initialize() {
			SqlMapper.AddTypeHandler(new GuidHandler());
			SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
			EnsureDatabaseCreated();
		}

		private void EnsureDatabaseCreated() {
			using (var connection = GetOpenConnection()) {
				var command = connection.CreateCommand();
				command.CommandText = @"
					CREATE TABLE IF NOT EXISTS session (
						sessionId TEXT NOT NULL PRIMARY KEY,
						serverId TEXT NOT NULL,
						gameId TEXT NOT NULL,
						maxPlayerCount INT NOT NULL,
						description TEXT
					);

					CREATE TABLE IF NOT EXISTS userJoin (
						userId TEXT NOT NULL,
						sessionId TEXT NOT NULL,
						expireTime TEXT NOT NULL,
						FOREIGN KEY(sessionId) REFERENCES session(sessionId),
                        UNIQUE(userId, sessionId)
					);

					CREATE TABLE IF NOT EXISTS gameName (
						gameId TEXT UNIQUE NOT NULL,
						serverId TEXT NOT NULL,
						name TEXT,
                        UNIQUE(gameId, serverId)
					);

					CREATE TABLE IF NOT EXISTS gameAlias (
						gameId TEXT NOT NULL,
						serverId TEXT NOT NULL,
						aliasGameId TEXT NOT NULL,
                        UNIQUE(gameId, serverId)
					);
				";
				command.ExecuteNonQuery();
			}
		}

		#region Sessions and Joins
		private string sessionSelectBase = @$"
			SELECT 
				session.sessionId AS sessionId, 
				session.gameId AS gameId, 
				name,
				session.serverId AS serverId,
				maxPlayerCount,
				description,
				userJoin.rowid AS rowid,
				userId,
				expireTime
			FROM
				session
			LEFT JOIN gameName ON
				gameName.gameId = session.gameId
				AND gameName.serverId = session.serverId
			LEFT JOIN userJoin ON
				userJoin.sessionId = session.sessionId";

		public async Task<IEnumerable<SessionDetails>> GetUserJoinableSessionsAsync(string serverId, string userId, IEnumerable<string>? gameIds = null) {
			using (var connection = GetOpenConnection()) {
				var aliasIds = (await GetGameAliasesAsync(connection, serverId, gameIds?.ToArray()))?.Values.Distinct();

				var sql = @$"{sessionSelectBase}
					WHERE 
						session.serverId = $serverId
						AND userId != $userId
						{(aliasIds == null ? "" : "AND session.GameId IN $gameIds")};
				";

				object? parameters = aliasIds != null
					? new { serverId, userId, gameIds = aliasIds }
					: new { serverId, userId };

				return await SessionQueryAsync(connection, sql, parameters);
			}
		}

		public async Task<IEnumerable<SessionDetails>> GetUserExistingSessionsAsync(string serverId, string userId) {
			using (var connection = GetOpenConnection()) {
				var sql = @$"
					SELECT 
						session.sessionId
					FROM
						session
					LEFT JOIN userJoin ON
						userJoin.sessionId = session.sessionId
					WHERE 
						serverId = ${nameof(serverId)}
						AND userId = ${nameof(userId)};
				";

				var ids = await connection.QueryAsync<Guid?>(sql, new { serverId, userId });
				var array = ids?
					.Where(id => id != null)
					.Select(id => (Guid)id!)
					.ToArray() ?? [];

				return await GetSessionsAsync(connection, array);
			}
		}

		public async Task<SessionDetails> CreateSessionAsync(string serverId, string userId, string gameId, int maxPlayerCount, string? description, DateTimeOffset expireTime) {
			using (var connection = GetOpenConnection()) {
				var aliasId = (await GetGameAliasesAsync(connection, serverId, gameId))[gameId];
				var sessionId = Guid.NewGuid();

				var command = connection.CreateCommand();
				command.CommandText = @"
					INSERT INTO session
					VALUES ($sessionId, $serverId, $gameId, $maxPlayerCount, $description);
				";

				command.Parameters.AddWithValue("$sessionId", sessionId);
				command.Parameters.AddWithValue("$serverId", serverId);
				command.Parameters.AddWithValue("$gameId", aliasId);
				command.Parameters.AddWithValue("$maxPlayerCount", maxPlayerCount);
				command.Parameters.AddWithValue("$description", description == null ? DBNull.Value : description);

				await command.ExecuteNonQueryAsync();

				return (await JoinSessionAsync(connection, sessionId, userId, expireTime))!;
			}
		}

		public async Task<SessionDetails?> JoinSessionAsync(Guid sessionId, string userId, DateTimeOffset expireTime) {
			using (var connection = GetOpenConnection()) {
				return await JoinSessionAsync(connection, sessionId, userId, expireTime);
			}
		}

		private async Task<SessionDetails?> JoinSessionAsync(SqliteConnection connection, Guid sessionId, string userId, DateTimeOffset expireTime) {
			var command = connection.CreateCommand();

			command.CommandText = @"
				INSERT INTO userJoin
					VALUES ($userId, $sessionId, $expireTime)
				ON CONFLICT (userId, sessionId) 
				DO UPDATE SET 
					expireTime=excluded.expireTime;
			";

			command.Parameters.AddWithValue("$sessionId", sessionId);
			command.Parameters.AddWithValue("$userId", userId);
			command.Parameters.AddWithValue("$expireTime", expireTime);

			await command.ExecuteNonQueryAsync();
			var sessions = await GetSessionsAsync(connection, sessionId);
			return sessions.Single();
		}


		public async Task<IEnumerable<SessionDetails>> GetSessionsAsync(params Guid[] sessionIds) {
			using (var connection = GetOpenConnection()) {
				return await GetSessionsAsync(connection, sessionIds);
			}
		}

		private async Task<IEnumerable<SessionDetails>> GetSessionsAsync(SqliteConnection connection, params Guid[] sessionIds) {
			if (sessionIds == null || !sessionIds.Any()) return Enumerable.Empty<SessionDetails>();
			var sql = @$"{sessionSelectBase}
				WHERE 
					session.sessionId IN ${nameof(sessionIds)};
				";

			return await SessionQueryAsync(connection, sql, new { sessionIds });
		}

		public async Task<IEnumerable<SessionDetails>> GetAllSessionsAsync() {
			using (var connection = GetOpenConnection()) {
				return await SessionQueryAsync(connection, $"{sessionSelectBase};");
			}
		}

		public async Task<(IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped)> LeaveSessionsAsync(string userId, params Guid[] sessionIds) {
			using (var connection = GetOpenConnection()) {
				return await RemoveJoinsFromSessionsAsync(connection, [userId], sessionIds);
			}
		}

		public async Task<(IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped)> LeaveAllSessionsAsync(params string[] userId) {
			using (var connection = GetOpenConnection()) {
				var sql = $@"
					SELECT
						sessionId
					FROM
						userJoin
					WHERE
						userId IN ${nameof(userId)};
				";

				var sessionIds = await connection.QueryAsync<Guid>(sql, new { userId });

				return await RemoveJoinsFromSessionsAsync(connection, userId, sessionIds?.ToArray() ?? []);
			}
		}

		public async Task<IEnumerable<Guid>> StopSessionsAsync(params Guid[] sessionIds) {
			using (var connection = GetOpenConnection()) {
				(var _, var stopped) = await RemoveJoinsFromSessionsAsync(connection, userIds: null, sessionIds);
				return stopped;
			}
		}

		public async Task<(IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped)> LeaveGamesAsync(string userId, string serverId, params string[] gameIds) {
			using (var connection = GetOpenConnection()) {
				var aliasIds = (await GetGameAliasesAsync(connection, serverId, gameIds)).Values.Distinct();
				var sql = @$"
					SELECT 
						session.sessionId
					FROM
						session
					LEFT JOIN userJoin ON
						userJoin.sessionId = session.sessionId
					WHERE 
						gameId IN ${nameof(aliasIds)}
						AND userId = ${nameof(userId)};
                ";

				var sessionIds = await connection.QueryAsync<Guid>(sql, new { aliasIds, userId });

				return await RemoveJoinsFromSessionsAsync(connection, [userId], sessionIds?.ToArray() ?? []);
			}
		}

		public async Task<(IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped)> ClearExpiredJoinsAsync() {
			using (var connection = GetOpenConnection()) {
				var now = DateTime.Now;
				var sql = $@"
					SELECT
						sessionId
					FROM
						userJoin
					WHERE
						expireTime <= ${nameof(now)};
				";

				var expiredIds = (await connection.QueryAsync<Guid>(sql, new { now }))?.Distinct().ToArray() ?? [];

				var command = connection.CreateCommand();
				command.CommandText = @"
					DELETE FROM
						userJoin
					WHERE
						expireTime <= $now;
				";

				command.Parameters.AddWithValue(nameof(now), now);
				await command.ExecuteNonQueryAsync();

				return await GetSessionChangesAsync(connection, expiredIds);
			}
		}

		private async Task<(IEnumerable<SessionDetails> updatedSessions, IEnumerable<Guid> stoppedSessions)> RemoveJoinsFromSessionsAsync(
			SqliteConnection connection,
			string[]? userIds,
			Guid[] sessionIds
		) {
			var command = connection.CreateCommand();
			command.CommandText = $@"
				DELETE FROM userJoin
				WHERE
				    {(userIds != null ? $"userId IN ({string.Join(",", sessionIds.Select((_, i) => "$s" + i))}) AND" : "")}
					sessionId IN ({string.Join(",", sessionIds.Select((_, i) => "$g" + i))});
			";
			if (userIds != null) command.Parameters.AddRange(userIds.Select((user, i) => new SqliteParameter("$s" + i, user)));
			command.Parameters.AddRange(sessionIds.Select((guid, i) => new SqliteParameter("$g" + i, guid)));
			await command.ExecuteNonQueryAsync();

			return await GetSessionChangesAsync(connection, sessionIds);
		}

		private async Task<(IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped)> GetSessionChangesAsync(
			SqliteConnection connection,
			Guid[] sessionIds
		) {
			var sql = $@"
				SELECT
					session.sessionId
				FROM
					session
				LEFT JOIN userJoin ON
					userJoin.sessionId = session.sessionId
				WHERE
					userId IS NULL;
			";

			var stoppedSessionIds = await connection.QueryAsync<Guid>(sql);

			if (stoppedSessionIds.Any()) {
				var command = connection.CreateCommand();
				command.CommandText = $@"
					DELETE FROM session
					WHERE
						sessionId IN ({string.Join(", ", stoppedSessionIds.Select((_, i) => "$g" + i))});
				";
				command.Parameters.AddRange(stoppedSessionIds.Select((guid, i) => new SqliteParameter("$g" + i, guid)));
				await command.ExecuteNonQueryAsync();
			}

			var updatedSessions = await GetSessionsAsync(connection, sessionIds);

			return (updatedSessions, stoppedSessionIds);
		}

		private async Task<IEnumerable<SessionDetails>> SessionQueryAsync(SqliteConnection connection, string sql, object? parameters = null) {
			// rowid sqlite implicit primarykey
			var result = await connection.QueryAsync<SessionDetails, (int rowid, string? userId, DateTimeOffset expireTime), SessionDetails>(sql, (session, userExpire) => {
				if (userExpire.userId != null) {
					session.UserExpires.Add(userExpire.userId, userExpire.expireTime);
				}
				return session;
			}, param: parameters,
				splitOn: "rowid");

			return result
				.GroupBy(s => s.SessionId)
				.Select(g => {
					var session = g.First();
					session.UserExpires = g
						.Select(s => s.UserExpires.Single())
						.ToDictionary();
					return session;
				});

		}
		#endregion

		#region Alias
		public async Task<Dictionary<string, string>> GetGameAliasesAsync(string serverId, params string[]? gameIds) {
			using (var connection = GetOpenConnection()) {
				return await GetGameAliasesAsync(connection, serverId, gameIds);
			}
		}

		private async Task<Dictionary<string, string>> GetGameAliasesAsync(SqliteConnection connection, string serverId, params string[]? gameIds) {
			if (gameIds == null || !gameIds.Any()) return new();
			var ids = gameIds.Select(s => s.ToLower()).Distinct();
			var sql = $@"
				SELECT 
					gameId, aliasGameId
				FROM 
					gameAlias
				WHERE
					serverId = $serverId
					AND gameId IN ${nameof(ids)};
			";

			var result = await connection.QueryAsync(sql, new { ids, serverId });

			var aliasIds = result
				?.Where(row => row?.gameId != null && row?.aliasGameId != null)
				?.ToDictionary(
					row => (string)row.gameId,
					row => (string)row.aliasGameId
				)
				?? new();

			foreach (var id in ids) {
				aliasIds.TryAdd(id, id.ToLower());
			}

			return aliasIds;
		}

		public async Task<Dictionary<string, string>> GetAllGameAliasesAsync(string serverId) {
			using (var connection = GetOpenConnection()) {
				var sql = $@"
					SELECT 
						gameId, aliasGameId
					FROM 
						gameAlias
					WHERE
						serverId = $serverId;
				";

				var result = await connection.QueryAsync(sql, new { serverId });

				return result
					?.Where(row => row?.gameId != null && row?.aliasGameId != null)
					?.ToDictionary(
						row => (string)row.gameId,
						row => (string)row.aliasGameId
					)
					?? new();
			}
		}

		public async Task DeleteGameAliasesAsync(string serverId, params string[] gameIds) {
			if (!gameIds.Any()) { return; }
			using (var connection = GetOpenConnection()) {
				var command = connection.CreateCommand();
				command.CommandText = $@"
					DELETE FROM 
						gameAlias
					WHERE
						serverId = $serverId
						AND gameId IN ({string.Join(",", gameIds.Select((_, i) => "$s" + i))});
				";

				command.Parameters.AddWithValue("$serverId", serverId);
				command.Parameters.AddRange(gameIds.Select((id, i) => new SqliteParameter("$s" + i, id)));

				await command.ExecuteNonQueryAsync();
			}
		}

		public async Task<IEnumerable<SessionDetails>> AddGameAliasAsync(string serverId, string aliasGameId, params string[] gameIds) {
			if (!gameIds.Any()) { return Enumerable.Empty<SessionDetails>(); }
			using (var connection = GetOpenConnection()) {
				var gameIdString = string.Join(",", gameIds.Select((_, i) => "$s" + i));
				var command = connection.CreateCommand();
				command.CommandText = $@"
					INSERT INTO 
						gameAlias
					VALUES
						{string.Join(",", gameIds.Select((_, i) => $"($s{i},$serverId,$aliasGameId)"))}
					ON CONFLICT (gameId, serverId) 
					DO UPDATE SET 
						aliasGameId=excluded.aliasGameId;

					UPDATE
						gameAlias
					SET
						aliasGameId = $aliasGameId
					WHERE 
						serverId = $serverId
						AND aliasGameId IN ({gameIdString});

					DELETE FROM
						gameAlias
					WHERE
						gameId = $aliasGameId
						AND serverId = $serverId;

					UPDATE OR IGNORE
						gameName
					SET
						gameId = $aliasGameId
					WHERE 
						gameId IN ({string.Join(",", gameIds.Select((_, i) => "$s" + i))})
						AND serverId = $serverId;
				";

				command.Parameters.AddRange(gameIds.Select((id, i) => new SqliteParameter("$s" + i, id)));
				command.Parameters.AddWithValue("$serverId", serverId);
				command.Parameters.AddWithValue("$aliasGameId", aliasGameId);

				await command.ExecuteNonQueryAsync();

				var sql = @"
					SELECT
						sessionId
					FROM
						session
					WHERE
						gameId IN $gameIds
						AND serverId = $serverId;
				";

				var idsToUpdate = await connection.QueryAsync<Guid>(sql, new { gameIds, serverId });

				if (!idsToUpdate.Any()) { return Enumerable.Empty<SessionDetails>(); }

				command.Parameters.Clear();

				command.CommandText = $@"
					UPDATE session
					SET 
						gameId = $aliasId
					WHERE
						gameId IN ({string.Join(",", gameIds.Select((_, i) => "$s" + i))})
						AND serverId = $serverId;
				";

				command.Parameters.AddRange(gameIds.Select((id, i) => new SqliteParameter("$s" + i, id)));
				command.Parameters.AddWithValue("$serverId", serverId);

				await command.ExecuteNonQueryAsync();

				return await GetSessionsAsync(idsToUpdate.ToArray());
			}
		}
		#endregion

		#region FullName
		public async Task<Dictionary<string, string>> GetGameNamesAsync(string serverId, params string[]? gameIds) {
			using (var connection = GetOpenConnection()) {
				if (gameIds == null || !gameIds.Any()) return new();
				var ids = gameIds.Select(s => s.ToLower()).Distinct();
				var sql = $@"
					SELECT 
						gameId, name
					FROM 
						gameName
					WHERE
						serverId = $serverId
						AND gameId IN ${nameof(ids)};
				";

				var result = await connection.QueryAsync(sql, new { ids, serverId });

				return result
					?.Where(row => row?.gameId != null && row?.aliasGameId != null)
					?.ToDictionary(
						row => (string)row.gameId,
						row => (string)row.aliasGameId
					)
					?? new();
			}
		}

		public async Task<Dictionary<string, string>> GetAllGameNamesAsync(string serverId) {
			using (var connection = GetOpenConnection()) {
				var sql = $@"
					SELECT 
						gameId, name
					FROM 
						gameName
					WHERE
						serverId = $serverId;
				";

				var result = await connection.QueryAsync(sql, new { serverId });

				return result
					?.Where(row => row?.gameId != null && row?.name != null)
					?.ToDictionary(
						row => (string)row.gameId,
						row => (string)row.name
					)
					?? new();
			}
		}

		public async Task<IEnumerable<SessionDetails>> AddGameNameAsync(string serverId, string name, string gameId) {
			using (var connection = GetOpenConnection()) {
				var command = connection.CreateCommand();
				command.CommandText = @"
					INSERT INTO 
						gameName
					VALUES
						($gameId,$serverId,$name)
					ON CONFLICT (gameId, serverId) 
					DO UPDATE SET 
						aliasGameId=excluded.aliasGameId;
				";

				command.Parameters.AddWithValue("$gameId", gameId);
				command.Parameters.AddWithValue("$serverId", serverId);
				command.Parameters.AddWithValue("$name", name);

				await command.ExecuteNonQueryAsync();

				var sql = $@"{sessionSelectBase}
					WHERE
						gameId = $gameId
						AND serverId = $serverId;
				";

				return await SessionQueryAsync(connection, sql, new { gameId, serverId });
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
