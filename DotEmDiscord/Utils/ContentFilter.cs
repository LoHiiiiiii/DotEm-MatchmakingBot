using System.Text.RegularExpressions;

namespace DotemDiscord.Utils {
	public static class ContentFilter {

		private static readonly string[] regexs = { "<*>", "https:\\/\\/", "http:\\/\\/" };
		private const int MAX_SEARCH_DURATION = 1440;
		public const int MAX_SYMBOL_COUNT = 200;

		public static bool ContainsForbidden(string text) {
			foreach (var regex in regexs) {
				if (!Regex.IsMatch(text, regex)) { continue; }
				return true;
			}

			return false;
		}

		public static int CapSearchDuration(int duration) {
			return Math.Clamp(duration, 0, 1440);
		}

		public static string[] CapSymbolCount(IEnumerable<string> ids) {
			return ids.Select(s => s.Length > MAX_SYMBOL_COUNT ? s.Substring(0,MAX_SYMBOL_COUNT) : s).ToArray();
		}
	}
}
