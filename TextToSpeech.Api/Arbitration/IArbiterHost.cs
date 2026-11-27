namespace TextToSpeech.Api.Arbitration
{
    /// <summary>The arbitration part of the API: agents, whose turn it is, and the reading policy. Call on the UI thread.</summary>
    public interface IArbiterHost
    {
        Task<ArbiterStatus> RegisterAsync(AgentRef agent);
        Task<ArbiterStatus> UnregisterAsync(string agentId);
        Task<ArbiterStatus> GetArbiterAsync();

        /// <summary>The user is in this agent's window (a client-side signal such as a prompt being submitted).</summary>
        Task<ArbiterStatus> FocusAsync(AgentRef agent);

        Task<PolicyResponse> GetPolicyAsync();

        /// <summary>Silences agents for some minutes. Agents may only make the policy stricter, never looser.</summary>
        Task<PolicyResponse> MuteAsync(int minutes);
    }

    /// <summary>Facts about the machine the policy and focus tracking need. The app implements it with Windows APIs.</summary>
    public interface IEnvironmentProbe
    {
        /// <summary>True for headphones or a headset as the default output, false for speakers, null when unknown.</summary>
        bool? HeadphonesConnected();

        /// <summary>Process id and title of the window the user is in, or null.</summary>
        (int Pid, string Title)? ForegroundWindow();

        /// <summary>Is <paramref name="ancestorPid"/> the process <paramref name="pid"/> itself or one of its parents?</summary>
        bool IsAncestor(int ancestorPid, int pid);

        /// <summary>Brings the window that hosts this process to the front. False when there is none or Windows refused.</summary>
        bool BringToFront(int pid);

        DateTimeOffset Now { get; }
    }
}
