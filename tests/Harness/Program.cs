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
var marks = ChatLine.SplitPlaceholders("~purrs softly~ ((brb))");
Console.WriteLine($"{(marks.SequenceEqual(new[] { "", "~", "purrs softly", "~", " ", "((", "brb", "))", "" }) ? "ok  " : "FAIL")} roleplay marks kept: [{string.Join("|", marks)}]");
Console.WriteLine(Repairer.CanRepair("plain words") && !Repairer.CanRepair("schön") ? "ok   CanRepair" : "FAIL CanRepair");
void Stretch(string name, bool ok)
{
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} stretch: {name}");
}
Stretch("collapse to three", ChatLine.Collapse("so SLEEEEEE and sooooo") == "so SLEEE and sooo");
Stretch("three letters untouched", ChatLine.Stretches("yesss").Count == 0);
string? Back(string repaired, string typed) => ChatLine.RestoreStretches(repaired, ChatLine.Stretches(typed), ChatLine.Collapse(typed));
Stretch("restores in order", Back("i was sooo tired SLEEE", "wa ssoooooo SLEEEEEEEE") == "i was soooooo tired SLEEEEEEEE");
Stretch("lost stretch is refused", Back("i was soo tired", "soooooo") == null);
Stretch("wrong letter is refused", Back("mmm tired", "soooooo") == null);
Stretch("changed word is refused", Back("SEEE xD", "SLEEEEEE xD") == null);
Stretch("whole word split", ChatLine.SplitStretched("i SLEEEEE xD").SequenceEqual(new[] { "i ", "SLEEEEE", " xD" }));
var keepList = ChatLine.ParseKeepWords("Zyrielle, Limsa\nzyrielle  Ul'dah");
Stretch("keep list reads and removes repeats", keepList.SequenceEqual(new[] { "Zyrielle", "Limsa", "Ul'dah" }));
var keepPattern = ChatLine.KeepPattern(keepList)!;
Stretch("keep matches any case", keepPattern.Matches("saw zyrielle, in LIMSA").Select(m => m.Value).SequenceEqual(new[] { "zyrielle", "LIMSA" }));
Stretch("keep needs the whole word", !keepPattern.IsMatch("limsas zyriellea"));
Stretch("keep handles an apostrophe", keepPattern.IsMatch("off to ul'dah now"));
Stretch("empty keep list", ChatLine.KeepPattern(new List<string>()) == null);
var store = new Dictionary<string, LearnedWord>();
var mem = new CorrectionMemory(store);
mem.Learn("that dress is so lawd on you", "that dress is so lewd on you");
Stretch("one fix is only remembered", mem.Apply("so lawd lol") == "so lawd lol" && store["lawd"].Count == 1);
mem.Learn("that was lawd", "that was lewd");
Stretch("second fix puts it in use", mem.Apply("so lawd lol, Lawd!") == "so lewd lol, Lewd!");
Stretch("rewording is not learned", !mem.Learn("i went to the shop", "i walked to the market") && !store.ContainsKey("went"));
Stretch("whole new sentence is not learned", !mem.Learn("ok see you", "nah i will stay here a while longer") && store.Count == 1);
mem.Learn("he is so lewd", "he is so lawd");
Stretch("changing it back unlearns it", !store.ContainsKey("lawd") && mem.Apply("so lawd") == "so lawd");
mem.Learn("becouse i said", "because i said"); mem.Learn("becouse no", "because no");
Stretch("two fixes in one message", mem.Learn("eather way and witch one", "either way and which one") && store["eather"].Count == 1 && store["witch"].Count == 1);
mem.Forget("becouse"); Stretch("forget one", mem.Apply("becouse") == "becouse");
mem.ForgetAll(); Stretch("forget all", store.Count == 0);
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

void Swear(string typed, string expected)
{
    var got = SwearWords.FixTypos(typed, ChatLine.RoleplayWords).Text;
    if (got == expected) Console.WriteLine($"ok   swear {typed}");
    else { failures++; Console.WriteLine($"FAIL swear {typed} -> {got}, expected {expected}"); }
}
Swear("that bastrad pulled again", "that bastard pulled again");
Swear("stop being an ashole", "stop being an asshole");
Swear("Bastrad", "Bastard");
Swear("BASTRAD", "BASTARD");
Swear("arshcloch", "arschloch");
Swear("scheise", "scheisse");
Swear("a witch and a ditch", "a witch and a ditch");
Swear("hello Bastrad", "hello Bastrad");
Swear("fuck shit bastard", "fuck shit bastard");
Swear("i want to go home", "i want to go home");
Swear("the counter is pitched", "the counter is pitched");

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
    var repair = repairer.RunKeeping(message, ChatLine.RoleplayWords);
    times.Add(timer.ElapsedMilliseconds);
    Console.WriteLine($"OUT\t{repair.Text}\t{string.Join(" | ", repair.UnsureWords)}");
}
foreach (var line in new[] { "i sa wZyrielle a tlimsa yesterda yan dwe talked", "tel lQuorvathe i wil lbe lat etonight" })
{
    Console.WriteLine($"PLAIN\t{repairer.RunKeepingStretches(line).Text}");
    Console.WriteLine($"KEEP\t{repairer.RunKeeping(line, new[] { "wZyrielle", "lQuorvathe" }).Text}");
}
var altered = "tell Tataroo i wil lbe lat etonight yo uknow";       // the model alone changes this made-up name
Console.WriteLine($"PLAIN\t{repairer.RunKeepingStretches(altered).Text}");
var keptRun = repairer.RunKeeping(altered, new[] { "Tataroo" });
Console.WriteLine($"KEEP\t{keptRun.Text}");
if (!keptRun.Text.Contains("Tataroo") || !keptRun.Text.Contains("tonight")) { failures++; Console.WriteLine("FAIL never-change word altered"); }
var spaced = repairer.RunKeeping("i wil lbe lat etonight", new[] { "lat" }).Text;
if (spaced.Contains("  ")) { failures++; Console.WriteLine($"FAIL double space: {spaced}"); }
var words = File.ReadAllLines(Path.Combine(directory, "model", "words.txt")).ToHashSet(StringComparer.OrdinalIgnoreCase);
void Suggest(string typedLine, string word, string expectPick, string? expectTyped)
{
    var shown = repairer.RunKeeping(typedLine, ChatLine.RoleplayWords).Text;
    var start = shown.IndexOf(word, StringComparison.Ordinal);
    if (start < 0) { failures++; Console.WriteLine($"FAIL suggest: '{word}' not in '{shown}'"); return; }
    var result = Suggestions.For(repairer, typedLine, shown, start, word, words, Array.Empty<string>());
    var ok = (expectPick.Length == 0 || result.Picks.Contains(expectPick)) && result.Typed == expectTyped && !result.Picks.Contains("bitch");
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} suggest {word}: [{string.Join(" | ", result.Picks)}] typed {result.Typed ?? "-"}");
}
Suggest("i dont no what your talking about", "now", "know", "no");
Suggest("thanks cfor sftaying up with mr lasy nitht", "last", "lady", "lasy");   // the web page also offers "lazy"; .NET and WebAssembly round a little differently
Suggest("Whats the wifi pasword?", "wife", "", "wifi");
Console.WriteLine($"final failures: {failures}");
times.Sort();
Console.WriteLine($"threads {threads}: median {times[times.Count / 2]} ms, worst {times[^1]} ms");
Console.WriteLine($"private memory {Process.GetCurrentProcess().PrivateMemorySize64 / 1_000_000} MB");
