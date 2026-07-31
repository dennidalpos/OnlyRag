namespace OnlyRag.Api;

internal static class AgentCycleGuard
{
    public static bool IsCyclicPatternDetected(List<string> history)
    {
        if (history.Count < 3) return false;
        int n = history.Count;

        static string GetToolName(string sig)
        {
            int idx = sig.IndexOf(':');
            return idx > 0 ? sig[..idx].Trim().ToLowerInvariant() : sig.Trim().ToLowerInvariant();
        }

        string t1 = GetToolName(history[n - 1]);
        string t2 = GetToolName(history[n - 2]);
        string t3 = GetToolName(history[n - 3]);

        if (t1 is "reflect_step" or "plan_task" && t2 is "reflect_step" or "plan_task" && t3 is "reflect_step" or "plan_task")
        {
            return true;
        }

        if (history.Count >= 4)
        {
            if (history[n - 1] == history[n - 2] && history[n - 2] == history[n - 3]) return true;

            for (int period = 2; period <= 4; period++)
            {
                if (n >= period * 3)
                {
                    bool matchExact = true;
                    bool matchToolName = true;

                    for (int i = 0; i < period; i++)
                    {
                        string elem = history[n - 1 - i];
                        string name = GetToolName(elem);

                        if (history[n - 1 - i - period] != elem || history[n - 1 - i - (period * 2)] != elem)
                        {
                            matchExact = false;
                        }

                        if (GetToolName(history[n - 1 - i - period]) != name || GetToolName(history[n - 1 - i - (period * 2)]) != name)
                        {
                            matchToolName = false;
                        }
                    }

                    if (matchExact || matchToolName) return true;
                }
            }
        }

        return false;
    }
}
