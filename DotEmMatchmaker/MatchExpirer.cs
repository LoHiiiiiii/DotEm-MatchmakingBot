using DotemModel;

namespace DotemMatchmaker {
	public class MatchExpirer {

		private readonly Matchmaker _matchmaker;

		private List<DateTimeOffset> Expirations { get; set; } = new();
		private DateTimeOffset? FirstExpiration { get; set; }
		private CancellationTokenSource? ExpirationSource { get; set; }

		private SemaphoreSlim expirationSemaphore = new SemaphoreSlim(1, 1);

		public Action<Exception>? ExceptionHandler { get; set; }

		public MatchExpirer(Matchmaker matchmaker) {
			_matchmaker = matchmaker;
			matchmaker.SessionChanged += HandleUpdatedExpire;
			matchmaker.SessionAdded += HandleAddedExpire;
		}

		public async Task StartClearingExpiredJoins() {
			try {
				var sessions = (await _matchmaker.GetAllSessionsAsync())
					.Where(s => s.UserExpires.Any());

				if (!sessions.Any()) { return; }

				await expirationSemaphore.WaitAsync();
				try {
					Expirations = sessions
						.SelectMany(s => s.UserExpires.Values)
						.Distinct()
						.ToList();
				} finally { expirationSemaphore.Release(); }

				await TryClearExpiredsAsync();
			} catch (Exception e) {
				ExceptionHandler?.Invoke(e);
			}
		}

		private async Task TryClearExpiredsAsync() {
			bool handleTask = false;

			await expirationSemaphore.WaitAsync();
			try {
				if (!Expirations.Any()) { return; }
				Expirations.Sort();
				if (Expirations.First() > DateTimeOffset.Now) {
					handleTask = true;
					return;
				}
				await _matchmaker.ClearExpiredJoinsAsync();
				Expirations = Expirations
					.Where(d => d > DateTimeOffset.Now)
					.ToList();
				FirstExpiration = null;
				if (!Expirations.Any()) { return; }
				handleTask = true;
			} finally {
				expirationSemaphore.Release();
				if (handleTask) { HandleExpirationTask(); }
			}
		}

		private async void HandleExpirationTask() {
			await expirationSemaphore.WaitAsync();
			bool clearExpireds = false;
			CancellationToken token;
			TimeSpan delay;
			try {
				if (!Expirations.Any()) {
					return;
				}
				Expirations.Sort();
				if (FirstExpiration != null
				&& Expirations.First() >= FirstExpiration
				&& FirstExpiration > DateTimeOffset.Now) {
					return;
				}

				if (Expirations.First() <= DateTime.Now) {
					clearExpireds = true;
					return;
				}

				ExpirationSource?.Cancel();
				ExpirationSource = new CancellationTokenSource();
				token = ExpirationSource.Token;
				FirstExpiration = Expirations.First();
				delay = FirstExpiration - DateTime.Now ?? TimeSpan.Zero;
			} finally {
				expirationSemaphore.Release();
				if (clearExpireds) {
					_ = TryClearExpiredsAsync();
				}
			}


			try {
				if (delay > TimeSpan.Zero) {
					await Task.Delay(delay, token);
				}
			} catch (OperationCanceledException) { return; }

			if (token.IsCancellationRequested) {
				return;
			}

			ExpirationSource = null;
			FirstExpiration = null;
			_ = TryClearExpiredsAsync();
		}

		private void HandleUpdatedExpire(IEnumerable<SessionDetails> updated, Dictionary<Guid, SessionStopReason> stopped) {
			try {
				if (!updated.Any()) { return; }
				HandleNewExpireTimes(updated);
			} catch (Exception e) {
				ExceptionHandler?.Invoke(e);
			}
		}

		private void HandleAddedExpire(IEnumerable<SessionDetails> added) {
			try {
				if (!added.Any()) { return; }
				HandleNewExpireTimes(added);
			} catch (Exception e) {
				ExceptionHandler?.Invoke(e);
			}
		}

		private async void HandleNewExpireTimes(IEnumerable<SessionDetails> details) {
			await expirationSemaphore.WaitAsync();
			try {
				Expirations = Expirations
					.Concat(details.SelectMany(sd => sd.UserExpires.Values))
					.Distinct()
					.ToList();
			} finally { expirationSemaphore.Release(); }

			_ = TryClearExpiredsAsync();
		}
	}
}