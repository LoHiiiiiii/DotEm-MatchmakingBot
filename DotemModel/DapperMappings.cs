using Dapper;
using System.Data;

namespace DotemModel {

	public abstract class SqliteTypeHandler<T> : SqlMapper.TypeHandler<T> {
		public override void SetValue(IDbDataParameter parameter, T? value)
			=> parameter.Value = value;
	}

	public class DateTimeOffsetHandler : SqliteTypeHandler<DateTimeOffset> {
		public override DateTimeOffset Parse(object value) => DateTimeOffset.Parse((string)value);
	}

	public class GuidHandler : SqliteTypeHandler<Guid> {
		public override Guid Parse(object value) => Guid.Parse((string)value);
	}
}
