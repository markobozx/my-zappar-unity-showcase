using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace ZapparCliTools
{
    // Runs one external process (zapworks / node / npm) and streams its output back to the
    // main thread. Process events fire on worker threads, so lines are queued and drained
    // from EditorApplication.update before touching any Unity API.
    public class ZapparCliRunner
    {
        static readonly Regex Ansi = new Regex(@"\x1B\[[0-9;?]*[ -/]*[@-~]");

        readonly ConcurrentQueue<string> m_lines = new ConcurrentQueue<string>();
        Process m_process;
        Action<int, string> m_onExit;
        readonly System.Text.StringBuilder m_output = new System.Text.StringBuilder();
        volatile bool m_exited;
        int m_exitCode;

        public string Label { get; private set; }
        public bool IsRunning => m_process != null;
        public event Action<string> OnLine;

        public static string WorkingDirectory => Directory.GetCurrentDirectory();

        public bool Start(string label, string exe, string args, Action<int, string> onExit)
        {
            if (IsRunning) return false;
            Label = label;
            m_onExit = onExit;
            m_output.Clear();
            m_exited = false;

            var psi = new ProcessStartInfo(exe, args)
            {
                WorkingDirectory = WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // The CLI uses interactive prompts; a closed stdin makes any missed prompt fail
                // instead of hanging forever.
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };
            // Unity launched from the Dock/Hub doesn't inherit the shell PATH, and the zapworks
            // script starts with "#!/usr/bin/env node", so node's folder must be on PATH.
            string nodeDir = Path.GetDirectoryName(ZapparCliSettings.NodePath);
            string path = psi.EnvironmentVariables["PATH"] ?? "";
            psi.EnvironmentVariables["PATH"] = string.Join(":", nodeDir, "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin", "/usr/sbin", "/sbin", path);
            psi.EnvironmentVariables["FORCE_COLOR"] = "0";
            psi.EnvironmentVariables["NO_COLOR"] = "1";

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (s, e) => { if (e.Data != null) m_lines.Enqueue(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) m_lines.Enqueue(e.Data); };
            p.Exited += (s, e) => { m_exitCode = SafeExitCode(p); m_exited = true; };

            Emit("$ " + exe + " " + args);
            try
            {
                p.Start();
            }
            catch (Exception ex)
            {
                Emit("Could not start " + exe + ": " + ex.Message);
                Label = null;
                onExit?.Invoke(-1, m_output.ToString());
                return false;
            }
            p.StandardInput.Close();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            m_process = p;
            EditorApplication.update += Pump;
            return true;
        }

        public void Stop()
        {
            if (m_process == null) return;
            try { if (!m_process.HasExited) m_process.Kill(); } catch { }
            Emit("[stopped]");
        }

        static int SafeExitCode(Process p)
        {
            try { return p.ExitCode; } catch { return -1; }
        }

        void Emit(string line)
        {
            line = Ansi.Replace(line, "");
            m_output.AppendLine(line);
            OnLine?.Invoke(line);
        }

        void Pump()
        {
            while (m_lines.TryDequeue(out var line)) Emit(line);
            if (!m_exited) return;
            // Output events can still arrive just after Exited; WaitForExit flushes them.
            try { m_process.WaitForExit(); } catch { }
            while (m_lines.TryDequeue(out var line)) Emit(line);

            EditorApplication.update -= Pump;
            m_process.Dispose();
            m_process = null;
            Label = null;
            var cb = m_onExit;
            m_onExit = null;
            cb?.Invoke(m_exitCode, m_output.ToString());
        }
    }
}
