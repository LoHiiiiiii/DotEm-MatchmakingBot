using Discord.WebSocket;
using Discord;
using DotemChatMatchmaker;
using DotemDiscord.ButtonMessages;
using DotemDiscord.Utils;
using DotemModel;

namespace DotemDiscord.Handlers {
	public class ButtonMessageHandler {

		public int TimeoutMinutes { get; }

		public ButtonMessageHandler(int timeoutMinutes) {
			TimeoutMinutes = timeoutMinutes;
		}	

		public async Task<SessionResult> GetSuggestionResultAsync(
			DiscordSocketClient client,
			ChatMatchmaker matchmaker,
			SocketInteraction interaction,
			IEnumerable<SessionDetails> joinableSessions,
			int durationMinutes,
			(string[]? gameIds, string? description, int? playerCount)? searchParams
		) {
			var guid = Guid.NewGuid();
			var structure = MessageStructures.GetSuggestionStructure(
				joinables: joinableSessions,
				userId: interaction.User.Id,
				searchId: searchParams != null
					? guid
					: null
			);
			var followup = await interaction.FollowupAsync(
				text: structure.content,
				components: structure.components,
				ephemeral: true
			);
			var suggestion = new SuggestionMessage(
				client: client,
				chatMatchmaker: matchmaker,
				message: followup,
				joinableSessions: joinableSessions,
				creatorId: interaction.User.Id,
				durationMinutes: durationMinutes,
				searchParams: searchParams,
				id: guid
			);
			suggestion.MessageSemaphore.Release();

			var cts = new CancellationTokenSource();
			SuggestionTimeout(durationMinutes, cts, suggestion);

			await suggestion.SuggestionSignal.WaitAsync(cts.Token);
			return suggestion.ExitResult ?? new SessionResult.NoAction();
		}

		public async void SuggestionTimeout(int durationMinutes, CancellationTokenSource tokenSource, SuggestionMessage suggestionMessage) {
			await Task.Delay(durationMinutes * 60 * 1000);
			if (suggestionMessage.Released) { return; }
			tokenSource.Cancel();
		}

		public async Task<SessionResult> GetSuggestionResultAsync(
			DiscordSocketClient client,
			ChatMatchmaker matchmaker,
			IUser user,
			IEnumerable<SessionDetails> joinableSessions,
			int durationMinutes,
			(string[]? gameIds, string? description, int? playerCount)? searchParams
		) {
			var guid = Guid.NewGuid();
			var structure = MessageStructures.GetSuggestionStructure(
				joinables: joinableSessions,
				userId: user.Id,
				searchId: searchParams != null
					? guid
					: null
			);
			var dm = await user.SendMessageAsync(
				text: structure.content,
				components: structure.components
			);
			var suggestion = new SuggestionMessage(
				client: client,
				chatMatchmaker: matchmaker,
				message: dm,
				joinableSessions: joinableSessions,
				creatorId: user.Id,
				durationMinutes: durationMinutes,
				searchParams: searchParams,
				id: guid
			);
			suggestion.MessageSemaphore.Release();

			await suggestion.SuggestionSignal.WaitAsync();
			return suggestion.ExitResult ?? new SessionResult.NoAction();
		}


		public SearchMessage CreateSearchMessage(
			DiscordSocketClient client,
			ChatMatchmaker matchmaker,
			IUserMessage message,
			IEnumerable<SessionDetails> searches,
			ulong creatorId) => new SearchMessage(client, matchmaker, message, searches, creatorId);
	}
}
