namespace TypeMend;

/// <summary>Where a line without its own channel command would go: the channel, and the person for a tell.</summary>
public readonly record struct ChatChannel(int Type, string TellName, ushort TellWorld, ulong TellContentId)
{
    /// <summary>
    /// A held line with its own channel command (like /p or /tell) always goes to that place. Without one it
    /// goes to the active channel, so it may only be sent while that is still the channel it was written for.
    /// </summary>
    public static bool SafeToSend(string linePrefix, ChatChannel whenHeld, ChatChannel now) =>
        linePrefix.Length > 0 || whenHeld == now;
}
