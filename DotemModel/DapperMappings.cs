﻿

using Dapper;
using System.Data;

namespace DotemModel {

	public abstract class SqliteTypeHandler<T> : SqlMapper.TypeHandler<T> {
		public override void SetValue(IDbDataParameter parameter, T? value)
			=> parameter.Value = value;
	}

	public class DateTimeOffsetHandler : SqliteTypeHandler<DateTimeOffset> {
		public override DateTimeOffset Parse(object value) {
			try {
				return DateTimeOffset.Parse((string)value);
			} catch (Exception e) {
				Console.WriteLine($"DateTimeOffset parsing error for value {value}: {e.Message}");
				return default;
			}
		}
	}

	public class GuidHandler : SqliteTypeHandler<Guid> {
		public override Guid Parse(object value) {
			try {
				return Guid.Parse((string)value);
			} catch (Exception e) {
				Console.WriteLine($"Guid parsing error for value {value}: {e.Message}");
				return default;
			}
		}
	}
}