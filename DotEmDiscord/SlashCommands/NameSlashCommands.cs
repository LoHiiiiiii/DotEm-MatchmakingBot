﻿using Discord.Interactions;
using Discord.WebSocket;
using DotemDiscord.Utils;
using Discord;
using DotemMatchmaker;
using System.Text;
using Discord.Net;

namespace DotemDiscord.SlashCommands {
	public class NameSlashCommands : InteractionModuleBase<SocketInteractionContext<SocketSlashCommand>> {

		private readonly Matchmaker _matchmaker;

		public NameSlashCommands(Matchmaker matchmaker) {
			_matchmaker = matchmaker;
			_matchmaker = matchmaker;
		}

		[EnabledInDm(false)]
		[DefaultMemberPermissions(GuildPermission.ManageGuild)]
		[SlashCommand("add-alias", "Adds an Alias ID for Game Ids.")]
		public async Task AddGameAliasesSlashCommandAsync(string aliasId, string gameIds) {
			try {
				await DeferAsync();
				if (string.IsNullOrEmpty(aliasId) || string.IsNullOrWhiteSpace(aliasId)) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = "Alias ID cannot be empty.";
					});
					return;
				}
				var splitAlias = aliasId.Split(' ');
				if (splitAlias.Length > 1) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = "Alias ID cannot contain a space.";
					});
					return;
				}
				var splitGames = gameIds
					.Split(' ')
					.Where(s => !string.IsNullOrEmpty(s) || !string.IsNullOrWhiteSpace(s))
					.ToArray();

				if (!splitGames.Any()) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = "Provide at least one valid Game ID.";
					});
					return;
				}
				await _matchmaker.AddGameAliasAsync(Context.Guild.Id.ToString(), splitAlias.Single(), splitGames);
				var response = MessageStructures.GetNaturalLanguageString(splitGames);
				await ModifyOriginalResponseAsync(x => {
					x.Content = $"Added Alias ID {splitAlias.Single()} for Game ID{(splitGames.Length > 1 ? "s" : "")} {response}.";
				});
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}

		[EnabledInDm(false)]
		[DefaultMemberPermissions(GuildPermission.ManageGuild)]
		[SlashCommand("add-game-name", "Adds a longer name for a Game Id.")]
		public async Task AddGameNameSlashCommandAsync(string gameId, string gameName) {
			try {
				await DeferAsync();


				if (ContentFilter.ContainsForbidden(gameId)) {
					var forbiddenStructure = MessageStructures.GetForbiddenStructure(gameId);

					await ModifyOriginalResponseAsync(x => {
						x.Content = forbiddenStructure.content;
						x.Components = forbiddenStructure.components;
						x.AllowedMentions = AllowedMentions.None;
					});

					return;
				}

				if (ContentFilter.ContainsForbidden(gameName)) {
					var forbiddenStructure = MessageStructures.GetForbiddenStructure(gameName);

					await ModifyOriginalResponseAsync(x => {
						x.Content = forbiddenStructure.content;
						x.Components = forbiddenStructure.components;
						x.AllowedMentions = AllowedMentions.None;
					});

					return;
				}

				if (string.IsNullOrEmpty(gameId) || string.IsNullOrWhiteSpace(gameId)) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = "Game ID cannot be empty.";
					});
					return;
				}
				var splitGame = gameId.Split(' ');
				if (splitGame.Length > 1) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = "Game ID cannot contain a space.";
					});
					return;
				}

				if (string.IsNullOrEmpty(gameName) || string.IsNullOrWhiteSpace(gameName)) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = "Game Name cannot be empty";
					});
					return;
				}

				var id = ContentFilter.CapSymbolCount(splitGame.Single());
				gameName = ContentFilter.CapSymbolCount(gameName);

				await _matchmaker.AddGameNameAsync(Context.Guild.Id.ToString(), id, gameName);
				await ModifyOriginalResponseAsync(x => {
					x.Content = $"Added Game Name {gameName} for Game ID {id}.";
				});
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				if (e is HttpException unknown && unknown.DiscordCode == DiscordErrorCode.UnknownInteraction) return;
				if (e is HttpException acknowledged && acknowledged.DiscordCode == DiscordErrorCode.InteractionHasAlreadyBeenAcknowledged) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}

		[EnabledInDm(false)]
		[DefaultMemberPermissions(GuildPermission.ManageGuild)]
		[SlashCommand("remove-alias", "Removes Alias IDs from Game IDs.")]
		public async Task RemoveGameAliasesMatchCommandAsync(string gameIds) {
			try {
				await DeferAsync();
				var splitGames = gameIds
					.Split(' ')
					.Where(s => !string.IsNullOrEmpty(s) || !string.IsNullOrWhiteSpace(s))
					.ToArray();

				if (!splitGames.Any()) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = "Provide at least one valid Game ID.";
					});
					return;
				}
				await _matchmaker.DeleteGameAliasesAsync(Context.Guild.Id.ToString(), splitGames);
				var response = MessageStructures.GetNaturalLanguageString(splitGames);
				var pluralSuffix = splitGames.Length > 1 ? "s" : "";
				await ModifyOriginalResponseAsync(x => {
					x.Content = $"Removed Alias ID{pluralSuffix} from Game ID{pluralSuffix} {response}.";
				});
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				if (e is HttpException unknown && unknown.DiscordCode == DiscordErrorCode.UnknownInteraction) return;
				if (e is HttpException acknowledged && acknowledged.DiscordCode == DiscordErrorCode.InteractionHasAlreadyBeenAcknowledged) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}

		[EnabledInDm(false)]
		[DefaultMemberPermissions(GuildPermission.ManageGuild)]
		[SlashCommand("remove-game-name", "Removes longer game name from Game IDs.")]
		public async Task RemoveGameNamesSlashCommandAsync(string gameIds) {
			try {
				await DeferAsync();
				var splitGames = gameIds
					.Split(' ')
					.Where(s => !string.IsNullOrEmpty(s) || !string.IsNullOrWhiteSpace(s))
					.ToArray();

				if (!splitGames.Any()) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = "Provide at least one valid Game ID.";
					});
					return;
				}
				await _matchmaker.DeleteGameNamesAsync(Context.Guild.Id.ToString(), splitGames);
				var response = MessageStructures.GetNaturalLanguageString(splitGames);
				var pluralSuffix = splitGames.Length > 1 ? "s" : "";
				await ModifyOriginalResponseAsync(x => {
					x.Content = $"Removed Game Name{pluralSuffix} from Game ID{pluralSuffix} {response}.";
				});
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				if (e is HttpException unknown && unknown.DiscordCode == DiscordErrorCode.UnknownInteraction) return;
				if (e is HttpException acknowledged && acknowledged.DiscordCode == DiscordErrorCode.InteractionHasAlreadyBeenAcknowledged) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}


		const int DISCORD_MESSAGE_LENGTH_MAX = 2000;
		[EnabledInDm(false)]
		[SlashCommand("list-games", "Lists saved game names and Alias IDs")]
		public async Task ListGamesSlashCommandAsync() {
			try {
				await DeferAsync(true);

				var aliases = await _matchmaker.GetAllGameAliasesAsync(Context.Guild.Id.ToString());
				var aliasGameIds = new Dictionary<string, string>();
				foreach (var kvp in aliases) {
					if (!aliasGameIds.ContainsKey(kvp.Value)) {
						aliasGameIds[kvp.Value] = kvp.Key;
					} else {
						aliasGameIds[kvp.Value] += $", {kvp.Key}";
					}
				}

				var names = await _matchmaker.GetAllGameNamesAsync(Context.Guild.Id.ToString());
				var ids = aliasGameIds.Keys.Concat(names.Keys).Distinct();

				var nameRows = ids
					.OrderBy(id => id)
					.OrderBy(id => names.GetValueOrDefault(id) ?? "")
					.Select(id =>
						$"{id}{(aliasGameIds.ContainsKey(id) ? $" ({aliasGameIds[id]})" : "")}{(names.ContainsKey(id) ? $" - {names[id]}" : "")}")
					.ToArray();

				if (!nameRows.Any()) {
					await ModifyOriginalResponseAsync(x => {
						x.Content = "No registered games on this server.";
					});
					return;
				}

				var builder = new StringBuilder();
				var originalModified = false;

				for (int i = 0; i < nameRows.Length; ++i) {
					var value = nameRows[i];
					if (value.Length > DISCORD_MESSAGE_LENGTH_MAX) {
						value = value.Substring(0, DISCORD_MESSAGE_LENGTH_MAX);
					}

					builder.Append(value);
					if (i == nameRows.Length - 1 || builder.Length + nameRows[i + 1].Length > DISCORD_MESSAGE_LENGTH_MAX - 2) {
						if (!originalModified) {
							originalModified = true;
							await ModifyOriginalResponseAsync(x => {
								x.Content = builder.ToString();
							});
						} else {
							await FollowupAsync(text: builder.ToString(), ephemeral: true);
						}
						builder.Clear();
					} else {
						builder.Append("\n");
					}
				}
			} catch (Exception e) {
				ExceptionHandling.ReportExceptionToFile(e);
				if (e is TimeoutException) return;
				if (e is HttpException unknown && unknown.DiscordCode == DiscordErrorCode.UnknownInteraction) return;
				if (e is HttpException acknowledged && acknowledged.DiscordCode == DiscordErrorCode.InteractionHasAlreadyBeenAcknowledged) return;
				await ExceptionHandling.ReportInteractionExceptionAsync(Context.Interaction);
			}
		}

	}
}