using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Dalamud.Configuration.Internal;
using Dalamud.Logging.Internal;
using Newtonsoft.Json;
using Serilog;
using Serilog.Core;
using Serilog.Events;

using static Dalamud.NativeFunctions;

namespace Dalamud
{
    /// <summary>
    /// The main entrypoint for the Dalamud system.
    /// </summary>
    public sealed class EntryPoint
    {
        /// <summary>
        /// A delegate used during initialization of the CLR from Dalamud.Boot.
        /// </summary>
        /// <param name="infoPtr">Pointer to a serialized <see cref="DalamudStartInfo"/> data.</param>
        public delegate void InitDelegate(IntPtr infoPtr);

        /// <summary>
        /// Initialize Dalamud.
        /// </summary>
        /// <param name="infoPtr">Pointer to a serialized <see cref="DalamudStartInfo"/> data.</param>
        public static void Initialize(IntPtr infoPtr)
        {
            var infoStr = Marshal.PtrToStringAnsi(infoPtr);
            var info = JsonConvert.DeserializeObject<DalamudStartInfo>(infoStr);

            new Thread(() => RunThread(info)).Start();
        }

        /// <summary>
        /// Initialize all Dalamud subsystems and start running on the main thread.
        /// </summary>
        /// <param name="info">The <see cref="DalamudStartInfo"/> containing information needed to initialize Dalamud.</param>
        private static void RunThread(DalamudStartInfo info)
        {
            // Setup logger
            var levelSwitch = InitLogging(info.WorkingDirectory);

            // Load configuration first to get some early persistent state, like log level
            var configuration = DalamudConfiguration.Load(info.ConfigurationPath);

            // Set the appropriate logging level from the configuration
#if !DEBUG
            levelSwitch.MinimumLevel = configuration.LogLevel;
#endif

            // Log any unhandled exception.
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            var finishSignal = new ManualResetEvent(false);

            try
            {
                Log.Information(new string('-', 80));
                Log.Information("Initializing a session..");

                // This is due to GitHub not supporting TLS 1.0, so we enable all TLS versions globally
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls;

                var dalamud = new Dalamud(info, levelSwitch, finishSignal, configuration);
                Log.Information("Starting a session..");

                // Run session
                dalamud.LoadTier1();
                dalamud.WaitForUnload();

                dalamud.Dispose();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Unhandled exception on main thread.");
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
                AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;

                Log.Information("Session has ended.");
                Log.CloseAndFlush();

                finishSignal.Set();
            }
        }

        private static LoggingLevelSwitch InitLogging(string baseDirectory)
        {
#if DEBUG
            var logPath = Path.Combine(baseDirectory, "dalamud.log");
            var oldPath = Path.Combine(baseDirectory, "dalamud.log.old");
#else
            var logPath = Path.Combine(baseDirectory, "..", "..", "..", "dalamud.log");
            var oldPath = Path.Combine(baseDirectory, "..", "..", "..", "dalamud.log.old");
#endif

            CullLogFile(logPath, oldPath, 1 * 1024 * 1024);
            CullLogFile(oldPath, null, 10 * 1024 * 1024);

            var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Verbose);
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Async(a => a.File(logPath))
                .WriteTo.Sink(SerilogEventSink.Instance)
                .MinimumLevel.ControlledBy(levelSwitch)
                .CreateLogger();

            return levelSwitch;
        }

        private static void CullLogFile(string logPath, string? oldPath, int cullingFileSize)
        {
            try
            {
                var bufferSize = 4096;

                var logFile = new FileInfo(logPath);

                if (!logFile.Exists)
                    logFile.Create();

                if (logFile.Length <= cullingFileSize)
                    return;

                var amountToCull = logFile.Length - cullingFileSize;

                if (amountToCull < bufferSize)
                    return;

                if (oldPath != null)
                {
                    var oldFile = new FileInfo(oldPath);

                    if (!oldFile.Exists)
                        oldFile.Create().Close();

                    using var reader = new BinaryReader(logFile.Open(FileMode.Open, FileAccess.Read));
                    using var writer = new BinaryWriter(oldFile.Open(FileMode.Append, FileAccess.Write));

                    var read = -1;
                    var total = 0;
                    var buffer = new byte[bufferSize];
                    while (read != 0 && total < amountToCull)
                    {
                        read = reader.Read(buffer, 0, buffer.Length);
                        writer.Write(buffer, 0, read);
                        total += read;
                    }
                }

                {
                    using var reader = new BinaryReader(logFile.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                    using var writer = new BinaryWriter(logFile.Open(FileMode.Open, FileAccess.Write, FileShare.ReadWrite));

                    reader.BaseStream.Seek(amountToCull, SeekOrigin.Begin);

                    var read = -1;
                    var total = 0;
                    var buffer = new byte[bufferSize];
                    while (read != 0)
                    {
                        read = reader.Read(buffer, 0, buffer.Length);
                        writer.Write(buffer, 0, read);
                        total += read;
                    }

                    writer.BaseStream.SetLength(total);
                }
            }
            catch (Exception ex)
            {
                var caption = "XIVLauncher Error";
                var message = $"Log cull threw an exception: {ex.Message}\n{ex.StackTrace ?? string.Empty}";
                _ = MessageBoxW(IntPtr.Zero, message, caption, MessageBoxType.IconError | MessageBoxType.Ok);
            }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            switch (args.ExceptionObject)
            {
                case Exception ex:
                    Log.Fatal(ex, "Unhandled exception on AppDomain");
                    break;
                default:
                    Log.Fatal("Unhandled SEH object on AppDomain: {Object}", args.ExceptionObject);
                    break;
            }
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs args)
        {
            if (!args.Observed)
                Log.Error(args.Exception, "Unobserved exception in Task.");
        }
    }
}
