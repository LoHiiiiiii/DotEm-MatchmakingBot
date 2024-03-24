using Discord;
using Discord.Net;
using Discord.WebSocket;
using DotemDiscord.Utils;

namespace DotemDiscord.Handlers {

	public class ButtonCleanser {

		private readonly DiscordSocketClient _client;

		private const int TIMEOUT_MILLISECONDS = 6000;
		
		public ButtonCleanser(
			DiscordSocketClient client
		) {
			_client = client;
		}

		public void Initialize() {
			_client.ButtonExecuted += HandleButtonPress;
		}

		public Task HandleButtonPress(SocketMessageComponent component) {
			try {
				DeleteFailedInteraction(component);
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
			}
			return Task.CompletedTask;
		}

		public async void DeleteFailedInteraction(SocketMessageComponent component) {
			var message = component.Message;
			if (message == null) { return; }
			await Task.Delay(TIMEOUT_MILLISECONDS);
			if (component.HasResponded) { return; }
			try {
				await message.DeleteAsync();
			} catch (Exception e) {
				if (e is HttpException http && http.DiscordCode == DiscordErrorCode.UnknownMessage) {
					return;
				}
				throw;
			}
		}
	}
}
