namespace OnlyRag.Api;

internal static class AgentCycleGuard
{
    public static bool IsCyclicPatternDetected(List<string> history)
    {
        if (history.Count < 3) return false;
        int n = history.Count;

        // 1. Detect 3 consecutive EXACT identical tool calls (same tool and exact same arguments)
        if (string.Equals(history[n - 1], history[n - 2], StringComparison.OrdinalIgnoreCase) &&
            string.Equals(history[n - 2], history[n - 3], StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 2. Detect 3 consecutive identical meta-tool calls with empty or identical arguments
        static string GetToolName(string sig)
        {
            int idx = sig.IndexOf(':');
            return idx > 0 ? sig[..idx].Trim().ToLowerInvariant() : sig.Trim().ToLowerInvariant();
        }

        string t1 = GetToolName(history[n - 1]);
        string t2 = GetToolName(history[n - 2]);
        string t3 = GetToolName(history[n - 3]);

        if ((t1 == "reflect_step" && t2 == "reflect_step" && t3 == "reflect_step") ||
            (t1 == "plan_task" && t2 == "plan_task" && t3 == "plan_task"))
        {
            if (string.Equals(history[n - 1], history[n - 2], StringComparison.OrdinalIgnoreCase) &&
                string.Equals(history[n - 2], history[n - 3], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // 3. Detect repeating exact cycles of period 2 to 4 (must match exact call signatures, not just tool names)
        if (history.Count >= 6)
        {
            for (int period = 2; period <= 4; period++)
            {
                if (n >= period * 3)
                {
                    bool matchExact = true;

                    for (int i = 0; i < period; i++)
                    {
                        string elem = history[n - 1 - i];
                        if (!string.Equals(history[n - 1 - i - period], elem, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(history[n - 1 - i - (period * 2)], elem, StringComparison.OrdinalIgnoreCase))
                        {
                            matchExact = false;
                            break;
                        }
                    }

                    if (matchExact) return true;
                }
            }
        }

        return false;
    }
}

