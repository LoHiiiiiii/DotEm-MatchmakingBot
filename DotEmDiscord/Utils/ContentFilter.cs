using System.Text.RegularExpressions;

namespace DotemDiscord.Utils {
	public static class ContentFilter {

		public static readonly string[] regexs = { "<*>", "https:\\/\\/", "http:\\/\\/" };

		public static bool ContainsForbidden(string text) {
			foreach (var regex in regexs) {
				if (!Regex.IsMatch(text, regex)) { continue; }
				return true;
			}

			return false;
		}
	}
}
