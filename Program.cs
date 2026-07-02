using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

class Program
{
    static Session session;
    static string csvPath = @"C:\Historian\kepware_historian.csv";
    static StreamWriter csvWriter;
    static readonly object csvLock = new object();
    static CancellationTokenSource cts = new CancellationTokenSource();

    static List<string> tagList = new List<string>
    {
        "ns=2;s=Channel1.Device1.Tag1",
        "ns=2;s=Channel1.Device1.Tag2"
        // add more tags OR later we can auto-discover them
    };

    static async Task Main(string[] args)
    {
        try
        {
            // Setup graceful shutdown
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            Directory.CreateDirectory(Path.GetDirectoryName(csvPath));

            // Initialize CSV with header if needed
            if (!File.Exists(csvPath))
            {
                File.WriteAllText(csvPath, "Timestamp,Tag,Value,StatusCode\n");
            }

            // Open StreamWriter for efficient CSV writing
            csvWriter = new StreamWriter(csvPath, append: true, bufferSize: 65536)
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
                    foreach (var tag in tagList)
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

                    await Task.Delay(5000, cts.Token); // scan rate
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
                Console.WriteLine($"Connecting to Kepware (attempt {retryCount + 1})...");

                var config = new ApplicationConfiguration()
                {
                    ApplicationName = "KepwareHistorianClient",
                    ApplicationType = ApplicationType.Client,
                    SecurityConfiguration = new SecurityConfiguration
                    {
                        AutoAcceptUntrustedCertificates = true,
                        ApplicationCertificate = new CertificateIdentifier()
                    },
                    TransportQuotas = new TransportQuotas
                    {
                        OperationTimeout = 15000
                    },
                    ClientConfiguration = new ClientConfiguration
                    {
                        DefaultSessionTimeout = 60000
                    }
                };

                await config.Validate(ApplicationType.Client);

                config.CertificateValidator.CertificateValidation += (s, e) =>
                {
                    e.Accept = true;
                };

                var endpointDescription =
                    CoreClientUtils.SelectEndpoint(
                        "opc.tcp://localhost:49320",
                        useSecurity: false
                    );

                var endpoint = new ConfiguredEndpoint(
                    null,
                    endpointDescription,
                    EndpointConfiguration.Create(config)
                );

                session = await Session.Create(
                    config,
                    endpoint,
                    false,
                    "KepwareSession",
                    60000,
                    null,
                    null
                );

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
