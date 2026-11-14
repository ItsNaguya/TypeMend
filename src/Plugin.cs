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
    private volatile string modelState = "Loading...";

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
            HideTips = () =>
            {
                configuration.ShowTips = false;
                pluginInterface.SavePluginConfig(configuration);
            },
        };
        interceptor = new ChatInterceptor(interop, log, OnChatLine);

        settings = new SettingsWindow(configuration, memory, () => pluginInterface.SavePluginConfig(configuration), () => modelState);

        commands.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Opens TypeMend. /typemend on or off turns repairs on or off; /typemend words opens your never-change list." });
        pluginInterface.UiBuilder.Draw += Draw;
        pluginInterface.UiBuilder.OpenConfigUi += settings.Open;
        pluginInterface.UiBuilder.OpenMainUi += settings.Open;
        if (!configuration.SeenWelcome) settings.Open();

        var directory = pluginInterface.AssemblyLocation.DirectoryName!;
        Task.Run(() => LoadModel(directory));
    }

    private void LoadModel(string directory)
    {
        try
        {
            repairer = new Repairer(directory, ModelThreads);
            modelState = "Ready, running on this PC";
            log.Information("TypeMend model loaded.");
        }
        catch (Exception e)
        {
            modelState = "Could not load, so messages go out as typed. See /xllog.";
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
            var keep = ChatLine.ParseKeepWords(configuration.NeverChange).Concat(ChatLine.ChatWords).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (configuration.KeepRoleplayWords) keep = keep.Concat(ChatLine.RoleplayWords).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var swears = configuration.KeepSwearWords;
            if (swears) keep = keep.Concat(SwearWords.Words).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var parts = ChatLine.SplitPlaceholders(body);
            for (var i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 1 || string.IsNullOrWhiteSpace(parts[i]))
                {
                    output.Append(parts[i]);
                    continue;
                }
                var (typed, swearFixes) = swears ? SwearWords.FixTypos(parts[i], keep) : (parts[i], new List<string>());
                var repair = model.RunKeeping(typed.Trim(), keep, token);
                var learned = memory.Apply(repair.Text);
                if (parts[i].StartsWith(' ')) output.Append(' ');
                output.Append(learned);
                if (parts[i].EndsWith(' ')) output.Append(' ');
                var words = learned.Split(' ');
                unsure.AddRange(repair.UnsureWords.Concat(swearFixes).Where(words.Contains));
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

    private void Draw()
    {
        preview.ShowTips = configuration.ShowTips;
        preview.Draw();
        settings.Draw();
    }

    private void OnCommand(string command, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "words":
                settings.Open(SettingsWindow.Tab.NeverChange);
                break;
            case "on":
                SetEnabled(true);
                break;
            case "off":
                SetEnabled(false);
                break;
            case "toggle":
                SetEnabled(!configuration.Enabled);
                break;
            case "":
                settings.Open();
                break;
            default:
                chat.Print("[TypeMend] Use /typemend, /typemend on, /typemend off or /typemend words.");
                break;
        }
    }

    private void SetEnabled(bool on)
    {
        configuration.Enabled = on;
        pluginInterface.SavePluginConfig(configuration);
        chat.Print(on ? "[TypeMend] On: you will see a preview before each message." : "[TypeMend] Off: messages are sent as typed.");
    }

    public void Dispose()
    {
        shutdown.Cancel();
        commands.RemoveHandler(Command);
        pluginInterface.UiBuilder.Draw -= Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= settings.Open;
        pluginInterface.UiBuilder.OpenMainUi -= settings.Open;
        interceptor.Dispose();
        repairer?.Dispose();
        shutdown.Dispose();
    }
}
