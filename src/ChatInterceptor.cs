using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;

namespace TypeMend;

/// <summary>
/// Sits on the game's "chat box entry" function. A message is only held back when the handler
/// takes it; any failure lets the message through unchanged, so nothing the writer typed is lost.
/// </summary>
public sealed unsafe class ChatInterceptor : IDisposable
{
    private const AllowedEntities ChatEntities = (AllowedEntities)0x27F;

    private readonly Hook<UIModule.Delegates.ProcessChatBoxEntry> hook;
    private readonly Func<string, bool, bool> handler;
    private readonly IPluginLog log;
    private bool sending;

    public ChatInterceptor(IGameInteropProvider interop, IPluginLog log, Func<string, bool, bool> handler)
    {
        this.log = log;
        this.handler = handler;
        hook = interop.HookFromAddress<UIModule.Delegates.ProcessChatBoxEntry>(
            (nint)UIModule.MemberFunctionPointers.ProcessChatBoxEntry, Detour);
        hook.Enable();
    }

    private void Detour(UIModule* module, Utf8String* message, nint a4, bool saveToHistory)
    {
        try
        {
            if (!sending && message != null && handler(message->ToString(), saveToHistory)) return;
        }
        catch (Exception e)
        {
            log.Error(e, "TypeMend could not read a chat line; it was sent unchanged.");
        }
        hook.Original(module, message, a4, saveToHistory);
    }

    /// <summary>The channel the game would use right now for a line without its own channel command.</summary>
    public ChatChannel CurrentChannel()
    {
        var shell = RaptureShellModule.Instance();
        return new ChatChannel(shell->ChatType, shell->TellName.ToString(), shell->TellWorldId, shell->ContentId);
    }

    /// <summary>Sends a line through the game. Must be called on the framework thread.</summary>
    public bool Send(string text, bool saveToHistory)
    {
        var line = Utf8String.FromString(text);
        var check = Utf8String.FromString(text);
        try
        {
            check->SanitizeString(ChatEntities, null);
            if (check->ToString().Length != text.Length) return false;

            sending = true;
            UIModule.Instance()->ProcessChatBoxEntry(line, 0, saveToHistory);
            return true;
        }
        finally
        {
            sending = false;
            line->Dtor(true);
            check->Dtor(true);
        }
    }

    public void Dispose() => hook.Dispose();
}
