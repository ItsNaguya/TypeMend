using System.Text;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace TypeMend;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/typemend";
    private const int ModelThreads = 2;

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly PreviewWindow preview;
    private readonly SettingsWindow settings;
    private readonly CorrectionMemory memory;
    private readonly ChatInterceptor interceptor;
    private readonly CancellationTokenSource shutdown = new();
    private volatile Repairer? repairer;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commands,
        IGameInteropProvider interop,
        IFramework framework,
        IChatGui chat,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.framework = framework;
        this.chat = chat;
        this.log = log;
        configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        memory = new CorrectionMemory(configuration.Corrections);
        preview = new PreviewWindow(Send)
        {
            RepairSent = (shown, sent) =>
            {
                if (memory.Learn(shown, sent)) pluginInterface.SavePluginConfig(configuration);
            },
        };
        interceptor = new ChatInterceptor(interop, log, OnChatLine);

        settings = new SettingsWindow(configuration, memory, () => pluginInterface.SavePluginConfig(configuration));

        commands.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Turns TypeMend on or off. /typemend words opens your never-change word list." });
        pluginInterface.UiBuilder.Draw += preview.Draw;
        pluginInterface.UiBuilder.Draw += settings.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += settings.Open;

        var directory = pluginInterface.AssemblyLocation.DirectoryName!;
        Task.Run(() => LoadModel(directory));
    }

    private void LoadModel(string directory)
    {
        try
        {
            repairer = new Repairer(directory, ModelThreads);
            log.Information("TypeMend model loaded.");
        }
        catch (Exception e)
        {
            log.Error(e, "TypeMend could not load its model.");
            framework.RunOnFrameworkThread(() => chat.PrintError("[TypeMend] The repair model could not load, so your messages will be sent as typed."));
        }
    }

    /// <summary>Returns true when the line is being held for the preview.</summary>
    private bool OnChatLine(string line, bool saveToHistory)
    {
        var model = repairer;
        if (!configuration.Enabled || model == null || preview.IsOpen) return false;
        if (!ChatLine.TrySplit(line, out var prefix, out var body) || !Repairer.CanRepair(body)) return false;

        preview.Open(line, prefix, saveToHistory, interceptor.CurrentChannel());
        var token = shutdown.Token;
        Task.Run(() => RepairBody(model, body, token), token);
        return true;
    }

    private void RepairBody(Repairer model, string body, CancellationToken token)
    {
        try
        {
            var output = new StringBuilder();
            var unsure = new List<string>();
            var keep = ChatLine.ParseKeepWords(configuration.NeverChange);
            var parts = ChatLine.SplitPlaceholders(body);
            for (var i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 1 || string.IsNullOrWhiteSpace(parts[i]))
                {
                    output.Append(parts[i]);
                    continue;
                }
                var repair = model.RunKeeping(parts[i], keep, token);
                var learned = memory.Apply(repair.Text);
                if (parts[i].StartsWith(' ')) output.Append(' ');
                output.Append(learned);
                if (parts[i].EndsWith(' ')) output.Append(' ');
                var words = learned.Split(' ');
                unsure.AddRange(repair.UnsureWords.Where(words.Contains));
            }
            preview.SetResult(output.ToString(), unsure);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            log.Error(e, "TypeMend repair failed.");
            preview.SetFailed("The repair failed. You can still send what you typed.");
        }
    }

    /// <summary>The window stays open until this has run, so a refused line is never lost.</summary>
    private void Send(string line, bool saveToHistory, string prefix, ChatChannel heldChannel) => framework.RunOnFrameworkThread(() =>
    {
        if (!ChatChannel.SafeToSend(prefix, heldChannel, interceptor.CurrentChannel()))
            preview.SendRefused("Not sent: your chat channel changed after you wrote this. Switch back to send it, or copy it from the box above.");
        else if (interceptor.Send(line))
            preview.SendDone();
        else
            preview.SendRefused("The game would not accept that text, so nothing was sent.");
    });

    private void OnCommand(string command, string arguments)
    {
        if (arguments.Trim().Equals("words", StringComparison.OrdinalIgnoreCase))
        {
            settings.Open();
            return;
        }
        configuration.Enabled = !configuration.Enabled;
        pluginInterface.SavePluginConfig(configuration);
        chat.Print($"[TypeMend] {(configuration.Enabled ? "On" : "Off: messages are sent as typed")}.");
    }

    public void Dispose()
    {
        shutdown.Cancel();
        commands.RemoveHandler(Command);
        pluginInterface.UiBuilder.Draw -= preview.Draw;
        pluginInterface.UiBuilder.Draw -= settings.Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= settings.Open;
        interceptor.Dispose();
        repairer?.Dispose();
        shutdown.Dispose();
    }
}
