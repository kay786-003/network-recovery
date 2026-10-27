using System;
using System.Collections.Generic;

namespace NetworkRecovery
{
    public static class WifiRecoveryPolicy
    {
        public static bool TargetAbsent(List<WifiNetwork> networks, string ssidHex, string profile)
        {
            bool visible = false;
            foreach (WifiNetwork network in networks)
            {
                if (!network.IsVisible) continue;
                visible = true;
                if ((!String.IsNullOrEmpty(ssidHex) && String.Equals(network.SsidHex, ssidHex, StringComparison.OrdinalIgnoreCase)) ||
                    String.Equals(network.ProfileName, profile, StringComparison.Ordinal)) return false;
            }
            return visible;
        }

        public static List<string> Candidates(List<WifiNetwork> networks, string primary)
        {
            List<string> result = new List<string>();
            foreach (WifiNetwork network in networks)
                if (network.IsVisible && network.HasSavedProfile && network.IsConnectable &&
                    !String.IsNullOrEmpty(network.ProfileName) && !result.Contains(network.ProfileName)) result.Add(network.ProfileName);
            result.Sort(StringComparer.Ordinal);
            if (result.Remove(primary)) result.Insert(0, primary);
            return result;
        }
    }
}
