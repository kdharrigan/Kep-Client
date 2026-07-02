# Kepware OPC UA Historian Client

## Overview

This project is a lightweight OPC UA client written in C# for collecting industrial data from a Kepware server and storing it in CSV format for analysis, reporting, and historian applications.

## Features

- OPC UA connectivity
- Configuration UI: discover servers, browse the address space, pick tags
- Automatic reconnection
- Configurable tag list
- Timestamped data logging
- Status code tracking
- CSV export
- Graceful shutdown support
- Fault-tolerant read loop

## Running

The application launches a Windows Forms window by default:

```
dotnet run
```

All settings live in `appsettings.json` (endpoint, security, credentials,
CSV path, scan interval, and the tag list); the window reads and writes it,
so moving to a new machine is just build + configure. The window provides:

- **Live** tab: displays the current value, status, and update time of every
  configured tag, refreshed at the scan interval, and logs to CSV while running
- **Connection** tab: **discover** OPC UA servers at a discovery URL, list
  their endpoints, pick a security mode, and choose **Anonymous** or
  **username/password** login
- **Tags** tab: **browse** the server address space and add variable nodes
- **Logging** tab: set the **CSV output path** and **scan interval**
- **History** tab: read archived values for a tag over a time range from
  Kepware's Local Historian (OPC UA Historical Access) and export to CSV

Click **Save** on any change to persist `appsettings.json`.

### Headless mode

To run purely as a console logger (e.g. as a background service) with no UI:

```
dotnet run -- --headless
```

### Viewing historical data

Kepware's Local Historian archives (e.g.
`C:\ProgramData\PTC\Kepware Server\V7\Historical Data`) are a proprietary
binary format and should not be read directly. Instead, the **History** tab
queries them over OPC UA Historical Access: choose a tag and a start/end
time, click **Read History**, and optionally **Export CSV**. Only tags that
are enabled for historization in the Local Historian plug-in return data.

## Technologies

- C#
- .NET
- OPC Foundation UA .NET SDK
- Kepware OPC UA Server

## Example Output

Timestamp,Tag,Value,StatusCode

2026-06-22 10:15:00.123,Channel1.Device1.Tag1,42,Good

2026-06-22 10:15:00.123,Channel1.Device1.Tag2,73,Good

## Future Improvements

- Auto-discover tags
- SQL database support
- MQTT publishing
- Real-time dashboards
- Historical querying
- Tag configuration file

## Author

Shaudae Richardson
