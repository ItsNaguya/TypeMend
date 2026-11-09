// Runs the plugin's Repairer outside the game.
// usage: Harness <directory holding model\ and onnxruntime.dll> <threads> <file with one message per line>
using System.Diagnostics;
using TypeMend;

var failures = 0;
void Check(string line, bool repairs, string prefix, string body)
{
    var result = ChatLine.TrySplit(line, out var p, out var b);
    var ok = result == repairs && (!repairs || (p == prefix && b == body));
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} [{line}] -> {result} [{p}] [{b}]");
}
Check("hello there", true, "", "hello there");
Check("/s hello there", true, "/s ", "hello there");
Check("/fc hello there", true, "/fc ", "hello there");
Check("/cwl3 hello there", true, "/cwl3 ", "hello there");
Check("/t Some Name@Shiva hello there", true, "/t Some Name@Shiva ", "hello there");
Check("/tell Some Name hello there", true, "/tell Some Name ", "hello there");
Check("/t <r> hello there", true, "/t <r> ", "hello there");
Check("/r hello there", true, "/r ", "hello there");
Check("/gearset change 3", false, "", "");
Check("/ac \"Fire\" <t>", false, "", "");
Check("/typemend", false, "", "");
Check("/s", false, "", "");
Check("/t Some Name@Shiva", false, "", "");
Check("12345 ...", false, "", "");
Check("", false, "", "");
var split = ChatLine.SplitPlaceholders("look at <t> now");
if (split.Length != 3 || split[1] != "<t>") { failures++; Console.WriteLine("FAIL placeholders"); }
Console.WriteLine(Repairer.CanRepair("plain words") && !Repairer.CanRepair("schön") ? "ok   CanRepair" : "FAIL CanRepair");
void Channel(string name, string prefix, ChatChannel held, ChatChannel now, bool expected)
{
    var ok = ChatChannel.SafeToSend(prefix, held, now) == expected;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} channel: {name}");
}
var say = new ChatChannel(1, "", 0, 0);
var fc = new ChatChannel(6, "", 0, 0);
var tellOne = new ChatChannel(0, "Some Name", 21, 111);
var tellOther = new ChatChannel(0, "Other Name", 21, 222);
Channel("same channel sends", "", say, say, true);
Channel("tell then back to say is refused", "", tellOne, say, false);
Channel("say then free company is refused", "", say, fc, false);
Channel("tell to a different person is refused", "", tellOne, tellOther, false);
Channel("own channel command ignores the switch", "/p ", say, fc, true);
Console.WriteLine($"chat line failures: {failures}");

var directory = args[0];
var threads = int.Parse(args[1]);
var messages = File.ReadAllLines(args[2]).Where(l => l.Length > 0).ToArray();

var load = Stopwatch.StartNew();
using var repairer = new Repairer(directory, threads);
Console.WriteLine($"loaded in {load.ElapsedMilliseconds} ms");

var times = new List<long>();
foreach (var message in messages)
{
    var timer = Stopwatch.StartNew();
    var repair = repairer.Run(message);
    times.Add(timer.ElapsedMilliseconds);
    Console.WriteLine($"OUT\t{repair.Text}\t{string.Join(" | ", repair.UnsureWords)}");
}
times.Sort();
Console.WriteLine($"threads {threads}: median {times[times.Count / 2]} ms, worst {times[^1]} ms");
Console.WriteLine($"private memory {Process.GetCurrentProcess().PrivateMemorySize64 / 1_000_000} MB");
