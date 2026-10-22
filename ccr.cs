// CCR (Chrome Caption Recorder) - 크롬/엣지 "실시간 자막" 말풍선 텍스트를 UI Automation으로 읽어 txt에 누적 저장
// 빌드: build.bat (Windows 기본 .NET Framework 4.x csc 사용, 별도 설치 불필요)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

[assembly: AssemblyTitle("CCR - Chrome Caption Recorder")]
[assembly: AssemblyProduct("CCR - Chrome Caption Recorder")]
[assembly: AssemblyDescription("크롬 실시간 자막 누적 저장기")]
[assembly: AssemblyCompany("kinphw")]
[assembly: AssemblyCopyright("kinphw")]
[assembly: AssemblyVersion("0.0.5.0")]
[assembly: AssemblyFileVersion("0.0.5.0")]
[assembly: AssemblyInformationalVersion("0.0.5")]

static class AppInfo
{
    public const string Name = "CCR";
    public const string LongName = "Chrome Caption Recorder";
    public const string Version = "0.0.5";
    public const string Author = "kinphw with CLAUDE";

    // 정보 창·ccr.exe /? 에 쓰는 항목 (이름, 값)
    public static readonly string[,] AboutRows = {
        { Name, LongName },
        { "버전", Version },
        { "개발자", Author } };

    public static string AboutText()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < AboutRows.GetLength(0); i++) sb.Append(AboutRows[i, 0]).Append(" : ").Append(AboutRows[i, 1]).Append("\r\n");
        return sb.ToString().TrimEnd();
    }
}

// ? 버튼·F1로 여는 정보 창
class AboutForm : Form
{
    public AboutForm()
    {
        Func<float, int> S = Theme.S;
        Text = AppInfo.Name + " 정보";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Card;
        Font = new Font(Theme.FontName, 9.5f);
        ClientSize = new Size(S(340), S(212));
        KeyPreview = true;
        KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter) Close(); };

        var logo = new PictureBox { Image = Theme.DrawLogo(S(44)), Size = new Size(S(44), S(44)), Location = new Point(S(24), S(22)) };
        var name = new Label { Text = AppInfo.Name, Font = new Font(Theme.FontName, 16f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true, Location = new Point(S(78), S(18)) };
        var sub = new Label { Text = "크롬 자막 기록기", Font = new Font(Theme.FontName, 9f), ForeColor = Theme.Sub, AutoSize = true, Location = new Point(S(80), S(48)) };
        var line = new Panel { BackColor = Theme.Border, Location = new Point(S(24), S(82)), Size = new Size(S(292), 1) };
        Controls.AddRange(new Control[] { logo, name, sub, line });

        var keyFont = new Font(Theme.FontName, 9.5f);
        var valFont = new Font(Theme.FontName, 9.5f, FontStyle.Bold);
        for (int i = 0; i < AppInfo.AboutRows.GetLength(0); i++)
        {
            int y = S(94 + i * 26);
            Controls.Add(new Label { Text = AppInfo.AboutRows[i, 0], Font = keyFont, ForeColor = Theme.Sub, AutoSize = false, Size = new Size(S(52), S(22)), Location = new Point(S(24), y), TextAlign = ContentAlignment.MiddleLeft });
            Controls.Add(new Label { Text = ":", Font = keyFont, ForeColor = Theme.Faint, AutoSize = false, Size = new Size(S(12), S(22)), Location = new Point(S(78), y), TextAlign = ContentAlignment.MiddleLeft });
            Controls.Add(new Label { Text = AppInfo.AboutRows[i, 1], Font = valFont, ForeColor = Theme.Text, AutoSize = true, Location = new Point(S(94), y + S(2)) });
        }

        var ok = new RoundButton { Text = "확인", Size = new Size(S(84), S(32)), Location = new Point(S(232), S(168)), Radius = 8,
                                   BaseColor = Theme.Dark, HoverColor = Theme.DarkHover, ForeColor2 = Color.White, Font = new Font(Theme.FontName, 9.5f, FontStyle.Bold) };
        ok.Click += delegate { Close(); };
        Controls.Add(ok);
    }
}

static class Program
{
    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int pid);

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--selftest") { SelfTest.Run(args[1]); return; }
        if (args.Length == 2 && args[0] == "--make-icon") { SelfTest.MakeIcon(args[1]); return; }
        if (args.Length >= 1 && Regex.IsMatch(args[0], @"^([/-]\?|\?|[/-]h|--help|[/-]v|--version|--about)$", RegexOptions.IgnoreCase))
        {
            if (AttachConsole(-1) && args[0] != "--about") { Console.WriteLine(); Console.WriteLine(AppInfo.AboutText()); }
            else
            {
                Application.EnableVisualStyles();
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.K = g.DpiX / 96f;
                Application.Run(new AboutForm { StartPosition = FormStartPosition.CenterScreen });
            }
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var form = new MainForm();
        if (args.Length >= 1 && args[0] == "--autostart") form.Shown += delegate { form.AutoStart(); };   // 실행하자마자 기록 시작
        // 기록 txt를 ccr.exe에 끌어다 놓으면 그 파일에 바로 이어서 기록
        if (args.Length == 1 && args[0].EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && File.Exists(args[0]))
        {
            string f = Path.GetFullPath(args[0]);
            form.Shown += delegate { form.AutoStart(f); };
        }
        Application.Run(form);
    }
}

// ---------------------------------------------------------------- 자막 말풍선 읽기
class CaptionReader
{
    static readonly Regex TitleRx = new Regex(@"^(실시간 자막|live caption)", RegexOptions.IgnoreCase);
    static readonly Regex BrowserRx = new Regex(@" - (Google Chrome|Chrome|Microsoft.? Edge|Whale)$");
    static readonly string[] BrowserProcs = { "chrome", "msedge", "whale" };

    AutomationElement bubble;

    static HashSet<int> BrowserPids()
    {
        var set = new HashSet<int>();
        foreach (var name in BrowserProcs)
            foreach (var p in Process.GetProcessesByName(name)) set.Add(p.Id);
        return set;
    }

    static List<AutomationElement> BrowserTopWindows()
    {
        var res = new List<AutomationElement>();
        var pids = BrowserPids();
        if (pids.Count == 0) return res;
        foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
        {
            try
            {
                var c = w.Current;
                if (pids.Contains(c.ProcessId) && c.ClassName.StartsWith("Chrome_WidgetWin")) res.Add(w);
            }
            catch { }
        }
        return res;
    }

    // 요소에서 읽을 수 있는 텍스트(Name / Value / TextPattern) 중 가장 긴 것
    static string ElementText(AutomationElement e)
    {
        string best = e.Current.Name ?? "";
        object pat;
        try
        {
            if (e.TryGetCurrentPattern(ValuePattern.Pattern, out pat))
            {
                string v = ((ValuePattern)pat).Current.Value ?? "";
                if (v.Length > best.Length) best = v;
            }
        }
        catch { }
        try
        {
            if (e.TryGetCurrentPattern(TextPattern.Pattern, out pat))
            {
                string v = ((TextPattern)pat).DocumentRange.GetText(-1) ?? "";
                if (v.Length > best.Length) best = v;
            }
        }
        catch { }
        return best;
    }

    // 자막 말풍선이면 본문(가장 긴 텍스트, 제목·버튼 제외), 아니면 null
    static string ReadBubble(AutomationElement w)
    {
        bool found = TitleRx.IsMatch(w.Current.Name ?? "");
        string best = "";
        foreach (AutomationElement e in w.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            try
            {
                var c = e.Current;
                if (TitleRx.IsMatch(c.Name ?? "")) { found = true; continue; }
                if (c.ControlType == ControlType.Button || c.ControlType == ControlType.MenuItem) continue;
                string t = ElementText(e);
                if (TitleRx.IsMatch(t)) continue;
                if (t.Length > best.Length) best = t;
            }
            catch { }
        }
        return found ? best : null;
    }

    public string Read()
    {
        if (bubble != null)
        {
            try { string t = ReadBubble(bubble); if (t != null) return t; } catch { }
            bubble = null;
        }
        var tops = BrowserTopWindows();
        foreach (var w in tops)
        {
            try
            {
                // 일반 브라우저 창은 건너뜀 (웹페이지 전체를 훑으면 느려짐)
                if (BrowserRx.IsMatch(w.Current.Name ?? "")) continue;
                string t = ReadBubble(w);
                if (t != null) { bubble = w; return t; }
            }
            catch { }
        }
        // 예비: 브라우저 창의 직계 자식 중 자막 말풍선
        foreach (var w in tops)
        {
            try
            {
                foreach (AutomationElement ch in w.FindAll(TreeScope.Children, Condition.TrueCondition))
                {
                    if (!TitleRx.IsMatch(ch.Current.Name ?? "")) continue;
                    string t = ReadBubble(ch);
                    if (t != null) { bubble = ch; return t; }
                }
            }
            catch { }
        }
        return null;
    }

    public static string Diagnose()
    {
        var sb = new StringBuilder();
        sb.AppendLine(AppInfo.Name + " v" + AppInfo.Version + " 진단 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("OS: " + Environment.OSVersion);
        foreach (var w in BrowserTopWindows())
        {
            try
            {
                var c = w.Current;
                sb.AppendLine("== TOP name=[" + c.Name + "] cls=" + c.ClassName + " rect=" + c.BoundingRectangle);
                bool browser = BrowserRx.IsMatch(c.Name ?? "");
                var scope = browser ? TreeScope.Children : TreeScope.Descendants;
                foreach (AutomationElement e in w.FindAll(scope, Condition.TrueCondition))
                {
                    var d = e.Current;
                    string t = ElementText(e);
                    sb.AppendLine("   " + d.ControlType.ProgrammaticName + " | id=" + d.AutomationId + " | cls=" + d.ClassName +
                                  " | name=[" + d.Name + "]" + (t != d.Name ? " | text=[" + t + "]" : ""));
                }
            }
            catch (Exception ex) { sb.AppendLine("   (오류) " + ex.Message); }
        }
        return sb.ToString();
    }
}

// ---------------------------------------------------------------- 누적 저장
// 말풍선은 최근 몇 줄만 보여주며 계속 밀려 올라가므로, 이미 저장한 끝부분을 새 텍스트에서 찾아
// 그 뒤만 이어 붙인다. 끝의 몇 글자는 인식이 고쳐질 수 있어 보류했다가 잠잠해지면 저장.
class Accumulator
{
    const int Hold = 12;
    static readonly int[] TailLens = { 40, 25, 15, 8 };

    readonly Writer w;
    string saved = "";
    string lastText = null;
    bool flushed = true;

    public Accumulator(Writer w) { this.w = w; }

    // 이어 쓰기: 파일 끝부분을 "이미 저장한 것"으로 두면 말풍선에 남은 부분과 자연히 이어짐
    public void Resume(string tail) { saved = tail.Length > 2000 ? tail.Substring(tail.Length - 2000) : tail; }

    public static string Normalize(string s) { return Regex.Replace(s ?? "", @"\s+", " ").Trim(); }

    // 새 텍스트에서 아직 저장 안 된 부분, 이어지는 지점이 없으면 null
    string NewPart(string t)
    {
        if (saved.Length == 0) return t;
        foreach (int len in TailLens)
        {
            if (saved.Length < len) continue;
            string tail = saved.Substring(saved.Length - len);
            int i = t.LastIndexOf(tail, StringComparison.Ordinal);
            if (i >= 0) return t.Substring(i + len);
        }
        return null;
    }

    void Commit(string t, bool all)
    {
        string np = NewPart(t);
        if (np == null) np = "\n" + t;   // 자막이 새로 시작됨
        if (!all)
        {
            if (np.Length <= Hold) return;
            int cut = np.Length - Hold;
            int sp = np.LastIndexOf(' ', cut);   // 단어 중간에서 자르지 않도록
            if (sp > 0) cut = sp + 1;
            np = np.Substring(0, cut);
        }
        Append(np);
    }

    public void Feed(string raw)
    {
        string t = Normalize(raw);
        if (t.Length == 0 || t == lastText) return;
        // 이전 자막과 이어지지 않으면(새 영상 등) 이전 보류분부터 모두 저장
        if (lastText != null && !flushed && NewPart(t) == null) Commit(lastText, true);
        Commit(t, false);
        lastText = t;
        flushed = false;
    }

    public void Flush()
    {
        if (lastText != null && !flushed) { Commit(lastText, true); flushed = true; }
    }

    void Append(string chunk)
    {
        if (chunk.Length == 0) return;
        saved += chunk;
        if (saved.Length > 2000) saved = saved.Substring(saved.Length - 2000);
        w.Write(chunk);
    }
}

// ---------------------------------------------------------------- 파일 쓰기 (두 방식 공용)
class Writer
{
    static readonly Encoding Utf8 = new UTF8Encoding(true);
    readonly string path;
    bool atLineStart = true;
    bool any;
    public event Action<string> Written;

    public Writer(string path)
    {
        this.path = path;
        // 기존 파일에 이어 쓰면 끝이 줄바꿈인지 보고 이어감
        try
        {
            var fi = new FileInfo(path);
            if (fi.Exists && fi.Length > 0)
            {
                any = true;
                using (var fs = File.OpenRead(path)) { fs.Seek(-1, SeekOrigin.End); atLineStart = fs.ReadByte() == '\n'; }
            }
        }
        catch { }
    }

    // 이어 쓰기용: 파일 끝부분(시각 표시 빼고 공백 정리)
    public static string ReadTail(string path, int chars)
    {
        try
        {
            string all = File.ReadAllText(path, Encoding.UTF8);
            if (all.Length > chars) all = all.Substring(all.Length - chars);
            return Squash(all);
        }
        catch { return ""; }
    }

    // 파일의 마지막 자막 줄 (빈 줄·회의명·구분선 제외)
    public static string LastTextLine(string path)
    {
        try
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = lines.Length - 1; i >= 0 && i >= lines.Length - 50; i--)
            {
                string l = lines[i].Trim();
                if (l.Length == 0 || l.StartsWith("■ ") || l.StartsWith("──")) continue;
                return l;
            }
        }
        catch { }
        return "";
    }

    public static string Squash(string s) { return Regex.Replace(Regex.Replace(s ?? "", @"\[\d{2}:\d{2}:\d{2}\] ", ""), @"\s+", " ").Trim(); }

    // 문장 끝(. ? !)에서 줄바꿈, 줄 머리에 시각 표시. '\n'은 강제 줄바꿈
    public void Write(string chunk)
    {
        if (string.IsNullOrEmpty(chunk)) return;
        string pretty = Regex.Replace(chunk, @"([.?!。？！])\s+", "$1\n");
        string[] parts = pretty.Split('\n');
        var sb = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            if (atLineStart) p = p.TrimStart();
            if (p.Length > 0)
            {
                if (atLineStart) sb.Append("[" + DateTime.Now.ToString("HH:mm:ss") + "] ");
                sb.Append(p);
                atLineStart = false;
            }
            if (i < parts.Length - 1 && !atLineStart) { sb.Append("\r\n"); atLineStart = true; }
        }
        Emit(sb.ToString());
    }

    // 문단 나눔 (발언자 바뀜 등): 줄을 끝내고 빈 줄 하나
    // 줄만 끝냄 (이미 줄 머리면 아무것도 안 함)
    public void LineBreak()
    {
        if (atLineStart) return;
        Emit("\r\n");
        atLineStart = true;
    }

    public void Paragraph()
    {
        if (!any) return;
        Emit((atLineStart ? "" : "\r\n") + "\r\n");
        atLineStart = true;
    }

    // 시각 없이 그대로 한 줄 (회의명 등)
    public void Note(string line)
    {
        Emit((atLineStart ? "" : "\r\n") + line + "\r\n");
        atLineStart = true;
    }

    void Emit(string s)
    {
        if (s.Length == 0) return;
        any = true;
        // 매번 열고 닫으므로 기록 중에도 다른 프로그램에서 파일을 열어볼 수 있음
        File.AppendAllText(path, s, Utf8);
        var h = Written;
        if (h != null) h(s);
    }
}

// ---------------------------------------------------------------- 다른 프로그램 연동용 안내 파일
// %LOCALAPPDATA%\CCR\current.json 에 지금 기록 중인 파일 경로를 남긴다. 다른 도구(Claude 세션 등)는
// 이 파일만 보면 현재 기록 파일을 찾아 끝에서부터 이어 읽을 수 있다. 기록 파일은 추가만 되고 고쳐 쓰지 않는다.
static class Pointer
{
    public static readonly string PathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCR", "current.json");

    public static void Write(string file, string source, string meeting, bool recording)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName));
            var d = new Dictionary<string, object> {
                { "app", AppInfo.Name + " " + AppInfo.Version },
                { "file", file }, { "recording", recording }, { "source", source ?? "" }, { "meeting", meeting ?? "" },
                { "updated", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") },
                { "format", "UTF-8 txt, append-only. '[HH:mm:ss] 문장' 줄, 빈 줄 = 발언자 바뀜, '■ ' 줄 = 회의명" } };
            File.WriteAllText(PathName, new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(d), new UTF8Encoding(false));
        }
        catch { }
    }
}

// ---------------------------------------------------------------- 국회 의사중계 AI 자막 (크롬 디버깅 포트로 페이지 직접 읽기)
// 사이트 자막 칸(#viewSubtit .incont)에는 최근 ~7줄만 남으므로 계속 옮겨 담는다.
//   <p class="smi_word stxt{N}"><span id="segarr_{N}_{k}" class="mhwa0|mhwa1?" style="color:…">토막</span>…</p>
//   글자색: #237c93 발언자1, #1e1e1e 발언자2, #0c2b80 아직 인식 중. mhwa0/1 = 새 발언자 시작
// (구조 출처: Assembly Webcast Subtitle Saver 5.0.0 capture.js 및 사이트 openos_util.js)
class WebUnit { public string Id, Text, Color; public bool Marked; }

class WebcastResult { public string Origin, Meeting; public List<WebUnit> Units = new List<WebUnit>(); }

class WebcastSource : IDisposable
{
    public const int Port = 9223;
    const string Expr = @"(function(){var r={o:String(performance.timeOrigin),n:'',u:[]};
var x=document.getElementById('xsubj');if(x)r.n=x.innerText.trim();
var vs=document.getElementById('viewSubtit');if(!vs)return JSON.stringify(r);
if(!window.__ccrOn){window.__ccrOn=1;if(getComputedStyle(vs).display==='none'){var b=document.querySelector('.player_ctrl .btn_subtit');if(b)b.click();}}
var ps=document.querySelectorAll('#viewSubtit .incont .smi_word');
for(var i=0;i<ps.length;i++){var p=ps[i],ss=p.querySelectorAll('span[id^=""segarr_""]');
 if(ss.length){for(var j=0;j<ss.length;j++){var s=ss[j];r.u.push({i:s.id,t:s.textContent.trim(),c:s.style.color,m:/\bmhwa[01]\b/.test(s.className)?1:0});}}
 else{var seg='';for(var k=0;k<p.classList.length;k++)if(p.classList[k].indexOf('stxt')===0)seg=p.classList[k];
  var sp=p.querySelector('span');r.u.push({i:seg||('c_'+p.textContent.trim()),t:p.textContent.trim(),c:sp?sp.style.color:'',m:0});}}
return JSON.stringify(r);})()";

    static readonly System.Web.Script.Serialization.JavaScriptSerializer Json =
        new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };

    System.Net.WebSockets.ClientWebSocket ws;
    string wsUrl;
    int nextId = 1;
    DateTime nextTry = DateTime.MinValue;

    public bool Connected { get { return ws != null; } }

    // 플레이어 페이지가 열려 있으면 자막 토막들, 아니면 null
    public WebcastResult Poll()
    {
        if (ws == null)
        {
            if (DateTime.Now < nextTry) return null;
            nextTry = DateTime.Now.AddSeconds(2);
            string url = FindPlayer();
            if (url == null) return null;
            try { Connect(url); } catch { Close(); return null; }
        }
        try
        {
            string s = Eval(Expr) as string;
            if (s == null) { Close(); return null; }
            var d = (Dictionary<string, object>)Json.DeserializeObject(s);
            var r = new WebcastResult { Origin = (string)d["o"], Meeting = (string)d["n"] };
            foreach (Dictionary<string, object> u in (object[])d["u"])
                r.Units.Add(new WebUnit { Id = (string)u["i"], Text = (string)u["t"], Color = (string)u["c"], Marked = Convert.ToInt32(u["m"]) == 1 });
            return r;
        }
        catch { Close(); return null; }
    }

    static string HttpGet(string url)
    {
        var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
        req.Timeout = 800; req.ReadWriteTimeout = 800; req.Proxy = null;
        using (var resp = req.GetResponse())
        using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
    }

    // 시험용으로 환경변수 CCR_URL_MATCH로 바꿀 수 있음 (기본: 플레이어 페이지)
    static readonly string UrlMatch = Environment.GetEnvironmentVariable("CCR_URL_MATCH") ?? "player";

    static string FindPlayer()
    {
        try
        {
            foreach (Dictionary<string, object> t in (object[])Json.DeserializeObject(HttpGet("http://127.0.0.1:" + Port + "/json/list")))
            {
                string type = t.ContainsKey("type") ? t["type"] as string : "";
                string url = t.ContainsKey("url") ? t["url"] as string : "";
                if (type == "page" && url.Contains("assembly.webcast.go.kr") && url.Contains(UrlMatch) && t.ContainsKey("webSocketDebuggerUrl"))
                    return (string)t["webSocketDebuggerUrl"];
            }
        }
        catch { }
        return null;
    }

    void Connect(string url)
    {
        ws = new System.Net.WebSockets.ClientWebSocket();
        using (var cts = new CancellationTokenSource(3000)) ws.ConnectAsync(new Uri(url), cts.Token).Wait();
        wsUrl = url;
    }

    object Eval(string expr)
    {
        int id = nextId++;
        var msg = new Dictionary<string, object> {
            { "id", id }, { "method", "Runtime.evaluate" },
            { "params", new Dictionary<string, object> { { "expression", expr }, { "returnByValue", true } } } };
        var bytes = Encoding.UTF8.GetBytes(Json.Serialize(msg));
        using (var cts = new CancellationTokenSource(4000))
        {
            ws.SendAsync(new ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, cts.Token).Wait();
            var buf = new byte[65536];
            while (true)
            {
                var ms = new MemoryStream();
                System.Net.WebSockets.WebSocketReceiveResult rr;
                do
                {
                    var t = ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token);
                    t.Wait();
                    rr = t.Result;
                    if (rr.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) throw new IOException("closed");
                    ms.Write(buf, 0, rr.Count);
                } while (!rr.EndOfMessage);
                var resp = Json.DeserializeObject(Encoding.UTF8.GetString(ms.ToArray())) as Dictionary<string, object>;
                if (resp == null || !resp.ContainsKey("id") || Convert.ToInt32(resp["id"]) != id) continue;
                var res = resp.ContainsKey("result") ? resp["result"] as Dictionary<string, object> : null;
                var inner = res != null && res.ContainsKey("result") ? res["result"] as Dictionary<string, object> : null;
                return inner != null && inner.ContainsKey("value") ? inner["value"] : null;
            }
        }
    }

    void Close()
    {
        try { if (ws != null) ws.Dispose(); } catch { }
        ws = null;
    }

    public void Dispose() { Close(); }

    public static string Status()
    {
        try
        {
            string list = HttpGet("http://127.0.0.1:" + Port + "/json/list");
            return "디버깅 포트 " + Port + " 응답함, 플레이어 페이지 " + (FindPlayer() != null ? "있음" : "없음") + "\r\n" + list;
        }
        catch (Exception ex) { return "디버깅 포트 " + Port + " 응답 없음 (" + ex.Message + ")"; }
    }

    // CCR 전용 크롬 창(디버깅 포트 + 별도 프로필)으로 의사중계 열기
    public static bool OpenBrowser(out string error)
    {
        error = null;
        string exe = FindBrowserExe();
        if (exe == null) { error = "크롬(또는 엣지)을 찾지 못했습니다."; return false; }
        string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCR", "browser-profile");
        Directory.CreateDirectory(profile);
        Process.Start(exe, "--remote-debugging-port=" + Port + " --user-data-dir=\"" + profile + "\" --no-first-run --no-default-browser-check " +
                           "--autoplay-policy=no-user-gesture-required --new-window https://assembly.webcast.go.kr/main/");
        return true;
    }

    static string FindBrowserExe()
    {
        foreach (var name in new[] { "chrome.exe", "msedge.exe" })
        {
            foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
            {
                try
                {
                    using (var k = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + name))
                    {
                        string p = k == null ? null : k.GetValue("") as string;
                        if (!string.IsNullOrEmpty(p) && File.Exists(p.Trim('"'))) return p.Trim('"');
                    }
                }
                catch { }
            }
        }
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var p in new[] {
            Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"), Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(local, @"Google\Chrome\Application\chrome.exe"), Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe") })
            if (File.Exists(p)) return p;
        return null;
    }
}

// 의사중계 자막 토막 → 파일. 토막은 확정(인식 중 색이 아니고 뒤에 새 토막이 옴, 또는 잠잠해짐)되면 순서대로 저장
class SegmentAccumulator
{
    const string Spk1 = "rgb(35, 124, 147)", Pending = "rgb(12, 43, 128)";
    class Unit { public string Id, Text, Spk; public bool Marked, Pending; public DateTime Changed; }

    readonly Writer w;
    readonly List<Unit> units = new List<Unit>();          // 아직 저장 안 된 토막 (말한 순서)
    readonly Dictionary<string, Unit> map = new Dictionary<string, Unit>();
    readonly HashSet<string> done = new HashSet<string>();  // 저장한 토막 id
    string lastSpk, lastSeg;
    string resumeTail = "";
    bool overlap;   // 이어 쓰기 직후: 사이트 자막 칸에 남은 줄 중 이미 파일에 있는 것은 건너뜀

    string resumeLast = "";   // 이어 쓰는 파일의 마지막 문장 줄 (끌 때 말하던 도중이던 토막일 수 있음)

    public void Resume(string tail, string lastLine)
    {
        resumeTail = tail ?? "";
        resumeLast = Writer.Squash(lastLine);
        overlap = resumeTail.Length > 0;
    }
    string meeting;
    public string Meeting { get { return meeting; } }

    public SegmentAccumulator(Writer w) { this.w = w; }

    public void Feed(WebcastResult r)
    {
        if (meeting == null && !string.IsNullOrEmpty(r.Meeting) && resumeTail.Contains("■ " + Writer.Squash(r.Meeting)))
            meeting = r.Meeting;   // 이어 쓰는 파일에 이미 같은 회의명이 있음
        if (!string.IsNullOrEmpty(r.Meeting) && r.Meeting != meeting)
        {
            meeting = r.Meeting;
            w.Note("■ " + meeting + "  (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " · 국회 의사중계 AI 자막)");
            lastSpk = null;
        }
        int prev = -1;
        foreach (var s in r.Units)
        {
            if (s.Text.Length == 0) continue;
            string id = r.Origin + "|" + s.Id;   // 새로고침하면 번호가 다시 시작하므로 페이지별로 구분
            if (done.Contains(id)) continue;
            bool pend = s.Color == Pending;
            string spk = pend ? "" : (s.Color == Spk1 ? "1" : "2");
            Unit u;
            if (!map.TryGetValue(id, out u))
            {
                u = new Unit { Id = id, Text = s.Text, Spk = spk, Marked = s.Marked, Pending = pend, Changed = DateTime.Now };
                // 이전 토막 바로 뒤에 끼워 넣음 (오래된 줄에 토막이 덧붙는 경우)
                int at = prev + 1 > 0 ? prev + 1 : units.Count;
                if (at > units.Count) at = units.Count;
                units.Insert(at, u);
                map[id] = u;
                prev = at;
            }
            else
            {
                if (u.Text != s.Text || u.Pending != pend || (spk != "" && u.Spk != spk) || (s.Marked && !u.Marked))
                {
                    u.Text = s.Text; u.Pending = pend; if (spk != "") u.Spk = spk; u.Marked |= s.Marked;
                    u.Changed = DateTime.Now;
                }
                prev = units.IndexOf(u);
            }
        }
        CommitReady(false);
    }

    public void Flush() { CommitReady(true); }

    void CommitReady(bool all)
    {
        var now = DateTime.Now;
        while (units.Count > 0)
        {
            var u = units[0];
            double idle = (now - u.Changed).TotalSeconds;
            bool ready = all || (!u.Pending && (units.Count > 1 || idle > 2)) || idle > 20;
            if (!ready) break;
            units.RemoveAt(0);
            map.Remove(u.Id);
            done.Add(u.Id);
            // AI 자막엔 마침표가 거의 없으므로 사이트 화면처럼 토막(stxt 번호)마다 줄을 바꾸고, 발언자가 바뀌면 빈 줄
            string seg = SegOf(u.Id);
            if (overlap)
            {
                string n = Writer.Squash(u.Text);
                if (n.Length > 0 && resumeTail.Contains(n))
                {
                    if (u.Spk != "") lastSpk = u.Spk;
                    lastSeg = seg;
                    continue;
                }
                overlap = false;   // 파일에 없는 첫 토막부터 저장
                // 끌 때 반쯤 저장된 토막이 그새 길어졌으면 이미 적힌 앞부분은 빼고 나머지만
                if (resumeLast.Length >= 4 && n.StartsWith(resumeLast) && n.Length > resumeLast.Length)
                    u.Text = "… " + n.Substring(resumeLast.Length).Trim();
            }
            if (u.Marked || (u.Spk != "" && lastSpk != null && u.Spk != lastSpk)) w.Paragraph();
            else if (seg != lastSeg) w.LineBreak();
            else u.Text = " " + u.Text;   // 같은 토막의 이어지는 조각
            if (u.Spk != "") lastSpk = u.Spk;
            lastSeg = seg;
            w.Write(u.Text);
        }
    }

    static readonly Regex SegRx = new Regex(@"\|(?:segarr_(\d+)_|stxt(\d+))");
    static string SegOf(string id)
    {
        var m = SegRx.Match(id);
        return m.Success ? id.Substring(0, id.IndexOf('|')) + "#" + m.Groups[1].Value + m.Groups[2].Value : id;
    }

    // 화면의 "실시간 인식"용: 아직 저장 안 된 토막들. 인식 중(잠정) 토막은 PendOn…PendOff로 감쌈
    public const char WebMark = '\u0001', PendOn = '\u0002', PendOff = '\u0003';
    public string LiveText()
    {
        if (units.Count == 0) return "";
        var sb = new StringBuilder();
        sb.Append(WebMark);
        foreach (var u in units)
        {
            if (sb.Length > 1) sb.Append(' ');
            if (u.Pending) sb.Append(PendOn).Append(u.Text).Append(PendOff); else sb.Append(u.Text);
        }
        return sb.ToString();
    }
}

// ---------------------------------------------------------------- 디자인 공통
static class Theme
{
    public static readonly Color Bg = Color.FromArgb(244, 245, 247);
    public static readonly Color Card = Color.White;
    public static readonly Color Border = Color.FromArgb(226, 229, 233);
    public static readonly Color Text = Color.FromArgb(31, 35, 40);
    public static readonly Color Sub = Color.FromArgb(107, 114, 128);
    public static readonly Color Faint = Color.FromArgb(156, 163, 175);
    public static readonly Color Red = Color.FromArgb(229, 72, 77);
    public static readonly Color RedHover = Color.FromArgb(214, 58, 64);
    public static readonly Color Dark = Color.FromArgb(55, 65, 81);
    public static readonly Color DarkHover = Color.FromArgb(31, 41, 55);
    public static readonly Color Green = Color.FromArgb(22, 163, 74);
    public static readonly Color Orange = Color.FromArgb(217, 119, 6);
    public static readonly Color Blue = Color.FromArgb(37, 99, 235);
    public static readonly Color Ghost = Color.FromArgb(243, 244, 246);
    public static readonly Color GhostHover = Color.FromArgb(229, 231, 235);
    public const string FontName = "Malgun Gothic";

    public static float K = 1f;   // DPI 배율
    public static int S(float v) { return (int)Math.Round(v * K); }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // 앱 아이콘: 빨간 둥근 사각형 + 흰 기록 점 + 자막 줄
    public static Bitmap DrawLogo(int size)
    {
        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float u = size / 32f;
            using (var path = Round(new RectangleF(1 * u, 1 * u, 30 * u, 30 * u), 8 * u))
            using (var br = new SolidBrush(Red)) g.FillPath(br, path);
            using (var w = new SolidBrush(Color.White))
            {
                g.FillEllipse(w, 7 * u, 8 * u, 8 * u, 8 * u);
                using (var p1 = Round(new RectangleF(18 * u, 10 * u, 8 * u, 4 * u), 2 * u)) g.FillPath(w, p1);
                using (var p2 = Round(new RectangleF(7 * u, 19 * u, 19 * u, 4 * u), 2 * u)) g.FillPath(w, p2);
            }
        }
        return bmp;
    }
}

// 둥근 버튼
class RoundButton : Control
{
    public Color BaseColor = Theme.Ghost, HoverColor = Theme.GhostHover, ForeColor2 = Theme.Text;
    public float Radius = 8;
    public Action<Graphics, RectangleF, Color> Glyph;
    bool hover, down;

    public RoundButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color c = hover ? HoverColor : BaseColor;
        if (down) c = ControlPaint.Dark(c, 0.05f);
        using (var path = Theme.Round(r, Theme.K * Radius))
        using (var br = new SolidBrush(c)) g.FillPath(br, path);

        var textRect = new Rectangle(0, 0, Width, Height);
        if (Glyph != null && Text.Length > 0)
        {
            Size ts = TextRenderer.MeasureText(Text, Font);
            float gs = Height * 0.32f, gap = Theme.K * 8;
            float total = gs + gap + ts.Width;
            float x0 = (Width - total) / 2f;
            Glyph(g, new RectangleF(x0, (Height - gs) / 2f, gs, gs), ForeColor2);
            textRect = new Rectangle((int)(x0 + gs + gap), 0, ts.Width + 2, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, ForeColor2, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
        else
        {
            TextRenderer.DrawText(g, Text, Font, textRect, ForeColor2, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }
    }
}

// 상태 표시 알약 (점 + 문구)
class StatusPill : Control
{
    public Color DotColor = Theme.Faint;
    public bool Dim;

    public StatusPill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public void Set(string text, Color dot) { Text = text; DotColor = dot; Dim = false; Invalidate(); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Size ts = TextRenderer.MeasureText(Text, Font);
        float dot = Theme.K * 8, padX = Theme.K * 12, gap = Theme.K * 8;
        float w = Math.Min(Width - 1, padX + dot + gap + ts.Width + padX);
        var r = new RectangleF(0.5f, 0.5f, w, Height - 1.5f);
        Color bgc = Color.FromArgb(28, DotColor);
        using (var path = Theme.Round(r, r.Height / 2))
        using (var br = new SolidBrush(bgc)) g.FillPath(br, path);
        using (var br = new SolidBrush(Dim ? Color.FromArgb(90, DotColor) : DotColor))
            g.FillEllipse(br, padX, (Height - dot) / 2f, dot, dot);
        TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(padX + dot + gap), 0, ts.Width + 4, Height),
                              ControlPaint.Dark(DotColor, 0.25f), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }
}

// 흰 카드 (둥근 모서리 + 테두리)
class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.Round(r, Theme.K * 10))
        {
            using (var br = new SolidBrush(Theme.Card)) g.FillPath(br, path);
            using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
        }
    }
}

// ---------------------------------------------------------------- 화면
class MainForm : Form
{
    static readonly Regex TsRx = new Regex(@"(\[\d{2}:\d{2}:\d{2}\] )");

    RoundButton btnRec, btnResume, btnHelp, btnWebcast, btnNotepad, btnFolder, btnDiag;
    StatusPill pill;
    Label lblStats, lblLiveTitle, lblLogTitle, lgPend, lgWait;
    RichTextBox liveBox;
    static readonly Color PendBack = Color.FromArgb(219, 234, 254);
    LinkLabel lnkFile;
    CheckBox chkTop, chkScroll;
    RichTextBox rtb;
    Font fText, fTs, fSmall, fBold;
    System.Windows.Forms.Timer tick;

    readonly string saveDir;
    Thread worker;
    volatile bool running;
    string currentFile;
    DateTime recStart;
    int lineCount, charCount;
    bool blink;

    public MainForm()
    {
        using (var g = CreateGraphics()) Theme.K = g.DpiX / 96f;
        S = Theme.S;

        Text = AppInfo.Name + " · 크롬 자막 기록기 v" + AppInfo.Version;
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font(Theme.FontName, 9.5f);
        BackColor = Theme.Bg;
        ClientSize = new Size(S(680), S(600));
        MinimumSize = new Size(S(520), S(420));
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        using (var logo = Theme.DrawLogo(S(32))) Icon = Icon.FromHandle(logo.GetHicon());

        fText = new Font(Theme.FontName, 11f);
        fTs = new Font("Consolas", 9.5f);
        fSmall = new Font(Theme.FontName, 8.5f);
        fBold = new Font(Theme.FontName, 13f, FontStyle.Bold);

        saveDir = PickSaveDir();
        BuildUi();

        tick = new System.Windows.Forms.Timer { Interval = 500 };
        tick.Tick += delegate { OnTick(); };
        tick.Start();

        KeyPress += delegate (object s, KeyPressEventArgs e) { if (e.KeyChar == '?' && !rtb.Focused) { e.Handled = true; ShowAbout(); } };
        KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.F1) { e.Handled = true; ShowAbout(); } };
        FormClosing += delegate { StopRec(); };
    }

    Func<float, int> S;

    void BuildUi()
    {
        // ---- 머리글
        var header = new Panel { Dock = DockStyle.Top, Height = S(64), BackColor = Theme.Card };
        header.Paint += delegate (object s, PaintEventArgs e) { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1); };
        var logo = new PictureBox { Image = Theme.DrawLogo(S(36)), Size = new Size(S(36), S(36)), Location = new Point(S(18), S(14)), BackColor = Color.Transparent };
        var title = new Label { Text = "크롬 자막 기록기", Font = fBold, ForeColor = Theme.Text, AutoSize = true, Location = new Point(S(62), S(11)), BackColor = Color.Transparent };
        var sub = new Label { Text = AppInfo.Name + " · " + AppInfo.LongName + " · v" + AppInfo.Version, Font = fSmall, ForeColor = Theme.Sub, AutoSize = true, Location = new Point(S(64), S(36)), BackColor = Color.Transparent };
        btnHelp = new RoundButton { Text = "?", Size = new Size(S(32), S(32)), Radius = 16, Font = new Font(Theme.FontName, 10.5f, FontStyle.Bold), ForeColor2 = Theme.Sub };
        btnHelp.Click += delegate { ShowAbout(); };
        btnWebcast = new RoundButton { Text = "국회 의사중계 열기", Height = S(32), Radius = 8, Font = new Font(Theme.FontName, 9f, FontStyle.Bold),
                                       BaseColor = Color.FromArgb(232, 240, 254), HoverColor = Color.FromArgb(214, 228, 253), ForeColor2 = Theme.Blue };
        btnWebcast.Width = TextRenderer.MeasureText(btnWebcast.Text, btnWebcast.Font).Width + S(28);
        btnWebcast.Click += delegate
        {
            string err;
            if (!WebcastSource.OpenBrowser(out err)) MessageBox.Show(this, err, AppInfo.Name);
        };
        header.Controls.AddRange(new Control[] { logo, title, sub, btnWebcast, btnHelp });
        header.Resize += delegate
        {
            btnHelp.Location = new Point(header.Width - S(18) - btnHelp.Width, (header.Height - btnHelp.Height) / 2);
            btnWebcast.Location = new Point(btnHelp.Left - S(10) - btnWebcast.Width, (header.Height - btnWebcast.Height) / 2);
        };

        // ---- 조작줄
        var bar = new Panel { Dock = DockStyle.Top, Height = S(76), BackColor = Theme.Bg };
        btnRec = new RoundButton { Text = "기록 시작", Size = new Size(S(150), S(46)), Location = new Point(S(18), S(16)), Radius = 10,
                                   BaseColor = Theme.Red, HoverColor = Theme.RedHover, ForeColor2 = Color.White, Font = new Font(Theme.FontName, 11f, FontStyle.Bold) };
        btnRec.Glyph = GlyphRec;
        btnRec.Click += delegate { if (running) StopRec(); else StartRec(); };
        btnResume = new RoundButton { Text = "이어서 기록…", Height = S(36), Location = new Point(S(176), S(21)), Radius = 8,
                                      Font = new Font(Theme.FontName, 9.5f, FontStyle.Bold), ForeColor2 = Theme.Dark,
                                      BaseColor = Color.White, HoverColor = Theme.GhostHover };
        btnResume.Width = TextRenderer.MeasureText(btnResume.Text, btnResume.Font).Width + S(26);
        btnResume.Click += delegate { if (!running) ResumeRec(); };
        pill = new StatusPill { Location = new Point(S(184), S(25)), Size = new Size(S(330), S(28)), Font = new Font(Theme.FontName, 9.5f, FontStyle.Bold) };
        pill.Set("대기 중", Theme.Faint);
        lblStats = new Label { Text = "", Font = new Font(Theme.FontName, 9.5f), ForeColor = Theme.Sub, AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Size = new Size(S(240), S(28)), BackColor = Color.Transparent };
        bar.Controls.AddRange(new Control[] { btnRec, btnResume, pill, lblStats });
        EventHandler relayout = delegate
        {
            lblStats.Location = new Point(bar.Width - S(18) - lblStats.Width, S(25));
            // 대기 중엔 [이어서 기록…] 버튼 뒤, 기록 중엔 버튼이 숨으므로 기록 버튼 바로 뒤
            pill.Left = btnResume.Visible ? btnResume.Right + S(10) : S(184);
            pill.Width = Math.Max(S(120), lblStats.Left - pill.Left - S(8));
        };
        bar.Resize += relayout;
        btnResume.VisibleChanged += relayout;

        // ---- 실시간 인식 카드
        var liveWrap = new Panel { Dock = DockStyle.Top, Height = S(92), Padding = new Padding(S(18), 0, S(18), S(10)), BackColor = Theme.Bg };
        var liveCard = new Card { Dock = DockStyle.Fill };
        lblLiveTitle = new Label { Text = "실시간 인식", Font = fSmall, ForeColor = Theme.Sub, AutoSize = true, Location = new Point(S(14), S(9)), BackColor = Theme.Card };
        liveBox = new RichTextBox { ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Card, ScrollBars = RichTextBoxScrollBars.None,
                                    Font = fText, Location = new Point(S(14), S(30)), Height = S(46), TabStop = false, DetectUrls = false, Cursor = Cursors.Default };
        // 범례: 인식 중(잠정) / 확정·저장 대기
        lgPend = new Label { Text = " 인식 중(잠정) ", Font = fSmall, ForeColor = Theme.Blue, BackColor = PendBack, AutoSize = true, Visible = false };
        lgWait = new Label { Text = "확정 · 저장 대기", Font = fSmall, ForeColor = Theme.Text, BackColor = Theme.Card, AutoSize = true, Visible = false };
        liveCard.Controls.AddRange(new Control[] { lblLiveTitle, liveBox, lgPend, lgWait });
        liveCard.Resize += delegate
        {
            liveBox.Width = liveCard.Width - S(28);
            lgWait.Location = new Point(liveCard.Width - S(14) - lgWait.Width, S(9));
            lgPend.Location = new Point(lgWait.Left - S(10) - lgPend.Width, S(9));
        };
        liveWrap.Controls.Add(liveCard);

        // ---- 저장된 기록 카드
        var logWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(18), 0, S(18), S(6)), BackColor = Theme.Bg };
        var logCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(S(14), S(44), S(6), S(10)) };
        lblLogTitle = new Label { Text = "저장된 기록 (파일에 쓰인 그대로)", Font = fSmall, ForeColor = Theme.Sub, AutoSize = true, Location = new Point(S(14), S(14)), BackColor = Theme.Card };
        btnNotepad = SmallButton("메모장으로 열기");
        btnNotepad.Click += delegate { OpenInNotepad(); };
        btnFolder = SmallButton("폴더 열기");
        btnFolder.Click += delegate { OpenFolder(); };
        btnDiag = SmallButton("진단 저장");
        btnDiag.Click += delegate { SaveDiag(true); };
        rtb = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text,
                                Font = fText, DetectUrls = false, HideSelection = false, ScrollBars = RichTextBoxScrollBars.Vertical };
        logCard.Controls.AddRange(new Control[] { rtb, lblLogTitle, btnNotepad, btnFolder, btnDiag });
        logCard.Resize += delegate
        {
            int x = logCard.Width - S(10);
            foreach (var b in new[] { btnDiag, btnFolder, btnNotepad }) { x -= b.Width; b.Location = new Point(x, S(9)); x -= S(6); }
        };
        logWrap.Controls.Add(logCard);

        // ---- 바닥글
        var footer = new Panel { Dock = DockStyle.Bottom, Height = S(40), BackColor = Theme.Bg };
        lnkFile = new LinkLabel { Text = "저장 위치: " + saveDir, Font = fSmall, AutoSize = false, AutoEllipsis = true, Location = new Point(S(20), S(10)), Height = S(20),
                                  LinkColor = Theme.Sub, ActiveLinkColor = Theme.Blue, LinkBehavior = LinkBehavior.HoverUnderline, BackColor = Theme.Bg };
        lnkFile.LinkClicked += delegate { OpenFolder(); };
        chkScroll = new CheckBox { Text = "자동 스크롤", Checked = true, AutoSize = true, Font = fSmall, ForeColor = Theme.Sub, BackColor = Theme.Bg };
        chkTop = new CheckBox { Text = "항상 위", Checked = true, AutoSize = true, Font = fSmall, ForeColor = Theme.Sub, BackColor = Theme.Bg };
        chkTop.CheckedChanged += delegate { TopMost = chkTop.Checked; };
        footer.Controls.AddRange(new Control[] { lnkFile, chkScroll, chkTop });
        footer.Resize += delegate
        {
            chkTop.Location = new Point(footer.Width - S(20) - chkTop.Width, S(10));
            chkScroll.Location = new Point(chkTop.Left - S(14) - chkScroll.Width, S(10));
            lnkFile.Width = Math.Max(S(50), chkScroll.Left - lnkFile.Left - S(10));
        };

        // 도킹은 나중에 추가한 것부터 배치되므로 Fill을 먼저, 맨 위 머리글을 마지막에 추가
        Controls.Add(logWrap);
        Controls.Add(footer);
        Controls.Add(liveWrap);
        Controls.Add(bar);
        Controls.Add(header);
    }

    RoundButton SmallButton(string text)
    {
        var b = new RoundButton { Text = text, Font = fSmall, Height = S(28), Radius = 6, ForeColor2 = Theme.Dark };
        b.Width = TextRenderer.MeasureText(text, fSmall).Width + S(22);
        return b;
    }

    static void GlyphRec(Graphics g, RectangleF r, Color c)
    {
        using (var br = new SolidBrush(c)) g.FillEllipse(br, r);
    }

    static void GlyphStop(Graphics g, RectangleF r, Color c)
    {
        using (var br = new SolidBrush(c))
        using (var p = Theme.Round(r, r.Width * 0.2f)) g.FillPath(br, p);
    }

    static string PickSaveDir()
    {
        string[] cands = {
            AppDomain.CurrentDomain.BaseDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CCR")
        };
        foreach (var d in cands)
        {
            try
            {
                Directory.CreateDirectory(d);
                string probe = Path.Combine(d, ".ccr_write_test");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return d.TrimEnd('\\');
            }
            catch { }
        }
        return Path.GetTempPath().TrimEnd('\\');
    }

    void ShowAbout()
    {
        using (var f = new AboutForm()) { f.TopMost = TopMost; f.ShowDialog(this); }
    }

    void Ui(MethodInvoker a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(a); } catch { }
    }

    void SetStatus(string s, Color c) { Ui(delegate { pill.Set(s, c); }); }

    void SetLive(string t)
    {
        Ui(delegate
        {
            liveBox.Clear();
            if (string.IsNullOrEmpty(t))
            {
                liveBox.SelectionColor = Theme.Faint;
                liveBox.AppendText(running ? "말소리가 나오면 여기 표시됩니다." : "기록을 시작하면 지금 인식 중인 문장이 여기 표시됩니다.");
                return;
            }
            // 의사중계: 인식 중(잠정)=하늘색 배경, 확정·저장 대기=진한 글씨 / 크롬 자막: 구분 정보가 없어 전체 파란 글씨
            bool web = t[0] == SegmentAccumulator.WebMark;
            if (web) t = t.Substring(1);
            // 카드 폭에 맞게 끝부분(최신)만: 표시 글자 수를 세어 앞을 자름
            int max = Math.Max(20, liveBox.Width * 2 / Math.Max(1, (int)(fText.Size * Theme.K)));
            int cut = 0, count = 0;
            for (int i = t.Length - 1; i >= 0; i--)
            {
                if (t[i] == SegmentAccumulator.PendOn || t[i] == SegmentAccumulator.PendOff) continue;
                if (++count > max) { cut = i + 1; break; }
            }
            bool pend = false;
            for (int i = 0; i < cut; i++)
            {
                if (t[i] == SegmentAccumulator.PendOn) pend = true;
                else if (t[i] == SegmentAccumulator.PendOff) pend = false;
            }
            var run = new StringBuilder(cut > 0 ? "…" : "");
            MethodInvoker flush = delegate
            {
                if (run.Length == 0) return;
                liveBox.SelectionStart = liveBox.TextLength;
                liveBox.SelectionColor = !web || pend ? Theme.Blue : Theme.Text;
                liveBox.SelectionBackColor = web && pend ? PendBack : Theme.Card;
                liveBox.AppendText(run.ToString());
                run.Length = 0;
            };
            for (int i = cut; i < t.Length; i++)
            {
                char ch = t[i];
                if (ch == SegmentAccumulator.PendOn || ch == SegmentAccumulator.PendOff)
                {
                    flush();
                    pend = ch == SegmentAccumulator.PendOn;
                    continue;
                }
                run.Append(ch);
            }
            flush();
            liveBox.SelectionStart = liveBox.TextLength;
            liveBox.ScrollToCaret();
        });
    }

    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref Point lp);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr lp);
    const int WM_SETREDRAW = 0x0B, EM_GETSCROLLPOS = 0x400 + 221, EM_SETSCROLLPOS = 0x400 + 222;

    void OnTick()
    {
        if (running)
        {
            blink = !blink;
            pill.Dim = blink && pill.DotColor == Theme.Green;
            pill.Invalidate();
            var el = DateTime.Now - recStart;
            lblStats.Text = string.Format("{0:00}:{1:00}:{2:00} · {3:N0}줄 · {4:N0}자", (int)el.TotalHours, el.Minutes, el.Seconds, lineCount, charCount);
        }
    }

    void AppendLog(string s)
    {
        foreach (Match m in TsRx.Matches(s)) lineCount++;
        charCount += TsRx.Replace(s, "").Replace("\r\n", "").Length;

        int selStart = rtb.SelectionStart, selLen = rtb.SelectionLength;
        bool follow = chkScroll.Checked;
        // 자동 스크롤을 끄면 읽던 위치를 그대로 두고 아래에만 덧붙임 (파일 저장은 항상 계속됨)
        var pos = new Point();
        if (!follow)
        {
            SendMessage(rtb.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref pos);
            SendMessage(rtb.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        }
        foreach (string piece in TsRx.Split(s))
        {
            if (piece.Length == 0) continue;
            bool ts = TsRx.IsMatch(piece);
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.SelectionColor = ts ? Theme.Faint : Theme.Text;
            rtb.SelectionFont = ts ? fTs : fText;
            rtb.AppendText(piece);
        }
        if (follow) { rtb.SelectionStart = rtb.TextLength; rtb.SelectionLength = 0; rtb.ScrollToCaret(); }
        else
        {
            rtb.SelectionStart = selStart; rtb.SelectionLength = selLen;
            SendMessage(rtb.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref pos);
            SendMessage(rtb.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
            rtb.Invalidate();
        }
    }

    // 이어서 기록: 기존 기록 파일을 골라 그 끝에 계속 씀 (가장 최근 파일이 기본 선택)
    void ResumeRec()
    {
        using (var dlg = new OpenFileDialog { Title = "이어서 기록할 파일 선택", Filter = "자막 기록 (*.txt)|*.txt|모든 파일 (*.*)|*.*", InitialDirectory = saveDir })
        {
            FileInfo latest = null;
            try
            {
                foreach (var f in new DirectoryInfo(saveDir).GetFiles("자막_*.txt"))
                    if (!f.Name.StartsWith("자막_진단") && (latest == null || f.LastWriteTime > latest.LastWriteTime)) latest = f;
            }
            catch { }
            if (latest != null) dlg.FileName = latest.Name;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            StartRec(dlg.FileName);
        }
    }

    void StartRec(string resumeFile = null)
    {
        bool resume = resumeFile != null;
        currentFile = resume ? resumeFile : Path.Combine(saveDir, "자막_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
        lnkFile.Text = (resume ? "이어서 저장 중: " : "저장 파일: ") + currentFile;
        rtb.Clear();
        lineCount = 0; charCount = 0;
        if (resume) ShowPrevious(currentFile);
        btnResume.Visible = false;
        recStart = DateTime.Now;
        running = true;
        btnRec.Text = "기록 중지";
        btnRec.Glyph = GlyphStop;
        btnRec.BaseColor = Theme.Dark; btnRec.HoverColor = Theme.DarkHover;
        btnRec.Invalidate();
        pill.Set("자막 말풍선 찾는 중...", Theme.Orange);
        SetLive("");
        OnTick();
        string file = currentFile;
        worker = new Thread(delegate () { Loop(file, resume); });
        worker.IsBackground = true;
        worker.Start();
    }

    // 이어 쓰는 파일의 마지막 부분을 흐리게 보여줌 (맥락 확인용, 통계에는 안 셈)
    void ShowPrevious(string file)
    {
        try
        {
            string all = File.ReadAllText(file, Encoding.UTF8);
            int from = Math.Max(0, all.Length - 3000);
            int nl = all.IndexOf('\n', from);
            string tail = (from > 0 && nl >= 0) ? all.Substring(nl + 1) : all.Substring(from);
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionColor = Theme.Faint;
            rtb.SelectionFont = fText;
            rtb.AppendText((from > 0 ? "…\r\n" : "") + tail.TrimEnd() + "\r\n");
        }
        catch { }
    }

    public void AutoStart(string resumeFile = null) { if (!running) StartRec(resumeFile); }

    void StopRec()
    {
        if (!running) return;
        running = false;
        if (worker != null) worker.Join(3000);
        btnRec.Text = "기록 시작";
        btnRec.Glyph = GlyphRec;
        btnRec.BaseColor = Theme.Red; btnRec.HoverColor = Theme.RedHover;
        btnRec.Invalidate();
        btnResume.Visible = true;
        pill.Set(File.Exists(currentFile) ? "중지됨 · 저장 완료" : "중지됨 · 저장된 내용 없음", Theme.Sub);
        SetLive("");
    }

    void OpenInNotepad()
    {
        if (currentFile != null && File.Exists(currentFile)) Process.Start("notepad.exe", "\"" + currentFile + "\"");
        else MessageBox.Show(this, "아직 저장된 파일이 없습니다. 기록을 시작하고 자막이 저장되면 열 수 있습니다.", AppInfo.Name);
    }

    void OpenFolder()
    {
        if (currentFile != null && File.Exists(currentFile)) Process.Start("explorer.exe", "/select,\"" + currentFile + "\"");
        else Process.Start("explorer.exe", "\"" + saveDir + "\"");
    }

    void SaveDiag(bool open)
    {
        string p = Path.Combine(saveDir, "자막_진단_" + DateTime.Now.ToString("HHmmss") + ".txt");
        try
        {
            File.WriteAllText(p, CaptionReader.Diagnose() + "\r\n== 의사중계 창\r\n" + WebcastSource.Status(), new UTF8Encoding(true));
            if (open) Process.Start("notepad.exe", "\"" + p + "\"");
        }
        catch (Exception ex) { if (open) MessageBox.Show(this, "진단 저장 실패: " + ex.Message, AppInfo.Name); }
    }

    void SetSourceTitle(string t)
    {
        Ui(delegate
        {
            lblLiveTitle.Text = "실시간 인식 (" + t + ")";
            lgPend.Visible = lgWait.Visible = t.Contains("의사중계");   // 잠정/확정 구분은 의사중계 자막에만 있음
        });
    }

    // 자막 출처는 매번 자동 선택: CCR 전용 창에 의사중계 플레이어가 열려 있으면 국회 AI 자막, 아니면 크롬 실시간 자막
    void Loop(string file, bool resume)
    {
        string tail = resume ? Writer.ReadTail(file, 6000) : "";
        var writer = new Writer(file);
        writer.Written += delegate (string s) { Ui(delegate { AppendLog(s); }); };
        if (resume)
        {
            writer.Paragraph();
            writer.Note("── 이어서 기록 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · " + AppInfo.Name + " " + AppInfo.Version + " ──");
        }
        var reader = new CaptionReader();
        var acc = new Accumulator(writer);
        var web = new WebcastSource();
        var seg = new SegmentAccumulator(writer);
        acc.Resume(tail);
        seg.Resume(tail, resume ? Writer.LastTextLine(file) : "");
        DateTime start = DateTime.Now, lastChange = DateTime.Now;
        string last = null, lastLive = null, pointerMeeting = null;
        Pointer.Write(file, "", null, true);
        bool diagDone = false;
        int state = -1;
        try
        {
            while (running)
            {
                WebcastResult wr = web.Poll();
                if (wr != null)
                {
                    if (state != 2)
                    {
                        acc.Flush();
                        SetStatus("기록 중 · 국회 의사중계 AI 자막", Theme.Green);
                        SetSourceTitle("국회 의사중계 AI 자막");
                        state = 2;
                    }
                    seg.Feed(wr);
                    string live = seg.LiveText();
                    if (live != lastLive) { SetLive(live); lastLive = live; }
                    if (seg.Meeting != pointerMeeting) { pointerMeeting = seg.Meeting; Pointer.Write(file, "webcast", pointerMeeting, true); }
                    Thread.Sleep(400);
                    continue;
                }
                if (state == 2) { seg.Flush(); lastLive = null; }

                string raw = null;
                try { raw = reader.Read(); } catch { }
                if (raw == null)
                {
                    if (state != 0)
                    {
                        SetStatus("자막 찾는 중... [국회 의사중계 열기] 창 또는 크롬 실시간 자막을 켜 주세요", Theme.Orange);
                        SetSourceTitle("자막 출처를 찾는 중");
                        SetLive(""); state = 0;
                    }
                    if (!diagDone && (DateTime.Now - start).TotalSeconds > 20) { diagDone = true; SaveDiag(false); }
                }
                else
                {
                    if (state != 1) { SetStatus("기록 중 · 크롬 실시간 자막", Theme.Green); SetSourceTitle("크롬 실시간 자막 말풍선"); state = 1; Pointer.Write(file, "chrome-live-caption", null, true); }
                    string t = Accumulator.Normalize(raw);
                    if (t.Length > 0 && t != last)
                    {
                        acc.Feed(t);
                        SetLive(t);
                        last = t;
                        lastChange = DateTime.Now;
                    }
                    else if ((DateTime.Now - lastChange).TotalSeconds > 3)
                    {
                        acc.Flush();   // 3초간 변화 없으면 보류분까지 저장
                    }
                }
                Thread.Sleep(400);
            }
        }
        catch (Exception ex) { SetStatus("오류: " + ex.Message, Theme.Red); }
        finally
        {
            try { acc.Flush(); } catch { }
            try { seg.Flush(); } catch { }
            web.Dispose();
            Pointer.Write(file, state == 2 ? "webcast" : state == 1 ? "chrome-live-caption" : "", pointerMeeting, false);
        }
    }

    // 화면 확인용: 가짜 자막으로 UI를 채움 (--demo)
    public void Demo()
    {
        recStart = DateTime.Now.AddMinutes(-12).AddSeconds(-34);
        running = true;
        btnRec.Text = "기록 중지"; btnRec.Glyph = GlyphStop; btnRec.BaseColor = Theme.Dark; btnRec.HoverColor = Theme.DarkHover;
        pill.Set("기록 중", Theme.Green);
        AppendLog("[14:02:11] 지금부터 종합감사를 시작하겠습니다.\r\n[14:02:15] 먼저 위원님들의 질의 순서에 따라 진행하겠습니다.\r\n[14:02:21] 첫 번째 질의는 김 위원님께서 해 주시겠습니다.\r\n");
        SetLive("장관님 지난 국정감사에서 지적된 사항에 대해 후속 조치가 어떻게 이루어졌는지");
        OnTick();
        running = false;
    }
}

// ---------------------------------------------------------------- 자체 시험 (--selftest 출력파일)
static class SelfTest
{
    // 빌드용: 로고를 여러 크기 PNG로 담은 .ico 생성
    public static void MakeIcon(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 256 };
        var pngs = new List<byte[]>();
        foreach (int s in sizes)
            using (var bmp = Theme.DrawLogo(s))
            using (var ms = new MemoryStream()) { bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); pngs.Add(ms.ToArray()); }
        using (var fs = File.Create(path))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
    }

    public static void Run(string outPath)
    {
        if (outPath == "--demo")
        {
            Application.EnableVisualStyles();
            var f = new MainForm();
            f.Shown += delegate { f.Demo(); };
            Application.Run(f);
            return;
        }
        if (outPath.StartsWith("web:"))   // web:초:출력파일 - 화면 없이 의사중계 창만 읽어 저장
        {
            string[] a = outPath.Split(new[] { ':' }, 3);
            if (File.Exists(a[2])) File.Delete(a[2]);
            var wr = new Writer(a[2]);
            var seg = new SegmentAccumulator(wr);
            using (var web = new WebcastSource())
            {
                DateTime end = DateTime.Now.AddSeconds(int.Parse(a[1]));
                int polls = 0, hits = 0;
                while (DateTime.Now < end)
                {
                    var r = web.Poll(); polls++;
                    if (r != null) { hits++; seg.Feed(r); }
                    Thread.Sleep(400);
                }
                seg.Flush();
                File.AppendAllText(a[2] + ".log", "polls=" + polls + " hits=" + hits + "\r\n");
            }
            return;
        }
        if (File.Exists(outPath)) File.Delete(outPath);
        var acc = new Accumulator(new Writer(outPath));
        string[] frames = {
            "안녕하세요",
            "안녕하세요 오늘은 회의",
            "안녕하세요 오늘은 회의를 시작하겠습니다.",
            "안녕하세요 오늘은 회의를 시작하겠습니다. 첫 번째 안건은 예산",
            "오늘은 회의를 시작하겠습니다. 첫 번째 안건은 예산 집행 현황입니다.",
            "시작하겠습니다. 첫 번째 안건은 예산 집행 현황입니다. 담당자께서 설명해 주시죠",
            "첫 번째 안건은 예산 집행 현황입니다. 담당자께서 설명해 주시죠. 네 장관님",
            "Next video starts here and continues",
            "Next video starts here and continues to the end."
        };
        foreach (var f in frames) acc.Feed(f);
        acc.Flush();
    }
}
