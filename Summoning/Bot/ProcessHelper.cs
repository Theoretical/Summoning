#if ENTRY
using System;
using System.Linq;
using System.Timers;
using Flash.Riot.platform.game;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Flash;
using Ignite;

namespace Summoning.Bot
{

    class ProcessHelper
    {
        private PlayerCredentialsDto _playerCredentialsDto;
        private bool _requestExit = false;
        private IgniteClient _ignite;

        public async void Launch(PlayerCredentialsDto playerCredentialsDto)
        {
            _playerCredentialsDto = playerCredentialsDto;
            _ignite = new IgniteClient(playerCredentialsDto);

            //Console.WriteLine("{0} {1} {2} {3}", playerCredentialsDto.serverIp, playerCredentialsDto.serverPort, playerCredentialsDto.encryptionKey, playerCredentialsDto.summonerId);
            //return;
            new System.Threading.Tasks.Task(() => _ignite.Start()).Start();
            var timer = new Timer();
            timer.Interval = TimeSpan.FromMinutes(5).TotalMilliseconds;
            timer.Elapsed += (s, e) =>
            {
                if (!_ignite.Connected)
                {
                    Flash.Log.Write("[{0}] Time out reached.", playerCredentialsDto.summonerName);
                    _ignite.Exit();
                    _ignite = new IgniteClient(_playerCredentialsDto);
                    new System.Threading.Tasks.Task(() => _ignite.Start()).Start();
                    return;
                }
                (s as Timer).Stop();
            };

            timer.Start();
        }

        public void Kill()
        {
            _requestExit = true;
            try
            {
                if (_ignite != null)
                {
                    _ignite.Exit();
                }
            }
            catch(Exception e) 
            {
                Flash.Log.Write("Error: {0}", e);
            }
        }
    }
}

#else
using System;
using System.Linq;
using System.Threading;
using Flash.Riot.platform.game;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Flash;

namespace Summoning.Bot
{
    class ProcessHelper
    {
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);



        [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
        public static extern IntPtr SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, int uFlags);

        private Process _process;
        private PlayerCredentialsDto _playerCredentialsDto;
        private bool _requestExit = false;
        public async void Launch(PlayerCredentialsDto playerCredentialsDto)
        {
            _playerCredentialsDto = playerCredentialsDto;
            _process = new Process();

            _process.StartInfo.WorkingDirectory = Globals.Configuration.GamePath;
            _process.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
            _process.StartInfo.FileName = Path.Combine(Globals.Configuration.GamePath, "League of Legends.exe");
            _process.Exited += OnExit;
            _process.EnableRaisingEvents = true;
            _process.StartInfo.Arguments = "\"8394\" \"LoLLauncher.exe\" \"" + "" + "\" \"" +
                playerCredentialsDto.serverIp + " " +
                playerCredentialsDto.serverPort + " " +
                playerCredentialsDto.encryptionKey + " " +
                playerCredentialsDto.summonerId + "\"";

            try
            {
                await Task.Run(() => _process.Start());

                _process.PriorityClass = ProcessPriorityClass.BelowNormal;
                Thread.Sleep(TimeSpan.FromSeconds(15));
                LoadPortal();

                var timer = new System.Timers.Timer();
                timer.Interval = TimeSpan.FromSeconds(5).TotalMilliseconds;

                await Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith((t) =>
                {
                    while (_process.MainWindowHandle == (IntPtr)0)
                        _process.Refresh();

                    ShowWindow(_process.MainWindowHandle, 9);
                    Log.Write("[{0}] Hiding process", _process.Id);
                    ShowWindow(_process.MainWindowHandle, 11);
                });

            }catch(Exception e)
            {
                Log.Write("Error on ProcessHandle: {0}", e);
                Launch(playerCredentialsDto);
            }
        }

        private void LoadPortal()
        {
            var process = new Process();
            process.StartInfo.FileName = "Summoning_Injector.exe";
            process.StartInfo.Arguments = _process.Id.ToString();
            process.Start();
        }

        private void OnExit(object sender, EventArgs args)
        {
            if (!_requestExit)
            {
                Log.Write("[{0}] process has crashed!", _process.Id);

                var p = Process.GetProcesses().ToList().Find(pc => pc.ProcessName.Contains("BsSndRpt"));

                if (p != null)
                    p.Kill();

                Launch(_playerCredentialsDto);
            }
        }

        public void Kill()
        {
            _requestExit = true;
            try
            {
                if (_process != null && Process.GetProcessById(_process.Id) != null)
                {
                    _process.Kill();
                    _process.WaitForExit();
                }
            }
            catch {  }
        }
    }
}

#endif