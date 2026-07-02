# Kepware OPC UA Historian Client

## Overview

This project is a lightweight OPC UA client written in C# for collecting industrial data from a Kepware server and storing it in CSV format for analysis, reporting, and historian applications.

## Features

- OPC UA connectivity
- Automatic reconnection
- Configurable tag list
- Timestamped data logging
- Status code tracking
- CSV export
- Graceful shutdown support
- Fault-tolerant read loop

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
