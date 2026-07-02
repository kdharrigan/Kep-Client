using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Strongly-typed application settings persisted to appsettings.json next to
/// the executable. Written by the configuration form, read by the historian.
/// </summary>
public class AppSettings
{
    public OpcSettings Opc { get; set; } = new OpcSettings();
    public HistorianSettings Historian { get; set; } = new HistorianSettings();

    [JsonIgnore]
    public static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load {ConfigPath}: {ex.Message}");
        }

        return new AppSettings();
    }

    public void Save()
    {
        string json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}

public class OpcSettings
{
    /// <summary>Discovery/server URL used to enumerate servers and endpoints.</summary>
    public string DiscoveryUrl { get; set; } = "opc.tcp://localhost:49320";

    /// <summary>The endpoint the historian actually connects to.</summary>
    public string EndpointUrl { get; set; } = "opc.tcp://localhost:49320";

    /// <summary>True to select a secured endpoint, false for Security Policy None.</summary>
    public bool UseSecurity { get; set; } = false;

    /// <summary>True to log in anonymously; false to use Username/Password.</summary>
    public bool Anonymous { get; set; } = true;

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";
}

public class HistorianSettings
{
    public string CsvPath { get; set; } = @"C:\Historian\kepware_historian.csv";

    public int ScanIntervalMs { get; set; } = 5000;

    public List<string> Tags { get; set; } = new List<string>();
}
