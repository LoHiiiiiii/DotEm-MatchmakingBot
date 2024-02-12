using DotemModel;

namespace DotemMatchmaker {
	public class MatchExpirer {

		private readonly Matchmaker _matchmaker;

		private List<DateTimeOffset> Expirations { get; set; } = new();
		private DateTimeOffset? FirstExpiration { get; set; } = new();
		private CancellationTokenSource? ExpirationSource { get; set; }


		public MatchExpirer(Matchmaker matchmaker) {
			_matchmaker = matchmaker;
			matchmaker.SessionChanged += HandleUpdatedExpire;
		}

		public async Task StartClearingExpireds() {
			var sessions = (await _matchmaker.GetAllSessionsAsync())
				.Where(s => s.UserExpires.Any());

			if (!sessions.Any()) { return; }

			Expirations = sessions
				.SelectMany(s => s.UserExpires.Values)
				.ToList();

			await TryClearExpiredsAsync();
		}

		private async Task TryClearExpiredsAsync() {
			Expirations.Sort();
			if (Expirations.First() > DateTimeOffset.Now) {
				HandleExpirationTask();
				return;
			}
			await _matchmaker.ClearpExpiredJoinsAsync();
			Expirations = Expirations
				.Where(d => d > DateTimeOffset.Now)
				.Distinct()
				.ToList();
			if (!Expirations.Any()) {
				FirstExpiration = null;
				return;
			}
			HandleExpirationTask();
		}


		private async void HandleExpirationTask() {
			if (!Expirations.Any()) return;
			Expirations.Sort();
			if (FirstExpiration != null 
				&& Expirations.First() >= FirstExpiration
				&& FirstExpiration > DateTimeOffset.Now) {
				return;
			}
			ExpirationSource?.Cancel();
			ExpirationSource = new CancellationTokenSource();
			var token = ExpirationSource.Token;
			FirstExpiration = Expirations.First();
			await Task.Delay(Expirations.First() - DateTimeOffset.Now, token);
			if (token.IsCancellationRequested) {
				return;
			}
			ExpirationSource = null;
			FirstExpiration = null;
			await TryClearExpiredsAsync();
		}

		private async void HandleUpdatedExpire(IEnumerable<SessionDetails> added, IEnumerable<SessionDetails> updated, IEnumerable<Guid> _) {
			var changed = added.Concat(updated)
				.Where(s => s.UserExpires.Any());

			if (!changed.Any()) { return; }

			Expirations = Expirations
				.Concat(changed.SelectMany(s => s.UserExpires.Values))
				.Distinct()
				.ToList();

			await TryClearExpiredsAsync();
		}
	}
}