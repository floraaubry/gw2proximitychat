using System.Text;

namespace GW2ProximityChat
{
    /// <summary>
    /// FNV-1a hashing so instance keys are stable and comparable across
    /// separate client processes. string.GetHashCode() is randomized per
    /// process in .NET and cannot be used for that purpose.
    /// </summary>
    public static class StableHash
    {
        public static string InstanceKey(string serverAddress, uint shardId)
        {
            return Fnv1a($"{serverAddress}|{shardId}").ToString("x8");
        }

        private static uint Fnv1a(string value)
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;

            uint hash = offsetBasis;
            foreach (byte b in Encoding.UTF8.GetBytes(value))
            {
                hash ^= b;
                hash *= prime;
            }

            return hash;
        }
    }
}
