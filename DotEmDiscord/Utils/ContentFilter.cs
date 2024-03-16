using System.Text.RegularExpressions;

namespace DotemDiscord.Utils {
	public static class ContentFilter {

		private static readonly string[] regexs = { "<.*>", "https:\\/\\/", "http:\\/\\/", "\\*.*\\*", "_.*_", "\n"};
		private const int MAX_SEARCH_DURATION = 1440;
		public const int MAX_PLAYER_COUNT = 99;
		public const int MAX_SYMBOL_COUNT = 200;

		public static bool ContainsForbidden(string text) {
			foreach (var regex in regexs) {
				if (!Regex.IsMatch(text, regex)) { continue; }
				return true;
			}

			return false;
		}

		public static string? ContainsForbidden(string[] texts) {
			foreach(var text in texts) {
				if (ContainsForbidden(text)) { return text; }
			}
			return null;
		}

		public static int CapSearchDuration(int duration) {
			return Math.Clamp(duration, 0, MAX_SEARCH_DURATION);
		}

		public static int CapPlayerCount(int count) {
			return Math.Clamp(count, 0, MAX_PLAYER_COUNT);
		}

		public static string[] CapSymbolCount(IEnumerable<string> ids) {
			return ids.Select(CapSymbolCount).ToArray();
		}

		public static string CapSymbolCount(string s) {
			return s.Length > MAX_SYMBOL_COUNT ? s.Substring(0, MAX_SYMBOL_COUNT) : s;
		}
	}
}
