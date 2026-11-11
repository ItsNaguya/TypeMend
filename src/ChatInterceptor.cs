using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.Shell;

namespace TypeMend;

/// <summary>
/// Sits on the function the game runs for every chat line and command, typed ones included. A message is only
/// held back when the handler takes it; any failure lets it through unchanged, so nothing the writer typed is lost.
/// Lines from running macros are never held.
/// </summary>
public sealed unsafe class ChatInterceptor : IDisposable
{
    private const AllowedEntities ChatEntities = (AllowedEntities)0x27F;

    private readonly Hook<ShellCommandModule.Delegates.ExecuteCommandInner> hook;
    private readonly Func<string, bool, bool> handler;
    private readonly IPluginLog log;
    private bool sending;

    public ChatInterceptor(IGameInteropProvider interop, IPluginLog log, Func<string, bool, bool> handler)
    {
        this.log = log;
        this.handler = handler;
        hook = interop.HookFromAddress<ShellCommandModule.Delegates.ExecuteCommandInner>(
            (nint)ShellCommandModule.MemberFunctionPointers.ExecuteCommandInner, Detour);
        hook.Enable();
    }

    private void Detour(ShellCommandModule* module, Utf8String* message, UIModule* uiModule)
    {
        try
        {
            if (!sending && message != null && RaptureShellModule.Instance()->MacroCurrentLine < 0
                && handler(message->ToString(), true)) return;
        }
        catch (Exception e)
        {
            log.Error(e, "TypeMend could not read a chat line; it was sent unchanged.");
        }
        hook.Original(module, message, uiModule);
    }

    /// <summary>The channel the game would use right now for a line without its own channel command.</summary>
    public ChatChannel CurrentChannel()
    {
        var shell = RaptureShellModule.Instance();
        return new ChatChannel(shell->ChatType, shell->TellName.ToString(), shell->TellWorldId, shell->ContentId);
    }

    /// <summary>
    /// Sends a line through the game. Must be called on the framework thread. It is not added to the chat
    /// history again: asking the game to do that from here crashed it (2026-11-11, with Simple Tweaks' history tweak).
    /// </summary>
    public bool Send(string text)
    {
        var line = Utf8String.FromString(text);
        var check = Utf8String.FromString(text);
        try
        {
            check->SanitizeString(ChatEntities, null);
            if (check->ToString().Length != text.Length) return false;

            sending = true;
            UIModule.Instance()->ProcessChatBoxEntry(line);
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
