using DotemModel;

namespace DotemMatchmaker.Utils
{
    public static class DictionaryExtensions
    {

        public static void AddRange(this Dictionary<Guid, SessionStopReason> dictionary, IEnumerable<(Guid id, SessionStopReason stopReason)> valuesToAdd)
        {
            foreach (var pair in valuesToAdd)
            {
                dictionary.TryAdd(pair.id, pair.stopReason);
            }
        }
    }
}
