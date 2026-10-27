using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

/// <summary>
/// Keeps a downloaded copy of the game current. On start it asks GitHub for the latest RELEASE of the
/// public repo (published by Tools/release.cmd); when its tag (v0.3.0) is newer than
/// <see cref="Application.version"/> the main menu offers the update. Taking it downloads the release's
/// <c>*-Win64.zip</c>, writes a small batch file that waits for the game to close, unzips the new build
/// over the install folder and starts it again, then quits.
///
/// Windows app only - the Editor and the web build never update themselves (the web page always serves
/// whatever the server holds). The install folder must be writable, which an unzipped download always is.
/// </summary>
public class GameUpdater : MonoBehaviour
{
    public static GameUpdater Instance { get; private set; }

    public enum State { Idle, Checking, UpToDate, Available, Downloading, Installing, Failed }

    [Tooltip("owner/repo whose GitHub Releases carry the game.")]
    public string repo = "mmamdouhmma-cmd/crucible-honorable-souls";
    [Tooltip("The release asset to download is the one whose name ends with this.")]
    public string assetSuffix = "-Win64.zip";

    public State Current { get; private set; } = State.Idle;
    public string LatestVersion { get; private set; } = "";
    public float Progress { get; private set; }
    public string Error { get; private set; } = "";

    /// <summary>True on the platforms that can replace their own files.</summary>
    public static bool Supported => Application.platform == RuntimePlatform.WindowsPlayer;

    string downloadUrl;
    UnityWebRequest download;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        if (Supported) StartCoroutine(Check());
    }

    [Serializable] class Release { public string tag_name; public Asset[] assets; }
    [Serializable] class Asset { public string name; public string browser_download_url; }

    IEnumerator Check()
    {
        Current = State.Checking;
        using (var req = UnityWebRequest.Get($"https://api.github.com/repos/{repo}/releases/latest"))
        {
            req.timeout = 10;
            req.SetRequestHeader("User-Agent", "CRUCIBLE");
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                // 404 = no release published yet; anything else = offline. Neither is worth bothering the player with.
                Current = State.UpToDate;
                Debug.Log("Updater: no release information (" + req.error + ")");
                yield break;
            }
            var rel = JsonUtility.FromJson<Release>(req.downloadHandler.text);
            if (rel == null || string.IsNullOrEmpty(rel.tag_name)) { Current = State.UpToDate; yield break; }
            LatestVersion = rel.tag_name.TrimStart('v', 'V');
            downloadUrl = null;
            if (rel.assets != null)
                foreach (var a in rel.assets)
                    if (a.name.EndsWith(assetSuffix, StringComparison.OrdinalIgnoreCase)) { downloadUrl = a.browser_download_url; break; }
            Current = downloadUrl != null && IsNewer(LatestVersion, Application.version) ? State.Available : State.UpToDate;
            Debug.Log($"Updater: this is {Application.version}, latest release {LatestVersion} -> {Current}");
        }
    }

    /// <summary>Compares dotted versions numerically (0.10.0 is newer than 0.9.2).</summary>
    public static bool IsNewer(string latest, string current)
    {
        var a = latest.Split('.');
        var b = current.Split('.');
        for (int i = 0; i < Mathf.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length && int.TryParse(a[i], out int xa) ? xa : 0;
            int y = i < b.Length && int.TryParse(b[i], out int yb) ? yb : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    /// <summary>Downloads the new build and restarts into it.</summary>
    public void Install()
    {
        if (Current != State.Available && Current != State.Failed) return;
        if (string.IsNullOrEmpty(downloadUrl)) return;
        StartCoroutine(DownloadAndRestart());
    }

    IEnumerator DownloadAndRestart()
    {
        Current = State.Downloading;
        Error = "";
        Progress = 0f;
        string work = Path.Combine(Path.GetTempPath(), "CrucibleUpdate");
        string zip = Path.Combine(work, "update.zip");
        try { Directory.CreateDirectory(work); if (File.Exists(zip)) File.Delete(zip); }
        catch (Exception e) { Fail("cannot write the download: " + e.Message); yield break; }

        download = new UnityWebRequest(downloadUrl, UnityWebRequest.kHttpVerbGET) { downloadHandler = new DownloadHandlerFile(zip) { removeFileOnAbort = true } };
        download.SetRequestHeader("User-Agent", "CRUCIBLE");
        var op = download.SendWebRequest();
        while (!op.isDone) { Progress = download.downloadProgress; yield return null; }
        bool ok = download.result == UnityWebRequest.Result.Success;
        string err = download.error;
        download.Dispose();
        download = null;
        if (!ok) { Fail("download failed: " + err); yield break; }
        Progress = 1f;
        Current = State.Installing;

        // The running exe and its data folder are locked until the game exits, so a batch file outside the
        // install folder waits for this process, unzips over the folder and launches the new exe.
        string install = Path.GetDirectoryName(Application.dataPath);            // ...\Crucible_Data -> ...\
        string exe = Path.Combine(install, Path.GetFileNameWithoutExtension(Application.dataPath).Replace("_Data", "") + ".exe");
        int pid = Process.GetCurrentProcess().Id;
        string script = Path.Combine(work, "apply-update.cmd");
        string unpack = Path.Combine(work, "unpacked");
        try
        {
            File.WriteAllText(script, InstallScript(pid, zip, unpack, install, exe, LatestVersion));
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized });
        }
        catch (Exception e) { Fail("could not start the installer: " + e.Message); yield break; }
        Debug.Log("Updater: restarting into " + LatestVersion);
        Application.Quit();
    }

    /// <summary>
    /// The batch file that applies an update once the game has closed: wait for <paramref name="pid"/> to
    /// exit, unzip, copy over <paramref name="install"/>, start <paramref name="exe"/>, clean up.
    /// </summary>
    public static string InstallScript(int pid, string zip, string unpack, string install, string exe, string version)
    {
        const string NL = "\r\n";
        return
            "@echo off" + NL +
            "title Updating CRUCIBLE..." + NL +
            "echo Updating CRUCIBLE to version " + version + " - this window closes by itself." + NL +
            ":wait" + NL +
            $"tasklist /fi \"PID eq {pid}\" | \"%SystemRoot%\\System32\\find.exe\" \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)" + NL +
            "powershell -NoProfile -ExecutionPolicy Bypass -Command \"" +
                "$ErrorActionPreference='Stop'; " +
                $"if (Test-Path '{unpack}') {{ Remove-Item -Recurse -Force '{unpack}' }}; " +
                $"Expand-Archive -Force '{zip}' '{unpack}'; " +
                // a zip of the folder's contents has the exe at the top; a zip of the folder has it one level
                // down - copy from wherever the exe actually is
                $"$src = (Get-ChildItem -Recurse -Filter '{Path.GetFileName(exe)}' '{unpack}' | Select-Object -First 1).DirectoryName; " +
                $"Copy-Item -Recurse -Force (Join-Path $src '*') '{install}'\"" + NL +
            "if errorlevel 1 (echo The update could not be applied. Download it again from the releases page. & pause & exit /b 1)" + NL +
            $"start \"\" \"{exe}\"" + NL +
            $"rmdir /s /q \"{unpack}\" & del \"{zip}\"" + NL;
    }

    void Fail(string why)
    {
        Error = why;
        Current = State.Failed;
        Debug.LogWarning("Updater: " + why);
    }

    void OnDestroy() { download?.Abort(); }
}
