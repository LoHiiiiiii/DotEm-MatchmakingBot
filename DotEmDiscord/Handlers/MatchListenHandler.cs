using DotemMatchmaker;
using DotemModel;
using DotemDiscord.Context;
using DotemMatchmaker.Context;
using Discord.WebSocket;
using Discord;
using DotemDiscord.Utils;
using Discord.Net;
using DotemExtensions;
using DotemDiscord.ButtonMessages;

namespace DotemDiscord.Handlers {
	public class MatchListenHandler {

		private readonly Matchmaker _matchmaker;
		private readonly ButtonMessageHandler _buttonMessageHandler;
		private readonly MatchmakingContext _matchmakingContext;
		private readonly DiscordContext _discordContext;
		private readonly ExtensionContext _extensionContext;
		private readonly DiscordSocketClient _client;

		private HashSet<SessionDetails> sessionsToSuggest = new HashSet<SessionDetails>();
		private SemaphoreSlim suggestionSemaphore = new SemaphoreSlim(1, 1);

		public MatchListenHandler(
			Matchmaker matchmaker,
			ButtonMessageHandler buttonMessageHandler,
			MatchmakingContext matchmakingContext,
			DiscordContext discordContext,
			ExtensionContext extensionContext,
			DiscordSocketClient client
		) {
			_matchmaker = matchmaker;
			_buttonMessageHandler = buttonMessageHandler;
			_matchmakingContext = matchmakingContext;
			_discordContext = discordContext;
			_extensionContext = extensionContext;
			_client = client;
		}

		public async void HandleNewSearchMessage(SearchMessage searchMessage) {
			await suggestionSemaphore.WaitAsync();
			try {
				var added = searchMessage.Searches.Values
					.Where(sessionsToSuggest.Contains);

				if (!added.Any()) { return; }

				foreach (var session in added) {
					sessionsToSuggest.Remove(session);
					var listens = await _matchmakingContext.GetMatchListenersAsync(session.ServerId, session.GameId);
					if (!listens.Any()) { continue; }

					foreach (var userString in listens) {
						if (session.UserExpires.ContainsKey(userString)) { continue; }
						HandleSuggestion(session, userString, searchMessage.Message);
					}
				}
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
			} finally {
				suggestionSemaphore.Release();
			}
		}

		public async void HandleSessionChanged(IEnumerable<SessionDetails> added, IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped) {
			await suggestionSemaphore.WaitAsync();
			try {
				if (!added.Any()) { return; }

				foreach (var session in added) {
					if (sessionsToSuggest.Contains(session)) { continue; }
					sessionsToSuggest.Add(session);
				}
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
			} finally {
				suggestionSemaphore.Release();
			}
		}

		public void Initialize() {
			_matchmaker.SessionChanged += HandleSessionChanged;
			_buttonMessageHandler.SearchMessageCreated += HandleNewSearchMessage;
		}


		private async void HandleSuggestion(SessionDetails session, string userString, IUserMessage replyMessage) {
			if (!ulong.TryParse(userString, out var userId)) { return; }
			var user = await _client.GetUserAsync(userId);
			if (user == null) { return; }

			var result = await _buttonMessageHandler.GetSuggestionResultAsync(userId, [session], durationMinutes: null, allowCancel: true, searchParams: null);
			if (result == null) { return; }
			if (result is SessionResult.FailedToJoin) {
				var structure = MessageStructures.GetFailedJoinStructure();
				try {
					await user.SendMessageAsync(text: structure.content, components: structure.components);
				} catch (HttpException e) {
					if (e.DiscordCode == DiscordErrorCode.CannotSendMessageToUser) { return; }
					throw;
				}
				return;
			}

			if (result is SessionResult.Matched matched) {
				var structure = MessageStructures.GetMatchedStructure(
					matched.matchedSession.GameId,
					matched.matchedSession.UserExpires.Keys,
					matched.matchedSession.Description
				);

				await replyMessage.ReplyAsync(text: structure.content, components: structure.components);
				return;
			}
		}
	}
}
