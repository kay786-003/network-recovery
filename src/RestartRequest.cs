using System;

namespace NetworkRecovery
{
    public static class RestartRequest
    {
        // A durable reservation and durable reason are both prerequisites for shutdown.
        public static bool Execute(Func<bool> reserve, Func<bool> save, Action log, Func<int> shutdown)
        {
            if (!reserve() || !save()) return false;
            log();
            return shutdown() == 0;
        }
    }
}
