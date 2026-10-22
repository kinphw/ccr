// 크롬 실시간 자막 기록기 - Chrome/Edge "실시간 자막" 말풍선 텍스트를 UI Automation으로 읽어 txt에 누적 저장
// 빌드: build.bat (Windows 기본 .NET Framework 4.x csc 사용)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--selftest") { SelfTest.Run(args[1]); return; }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
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
        sb.AppendLine("진단 시각: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
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
    static readonly Encoding Utf8 = new UTF8Encoding(true);

    readonly string path;
    string saved = "";
    string lastText = null;
    bool flushed = true;
    bool atLineStart = true;
    public event Action<string> Written;

    public Accumulator(string path) { this.path = path; }

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

        // 문장 끝(. ? !)에서 줄바꿈, 줄 머리에 시각 표시
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
        string s = sb.ToString();
        if (s.Length == 0) return;
        File.AppendAllText(path, s, Utf8);
        var h = Written;
        if (h != null) h(s);
    }
}

// ---------------------------------------------------------------- 화면
class MainForm : Form
{
    readonly Button btnRec, btnFolder, btnDiag;
    readonly CheckBox chkTop;
    readonly Label lblStatus, lblFile;
    readonly TextBox txt;
    readonly string saveDir;

    Thread worker;
    volatile bool running;
    string currentFile;

    public MainForm()
    {
        Text = "크롬 자막 기록기";
        Font = new Font("맑은 고딕", 10f);
        ClientSize = new Size(560, 440);
        MinimumSize = new Size(420, 300);
        TopMost = true;

        saveDir = PickSaveDir();

        btnRec = new Button { Text = "● 기록 시작", Location = new Point(10, 10), Size = new Size(140, 40), BackColor = Color.FromArgb(220, 53, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnRec.FlatAppearance.BorderSize = 0;
        btnRec.Click += delegate { if (running) StopRec(); else StartRec(); };

        btnFolder = new Button { Text = "저장 폴더 열기", Location = new Point(160, 10), Size = new Size(120, 40) };
        btnFolder.Click += delegate { Process.Start("explorer.exe", currentFile != null && File.Exists(currentFile) ? "/select,\"" + currentFile + "\"" : "\"" + saveDir + "\""); };

        btnDiag = new Button { Text = "진단 저장", Location = new Point(290, 10), Size = new Size(100, 40) };
        btnDiag.Click += delegate { SaveDiag(true); };

        chkTop = new CheckBox { Text = "항상 위", Location = new Point(400, 18), AutoSize = true, Checked = true };
        chkTop.CheckedChanged += delegate { TopMost = chkTop.Checked; };

        lblStatus = new Label { Text = "대기 중 - 크롬 설정 > 접근성 > 실시간 자막을 켜고 [기록 시작]", Location = new Point(10, 58), AutoSize = false, Size = new Size(540, 22), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
        lblFile = new Label { Text = "저장 위치: " + saveDir, Location = new Point(10, 80), AutoSize = false, Size = new Size(540, 22), ForeColor = Color.DimGray, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };

        txt = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Location = new Point(10, 106), Size = new Size(540, 324), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom, BackColor = Color.White };

        Controls.AddRange(new Control[] { btnRec, btnFolder, btnDiag, chkTop, lblStatus, lblFile, txt });
        FormClosing += delegate { StopRec(); };
    }

    static string PickSaveDir()
    {
        string[] cands = {
            AppDomain.CurrentDomain.BaseDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "크롬자막기록")
        };
        foreach (var d in cands)
        {
            try
            {
                Directory.CreateDirectory(d);
                string probe = Path.Combine(d, ".write_test");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return d.TrimEnd('\\');
            }
            catch { }
        }
        return Path.GetTempPath();
    }

    void SetStatus(string s, Color c)
    {
        if (IsDisposed) return;
        BeginInvoke((MethodInvoker)delegate { lblStatus.Text = s; lblStatus.ForeColor = c; });
    }

    void StartRec()
    {
        currentFile = Path.Combine(saveDir, "자막_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
        lblFile.Text = "저장 파일: " + currentFile;
        txt.Clear();
        running = true;
        btnRec.Text = "■ 기록 중지";
        btnRec.BackColor = Color.FromArgb(108, 117, 125);
        string file = currentFile;
        worker = new Thread(delegate () { Loop(file); });
        worker.IsBackground = true;
        worker.Start();
    }

    void StopRec()
    {
        if (!running) return;
        running = false;
        if (worker != null) worker.Join(3000);
        btnRec.Text = "● 기록 시작";
        btnRec.BackColor = Color.FromArgb(220, 53, 69);
        lblStatus.Text = "중지됨 - 저장 완료";
        lblStatus.ForeColor = Color.Black;
    }

    void SaveDiag(bool open)
    {
        string p = Path.Combine(saveDir, "자막_진단_" + DateTime.Now.ToString("HHmmss") + ".txt");
        try
        {
            File.WriteAllText(p, CaptionReader.Diagnose(), new UTF8Encoding(true));
            if (open) Process.Start("notepad.exe", "\"" + p + "\"");
        }
        catch (Exception ex) { if (open) MessageBox.Show(this, "진단 저장 실패: " + ex.Message); }
    }

    void Loop(string file)
    {
        var reader = new CaptionReader();
        var acc = new Accumulator(file);
        acc.Written += delegate (string s)
        {
            if (IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate { txt.AppendText(s.Replace("\r\n", "\n").Replace("\n", "\r\n")); });
        };
        DateTime start = DateTime.Now, lastChange = DateTime.Now;
        string last = null;
        bool diagDone = false;
        int state = -1;
        try
        {
            while (running)
            {
                string raw = null;
                try { raw = reader.Read(); } catch { }
                if (raw == null)
                {
                    if (state != 0) { SetStatus("자막 말풍선을 찾는 중... (크롬 실시간 자막이 켜져 있고 소리가 나고 있어야 함)", Color.DarkOrange); state = 0; }
                    if (!diagDone && (DateTime.Now - start).TotalSeconds > 20) { diagDone = true; SaveDiag(false); }
                }
                else
                {
                    if (state != 1) { SetStatus("● 기록 중 - 자막 말풍선 연결됨", Color.Green); state = 1; }
                    string t = Accumulator.Normalize(raw);
                    if (t.Length > 0 && t != last)
                    {
                        acc.Feed(t);
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
        catch (Exception ex) { SetStatus("오류: " + ex.Message, Color.Red); }
        finally
        {
            try { acc.Flush(); } catch { }
        }
    }
}

// ---------------------------------------------------------------- 자체 시험 (--selftest 출력파일)
static class SelfTest
{
    public static void Run(string outPath)
    {
        if (File.Exists(outPath)) File.Delete(outPath);
        var acc = new Accumulator(outPath);
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
