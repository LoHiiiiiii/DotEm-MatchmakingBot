using DotemMatchmaker;
using DotemModel;
using DotemDiscord.Context;
using DotemMatchmaker.Context;
using Discord.WebSocket;
using Discord;
using DotemDiscord.Utils;

namespace DotemDiscord.Handlers {
	public class MatchListenHandler {

		private readonly Matchmaker _matchmaker;
		private readonly ButtonMessageHandler _buttonMessageHandler;
		private readonly MatchmakingContext _matchmakingContext;
		private readonly DiscordContext _discordContext;
		private readonly DiscordSocketClient _client;

		public MatchListenHandler(
			Matchmaker matchmaker,
			ButtonMessageHandler buttonMessageHandler,
			MatchmakingContext matchmakingContext,
			DiscordContext discordContext,
			DiscordSocketClient client
		) {
			_matchmaker = matchmaker;
			_matchmaker.SessionChanged += HandleSessionChanged;
			_buttonMessageHandler = buttonMessageHandler;
			_matchmakingContext = matchmakingContext;
			_discordContext = discordContext;
			_client = client;
		}

		public async void HandleSessionChanged(IEnumerable<SessionDetails> added, IEnumerable<SessionDetails> updated, IEnumerable<Guid> stopped) {
			if (!added.Any()) { return; }

			foreach (var session in added) {
				var listens = await _matchmakingContext.GetMatchListenersAsync(session.ServerId, session.GameId);
				if (!listens.Any()) { continue; }

				var message = await GetSearchMessage(session);

				if (message == null) { continue; }

				foreach (var userString in listens) {
					if (session.UserExpires.ContainsKey(userString)) { continue; }
					HandleSuggestion(session, message, userString);
				}
			}
		}

		private async Task<IUserMessage?> GetSearchMessage(SessionDetails session) {
			var idPairs = await _discordContext.GetSearchMessagesForSessionAsync(session.SessionId.ToString());

			foreach (var idPair in idPairs) {
				if (!ulong.TryParse(idPair.channelId, out var channelId)) { continue; }
				if (!ulong.TryParse(idPair.messageId, out var messageId)) { continue; }
				var channel = (IMessageChannel) await _client.GetChannelAsync(channelId);
				if (channel == null) { continue; }
				var message = (IUserMessage) await channel.GetMessageAsync(messageId);
				if (message == null) { continue; }
				return message;
			}
			return null;
		}

		private async void HandleSuggestion(SessionDetails session, IUserMessage message, string userString) {
			if (!ulong.TryParse(userString, out var userId)) { return; }
			var user = await _client.GetUserAsync(userId);
			if (user == null) { return; }

			var result = await _buttonMessageHandler.GetSuggestionResultAsync(userId, [session], durationMinutes: null, allowCancel: true, searchParams: null);
			if (result  == null) { return; }
			if (result is SessionResult.FailedToJoin) {
				var structure = MessageStructures.GetFailedJoinStructure();
				await user.SendMessageAsync(text: structure.content, components: structure.components);
				return;
			}
			
			if (result is SessionResult.Matched matched) {
				var structure = MessageStructures.GetMatchedStructure(
					matched.matchedSession.GameId,
					matched.matchedSession.UserExpires.Keys,
					matched.matchedSession.Description
				);

				await message.ReplyAsync(text: structure.content, components: structure.components);
				return;
			}
		}
	}
}
