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
        preview = new PreviewWindow(Send);
        interceptor = new ChatInterceptor(interop, log, OnChatLine);

        commands.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Turns TypeMend on or off." });
        pluginInterface.UiBuilder.Draw += preview.Draw;

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

        preview.Open(line, prefix, saveToHistory);
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
            var parts = ChatLine.SplitPlaceholders(body);
            for (var i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 1 || string.IsNullOrWhiteSpace(parts[i]))
                {
                    output.Append(parts[i]);
                    continue;
                }
                var repair = model.Run(parts[i], token);
                if (parts[i].StartsWith(' ')) output.Append(' ');
                output.Append(repair.Text);
                if (parts[i].EndsWith(' ')) output.Append(' ');
                unsure.AddRange(repair.UnsureWords);
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

    private void Send(string line, bool saveToHistory) => framework.RunOnFrameworkThread(() =>
    {
        if (!interceptor.Send(line, saveToHistory))
            chat.PrintError("[TypeMend] The game would not accept that text, so nothing was sent.");
    });

    private void OnCommand(string command, string arguments)
    {
        configuration.Enabled = !configuration.Enabled;
        pluginInterface.SavePluginConfig(configuration);
        chat.Print($"[TypeMend] {(configuration.Enabled ? "On" : "Off: messages are sent as typed")}.");
    }

    public void Dispose()
    {
        shutdown.Cancel();
        commands.RemoveHandler(Command);
        pluginInterface.UiBuilder.Draw -= preview.Draw;
        interceptor.Dispose();
        repairer?.Dispose();
        shutdown.Dispose();
    }
}
