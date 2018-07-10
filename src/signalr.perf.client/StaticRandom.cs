using System;

namespace signalr.perf.client
{
    internal class StaticRandom
    {
        private static readonly object RandomLock = new object();
        private static readonly Random RandomInterval = new Random((int)DateTime.UtcNow.Ticks);

        public static int Next(int maxValue)
        {
            lock (RandomLock)
            {
                return RandomInterval.Next(maxValue);
            }
        }
    }
}