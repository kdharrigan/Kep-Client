using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;

class Program
{
    static Session session;
    static AppSettings settings = new AppSettings();
    static StreamWriter csvWriter;
    static readonly object csvLock = new object();
    static CancellationTokenSource cts = new CancellationTokenSource();

    [STAThread]
    static void Main(string[] args)
    {
        // Run as a headless console logger with:  dotnet run -- --headless
        // (useful for running as a background service). Otherwise launch the UI.
        if (args.Any(a => a.Equals("--headless", StringComparison.OrdinalIgnoreCase))
            || args.Any(a => a.Equals("--run", StringComparison.OrdinalIgnoreCase)))
        {
            RunHistorianAsync().GetAwaiter().GetResult();
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ConfigForm());
    }

    static async Task RunHistorianAsync()
    {
        settings = AppSettings.Load();

        if (settings.Historian.Tags.Count == 0)
        {
            Console.WriteLine("No tags are configured yet.");
            Console.WriteLine("Run the configuration utility to discover a server and pick tags:");
            Console.WriteLine();
            Console.WriteLine("    dotnet run -- --configure");
            Console.WriteLine();
            return;
        }

        try
        {
            // Setup graceful shutdown
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            Directory.CreateDirectory(Path.GetDirectoryName(settings.Historian.CsvPath));

            // Initialize CSV with header if needed
            if (!File.Exists(settings.Historian.CsvPath))
            {
                File.WriteAllText(settings.Historian.CsvPath, "Timestamp,Tag,Value,StatusCode\n");
            }

            // Open StreamWriter for efficient CSV writing
            csvWriter = new StreamWriter(settings.Historian.CsvPath, append: true, encoding: new UTF8Encoding(false), bufferSize: 65536)
            {
                AutoFlush = false
            };

            await ConnectWithRetry();

            Console.WriteLine("Historian started...\n");

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    // Verify session is still alive
                    if (session == null || !session.Connected)
                    {
                        throw new Exception("Session disconnected");
                    }

                    // Read all tags
                    foreach (var tag in settings.Historian.Tags)
                    {
                        try
                        {
                            ReadAndWrite(tag);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error reading tag {tag}: {ex.Message}");
                            // Continue to next tag instead of crashing entire loop
                        }
                    }

                    // Flush periodically to ensure data is written
                    csvWriter.Flush();

                    await Task.Delay(settings.Historian.ScanIntervalMs, cts.Token); // scan rate
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Read loop error: {ex.Message}");
                    csvWriter.Flush();

                    await ConnectWithRetry();
                }
            }
        }
        finally
        {
            // Cleanup
            csvWriter?.Flush();
            csvWriter?.Dispose();
            session?.Close();
            session?.Dispose();
            Console.WriteLine("Historian stopped.");
        }
    }

    #region CONNECTION

    static async Task ConnectWithRetry()
    {
        int retryCount = 0;
        const int maxRetries = 10; // Prevent infinite retries
        const int retryDelayMs = 5000;

        while (retryCount < maxRetries)
        {
            try
            {
                Console.WriteLine($"Connecting to {settings.Opc.EndpointUrl} (attempt {retryCount + 1})...");

                var config = await OpcUaHelper.BuildConfigurationAsync();
                session = await OpcUaHelper.CreateSessionAsync(config, settings);

                Console.WriteLine("Connected successfully.\n");
                return;
            }
            catch (Exception ex)
            {
                retryCount++;
                Console.WriteLine($"Connection failed: {ex.Message}");

                if (retryCount < maxRetries)
                {
                    Console.WriteLine($"Retrying in {retryDelayMs / 1000} seconds...\n");
                    await Task.Delay(retryDelayMs, cts.Token);
                }
                else
                {
                    Console.WriteLine("Max connection retries exceeded. Exiting.");
                    cts.Cancel();
                    throw;
                }
            }
        }
    }

    #endregion

    #region READ + LOG

    static void ReadAndWrite(string nodeId)
    {
        DataValue value = session.ReadValue(nodeId);

        string line =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}," +
            $"{nodeId}," +
            $"{value.Value ?? "null"}," +
            $"{value.StatusCode}";

        lock (csvLock)
        {
            csvWriter.WriteLine(line);
        }

        Console.WriteLine(line);
    }

    #endregion
}
