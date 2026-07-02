using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;

/// <summary>
/// Windows Forms utility for configuring the historian on a new machine:
/// discover OPC UA servers, choose an endpoint, browse the address space to
/// pick tags, set credentials and logging options, then save appsettings.json.
/// </summary>
public class ConfigForm : Form
{
    private readonly AppSettings _settings;
    private Session _session;

    // Connection tab
    private TextBox _txtDiscoveryUrl;
    private ListBox _lstServers;
    private ListBox _lstEndpoints;
    private TextBox _txtEndpointUrl;
    private CheckBox _chkUseSecurity;
    private RadioButton _rbAnonymous;
    private RadioButton _rbUser;
    private TextBox _txtUser;
    private TextBox _txtPass;

    // Tags tab
    private TreeView _tree;
    private ListBox _lstTags;

    // Settings tab
    private TextBox _txtCsvPath;
    private NumericUpDown _numScan;

    // History tab
    private ComboBox _cmbHistTag;
    private DateTimePicker _dtStart;
    private DateTimePicker _dtEnd;
    private DataGridView _grid;

    // Live tab
    private Button _btnLive;
    private CheckBox _chkLog;
    private DataGridView _liveGrid;
    private System.Windows.Forms.Timer _liveTimer;
    private StreamWriter _liveCsv;
    private readonly System.Collections.Generic.Dictionary<string, int> _liveRowByTag =
        new System.Collections.Generic.Dictionary<string, int>();
    private bool _liveBusy;

    private Label _status;

    /// <summary>Node metadata stashed on each TreeNode.Tag.</summary>
    private class NodeTag
    {
        public NodeId Id;
        public bool IsVariable;
    }

    private class ServerItem
    {
        public ApplicationDescription Server;
        public string DiscoveryUrl =>
            Server.DiscoveryUrls != null && Server.DiscoveryUrls.Count > 0
                ? Server.DiscoveryUrls[0]
                : "";
        public override string ToString() =>
            $"{Server.ApplicationName?.Text}  [{Server.ApplicationType}]  {DiscoveryUrl}";
    }

    private class EndpointItem
    {
        public EndpointDescription Endpoint;
        public override string ToString()
        {
            string policy = Endpoint.SecurityPolicyUri ?? "";
            int hash = policy.LastIndexOf('#');
            string shortPolicy = hash >= 0 ? policy.Substring(hash + 1) : policy;
            return $"{Endpoint.EndpointUrl}   |   {shortPolicy}   |   {Endpoint.SecurityMode}";
        }
    }

    public ConfigForm()
    {
        _settings = AppSettings.Load();

        Text = "Kepware Historian – Configuration";
        Width = 780;
        Height = 620;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(680, 540);

        BuildUi();
        LoadSettingsIntoUi();
    }

    private void BuildUi()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildLiveTab());
        tabs.TabPages.Add(BuildConnectionTab());
        tabs.TabPages.Add(BuildTagsTab());
        tabs.TabPages.Add(BuildSettingsTab());
        tabs.TabPages.Add(BuildHistoryTab());
        tabs.Selected += (s, e) =>
        {
            if (e.TabPage != null && e.TabPage.Text == "History")
            {
                RefreshHistoryTagList();
            }
        };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var btnSave = new Button { Text = "Save", Width = 100, Height = 30, Left = 560, Top = 7, Anchor = AnchorStyles.Right | AnchorStyles.Top };
        var btnClose = new Button { Text = "Close", Width = 100, Height = 30, Left = 665, Top = 7, Anchor = AnchorStyles.Right | AnchorStyles.Top };
        btnSave.Click += (s, e) => Save();
        btnClose.Click += (s, e) => Close();
        bottom.Controls.Add(btnSave);
        bottom.Controls.Add(btnClose);

        _status = new Label { Dock = DockStyle.Bottom, Height = 22, Text = "Ready.", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), BorderStyle = BorderStyle.Fixed3D };

        Controls.Add(tabs);
        Controls.Add(bottom);
        Controls.Add(_status);
    }

    private TabPage BuildConnectionTab()
    {
        var page = new TabPage("Connection");

        var lblUrl = new Label { Text = "Discovery / server URL:", Left = 12, Top = 15, Width = 150 };
        _txtDiscoveryUrl = new TextBox { Left = 165, Top = 12, Width = 380, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var btnDiscover = new Button { Text = "Discover Servers", Left = 555, Top = 11, Width = 150, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        btnDiscover.Click += async (s, e) => await DiscoverServersAsync();

        var lblServers = new Label { Text = "Servers:", Left = 12, Top = 46, Width = 150 };
        _lstServers = new ListBox { Left = 15, Top = 66, Width = 690, Height = 120, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        _lstServers.SelectedIndexChanged += async (s, e) => await LoadEndpointsForSelectedServerAsync();

        var lblEndpoints = new Label { Text = "Endpoints (double-click to use):", Left = 12, Top = 194, Width = 250 };
        _lstEndpoints = new ListBox { Left = 15, Top = 214, Width = 690, Height = 120, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        _lstEndpoints.DoubleClick += (s, e) => UseSelectedEndpoint();

        var lblSelected = new Label { Text = "Endpoint URL:", Left = 12, Top = 348, Width = 150 };
        _txtEndpointUrl = new TextBox { Left = 165, Top = 345, Width = 380, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        _chkUseSecurity = new CheckBox { Text = "Use security", Left = 555, Top = 347, Width = 150, Anchor = AnchorStyles.Top | AnchorStyles.Right };

        var grpIdentity = new GroupBox { Text = "Identity", Left = 15, Top = 380, Width = 690, Height = 110, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        _rbAnonymous = new RadioButton { Text = "Anonymous", Left = 15, Top = 25, Width = 120 };
        _rbUser = new RadioButton { Text = "Username / password", Left = 15, Top = 52, Width = 160 };
        var lblUser = new Label { Text = "Username:", Left = 200, Top = 28, Width = 70 };
        _txtUser = new TextBox { Left = 275, Top = 25, Width = 180 };
        var lblPass = new Label { Text = "Password:", Left = 200, Top = 58, Width = 70 };
        _txtPass = new TextBox { Left = 275, Top = 55, Width = 180, UseSystemPasswordChar = true };
        _rbAnonymous.CheckedChanged += (s, e) => UpdateIdentityEnabled();
        grpIdentity.Controls.AddRange(new Control[] { _rbAnonymous, _rbUser, lblUser, _txtUser, lblPass, _txtPass });

        page.Controls.AddRange(new Control[]
        {
            lblUrl, _txtDiscoveryUrl, btnDiscover,
            lblServers, _lstServers,
            lblEndpoints, _lstEndpoints,
            lblSelected, _txtEndpointUrl, _chkUseSecurity,
            grpIdentity
        });
        return page;
    }

    private TabPage BuildTagsTab()
    {
        var page = new TabPage("Tags");

        var btnBrowse = new Button { Text = "Connect && Browse", Left = 15, Top = 12, Width = 160 };
        btnBrowse.Click += async (s, e) => await ConnectAndBrowseAsync();

        _tree = new TreeView { Left = 15, Top = 46, Width = 400, Height = 490, HideSelection = false, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left };
        _tree.BeforeExpand += Tree_BeforeExpand;
        // Double-clicking a variable (leaf) node adds it as a tag.
        _tree.NodeMouseDoubleClick += (s, e) =>
        {
            if (e.Node?.Tag is NodeTag t && t.IsVariable)
            {
                AddNode(e.Node);
            }
        };

        var btnAdd = new Button { Text = "Add tag  >>", Left = 425, Top = 120, Width = 110, Anchor = AnchorStyles.Top };
        btnAdd.Click += (s, e) => AddSelectedTag();
        var btnRemove = new Button { Text = "<< Remove", Left = 425, Top = 160, Width = 110, Anchor = AnchorStyles.Top };
        btnRemove.Click += (s, e) => RemoveSelectedTag();

        var lblTags = new Label { Text = "Tags to log:", Left = 545, Top = 28, Width = 150, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _lstTags = new ListBox { Left = 545, Top = 46, Width = 205, Height = 490, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right };

        page.Controls.AddRange(new Control[] { btnBrowse, _tree, btnAdd, btnRemove, lblTags, _lstTags });
        return page;
    }

    private TabPage BuildSettingsTab()
    {
        var page = new TabPage("Logging");

        var lblCsv = new Label { Text = "CSV output path:", Left = 15, Top = 24, Width = 120 };
        _txtCsvPath = new TextBox { Left = 140, Top = 21, Width = 470, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var btnBrowseCsv = new Button { Text = "...", Left = 620, Top = 20, Width = 40, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        btnBrowseCsv.Click += (s, e) => BrowseCsvPath();

        var lblScan = new Label { Text = "Scan interval (ms):", Left = 15, Top = 64, Width = 120 };
        _numScan = new NumericUpDown { Left = 140, Top = 61, Width = 120, Minimum = 100, Maximum = 3600000, Increment = 500 };

        page.Controls.AddRange(new Control[] { lblCsv, _txtCsvPath, btnBrowseCsv, lblScan, _numScan });
        return page;
    }

    private TabPage BuildLiveTab()
    {
        var page = new TabPage("Live");

        _btnLive = new Button { Text = "Start", Left = 12, Top = 12, Width = 100, Height = 28 };
        _btnLive.Click += async (s, e) => await ToggleLiveAsync();

        _chkLog = new CheckBox { Text = "Log to CSV while running", Left = 125, Top = 16, Width = 220, Checked = true };

        _liveGrid = new DataGridView
        {
            Left = 12,
            Top = 50,
            Width = 736,
            Height = 484,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        _liveGrid.Columns.Add("Tag", "Tag");
        _liveGrid.Columns.Add("Value", "Value");
        _liveGrid.Columns.Add("Status", "Status");
        _liveGrid.Columns.Add("Updated", "Updated");

        page.Controls.AddRange(new Control[] { _btnLive, _chkLog, _liveGrid });
        return page;
    }

    private async Task ToggleLiveAsync()
    {
        if (_liveTimer != null && _liveTimer.Enabled)
        {
            StopLive();
            return;
        }

        SyncHistorianFromUi();
        if (_settings.Historian.Tags.Count == 0)
        {
            SetStatus("No tags to monitor. Add tags on the Tags tab first.");
            return;
        }

        try
        {
            UseWaitCursor = true;
            SetStatus("Starting live monitor ...");

            await EnsureSessionAsync();

            _liveGrid.Rows.Clear();
            _liveRowByTag.Clear();
            foreach (var tag in _settings.Historian.Tags)
            {
                int idx = _liveGrid.Rows.Add(tag, "", "", "");
                _liveRowByTag[tag] = idx;
            }

            if (_chkLog.Checked)
            {
                OpenLiveCsv();
            }

            if (_liveTimer == null)
            {
                _liveTimer = new System.Windows.Forms.Timer();
                _liveTimer.Tick += LiveTimer_Tick;
            }
            _liveTimer.Interval = Math.Max(100, _settings.Historian.ScanIntervalMs);
            _liveTimer.Start();

            _btnLive.Text = "Stop";
            SetStatus($"Live: monitoring {_settings.Historian.Tags.Count} tag(s) every {_liveTimer.Interval} ms" +
                      (_chkLog.Checked ? $", logging to {_settings.Historian.CsvPath}." : "."));
        }
        catch (Exception ex)
        {
            SetStatus("Failed to start live monitor.");
            MessageBox.Show(this, ex.Message, "Live monitor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            StopLive();
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private async void LiveTimer_Tick(object sender, EventArgs e)
    {
        if (_liveBusy)
        {
            return; // don't overlap reads if the server is slow
        }
        if (_session == null || !_session.Connected)
        {
            SetStatus("Session lost – stopping live monitor.");
            StopLive();
            return;
        }

        _liveBusy = true;
        try
        {
            foreach (var tag in _settings.Historian.Tags)
            {
                string value;
                string status;
                try
                {
                    DataValue dv = await Task.Run(() => _session.ReadValue(tag));
                    value = dv.Value?.ToString() ?? "null";
                    status = dv.StatusCode.ToString();
                }
                catch (Exception ex)
                {
                    value = "ERR";
                    status = ex.Message;
                }

                DateTime ts = DateTime.Now;
                UpdateLiveRow(tag, value, status, ts);

                if (_liveCsv != null && value != "ERR")
                {
                    _liveCsv.WriteLine($"{ts:yyyy-MM-dd HH:mm:ss.fff},{tag},{value},{status}");
                }
            }

            _liveCsv?.Flush();
        }
        finally
        {
            _liveBusy = false;
        }
    }

    private void UpdateLiveRow(string tag, string value, string status, DateTime ts)
    {
        if (_liveRowByTag.TryGetValue(tag, out int idx) && idx < _liveGrid.Rows.Count)
        {
            var cells = _liveGrid.Rows[idx].Cells;
            cells[1].Value = value;
            cells[2].Value = status;
            cells[3].Value = ts.ToString("HH:mm:ss.fff");
        }
    }

    private void OpenLiveCsv()
    {
        string dir = Path.GetDirectoryName(_settings.Historian.CsvPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        bool exists = File.Exists(_settings.Historian.CsvPath);
        _liveCsv = new StreamWriter(_settings.Historian.CsvPath, append: true, new UTF8Encoding(false))
        {
            AutoFlush = false
        };
        if (!exists)
        {
            _liveCsv.WriteLine("Timestamp,Tag,Value,StatusCode");
        }
    }

    private void StopLive()
    {
        _liveTimer?.Stop();
        if (_btnLive != null)
        {
            _btnLive.Text = "Start";
        }
        if (_liveCsv != null)
        {
            try
            {
                _liveCsv.Flush();
                _liveCsv.Dispose();
            }
            catch
            {
                // ignore flush/dispose errors on shutdown
            }
            _liveCsv = null;
        }
        SetStatus("Live monitor stopped.");
    }

    private TabPage BuildHistoryTab()
    {
        var page = new TabPage("History");

        var lblTag = new Label { Text = "Tag (NodeId):", Left = 12, Top = 16, Width = 90 };
        _cmbHistTag = new ComboBox { Left = 105, Top = 12, Width = 440, DropDownStyle = ComboBoxStyle.DropDown, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

        var lblStart = new Label { Text = "Start:", Left = 12, Top = 48, Width = 45 };
        _dtStart = new DateTimePicker { Left = 60, Top = 44, Width = 180, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", ShowUpDown = true };
        var lblEnd = new Label { Text = "End:", Left = 260, Top = 48, Width = 35 };
        _dtEnd = new DateTimePicker { Left = 300, Top = 44, Width = 180, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", ShowUpDown = true };

        // Default to the last hour.
        _dtEnd.Value = DateTime.Now;
        _dtStart.Value = DateTime.Now.AddHours(-1);

        var btnRead = new Button { Text = "Read History", Left = 500, Top = 42, Width = 120, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        btnRead.Click += async (s, e) => await ReadHistoryAsync();
        var btnExport = new Button { Text = "Export CSV...", Left = 628, Top = 42, Width = 120, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        btnExport.Click += (s, e) => ExportHistory();

        _grid = new DataGridView
        {
            Left = 12,
            Top = 82,
            Width = 736,
            Height = 452,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        _grid.Columns.Add("Timestamp", "Timestamp");
        _grid.Columns.Add("Value", "Value");
        _grid.Columns.Add("Status", "Status");

        page.Controls.AddRange(new Control[]
        {
            lblTag, _cmbHistTag,
            lblStart, _dtStart, lblEnd, _dtEnd,
            btnRead, btnExport,
            _grid
        });
        return page;
    }

    private void RefreshHistoryTagList()
    {
        string current = _cmbHistTag.Text;
        _cmbHistTag.Items.Clear();
        foreach (var tag in _lstTags.Items)
        {
            _cmbHistTag.Items.Add(tag.ToString());
        }
        if (!string.IsNullOrEmpty(current))
        {
            _cmbHistTag.Text = current;
        }
        else if (_cmbHistTag.Items.Count > 0)
        {
            _cmbHistTag.SelectedIndex = 0;
        }
    }

    private async Task ReadHistoryAsync()
    {
        string nodeIdStr = _cmbHistTag.Text.Trim();
        if (nodeIdStr.Length == 0)
        {
            SetStatus("Select or enter a tag NodeId to read.");
            return;
        }

        DateTime startUtc = _dtStart.Value.ToUniversalTime();
        DateTime endUtc = _dtEnd.Value.ToUniversalTime();

        try
        {
            UseWaitCursor = true;
            SetStatus($"Reading history for {nodeIdStr} ...");

            var session = await EnsureSessionAsync();
            NodeId nodeId = NodeId.Parse(nodeIdStr);
            var values = await Task.Run(() => OpcUaHelper.ReadHistory(session, nodeId, startUtc, endUtc));

            _grid.Rows.Clear();
            foreach (var dv in values)
            {
                _grid.Rows.Add(
                    dv.SourceTimestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    dv.Value?.ToString() ?? "null",
                    dv.StatusCode.ToString());
            }

            SetStatus(values.Count == 0
                ? "No historical values returned. Confirm the tag is historized in the Local Historian and the time range has data."
                : $"Read {values.Count} value(s) for {nodeIdStr}.");
        }
        catch (Exception ex)
        {
            SetStatus("History read failed.");
            MessageBox.Show(this, ex.Message, "History read failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void ExportHistory()
    {
        if (_grid.Rows.Count == 0)
        {
            SetStatus("Nothing to export – read history first.");
            return;
        }

        using (var dlg = new SaveFileDialog
        {
            Title = "Export history to CSV",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = "history_export.csv"
        })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            string tag = _cmbHistTag.Text.Trim();
            try
            {
                using (var w = new StreamWriter(dlg.FileName, false, new UTF8Encoding(false)))
                {
                    w.WriteLine("Timestamp,Tag,Value,StatusCode");
                    foreach (DataGridViewRow row in _grid.Rows)
                    {
                        if (row.IsNewRow)
                        {
                            continue;
                        }
                        string ts = row.Cells[0].Value?.ToString() ?? "";
                        string val = row.Cells[1].Value?.ToString() ?? "";
                        string st = row.Cells[2].Value?.ToString() ?? "";
                        w.WriteLine($"{ts},{tag},{val},{st}");
                    }
                }
                SetStatus($"Exported {_grid.Rows.Count} row(s) to {dlg.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void LoadSettingsIntoUi()
    {
        _txtDiscoveryUrl.Text = _settings.Opc.DiscoveryUrl;
        _txtEndpointUrl.Text = _settings.Opc.EndpointUrl;
        _chkUseSecurity.Checked = _settings.Opc.UseSecurity;
        _rbAnonymous.Checked = _settings.Opc.Anonymous;
        _rbUser.Checked = !_settings.Opc.Anonymous;
        _txtUser.Text = _settings.Opc.Username;
        _txtPass.Text = _settings.Opc.Password;

        _txtCsvPath.Text = _settings.Historian.CsvPath;
        _numScan.Value = Math.Min(_numScan.Maximum, Math.Max(_numScan.Minimum, _settings.Historian.ScanIntervalMs));

        _lstTags.Items.Clear();
        foreach (var tag in _settings.Historian.Tags)
        {
            _lstTags.Items.Add(tag);
        }

        UpdateIdentityEnabled();
    }

    private void UpdateIdentityEnabled()
    {
        bool useUser = _rbUser.Checked;
        _txtUser.Enabled = useUser;
        _txtPass.Enabled = useUser;
    }

    private async Task DiscoverServersAsync()
    {
        string url = _txtDiscoveryUrl.Text.Trim();
        if (url.Length == 0)
        {
            SetStatus("Enter a discovery URL first.");
            return;
        }

        try
        {
            UseWaitCursor = true;
            SetStatus($"Discovering servers at {url} ...");
            var servers = await Task.Run(() => OpcUaHelper.DiscoverServers(url));

            _lstServers.Items.Clear();
            _lstEndpoints.Items.Clear();
            foreach (var server in servers)
            {
                _lstServers.Items.Add(new ServerItem { Server = server });
            }
            SetStatus($"Found {servers.Count} server(s). Select one to list its endpoints.");
        }
        catch (Exception ex)
        {
            SetStatus("Discovery failed.");
            MessageBox.Show(this, ex.Message, "Discovery failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private async Task LoadEndpointsForSelectedServerAsync()
    {
        if (!(_lstServers.SelectedItem is ServerItem item))
        {
            return;
        }

        string url = string.IsNullOrEmpty(item.DiscoveryUrl) ? _txtDiscoveryUrl.Text.Trim() : item.DiscoveryUrl;
        try
        {
            UseWaitCursor = true;
            SetStatus($"Getting endpoints for {url} ...");
            var endpoints = await Task.Run(() => OpcUaHelper.GetEndpoints(url));

            _lstEndpoints.Items.Clear();
            foreach (var ep in endpoints)
            {
                _lstEndpoints.Items.Add(new EndpointItem { Endpoint = ep });
            }
            SetStatus($"Found {endpoints.Count} endpoint(s). Double-click one to use it.");
        }
        catch (Exception ex)
        {
            SetStatus("Failed to get endpoints.");
            MessageBox.Show(this, ex.Message, "Endpoints", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void UseSelectedEndpoint()
    {
        if (!(_lstEndpoints.SelectedItem is EndpointItem item))
        {
            return;
        }

        _txtEndpointUrl.Text = item.Endpoint.EndpointUrl;
        _chkUseSecurity.Checked = item.Endpoint.SecurityMode != MessageSecurityMode.None;
        SetStatus($"Using endpoint {item.Endpoint.EndpointUrl} ({item.Endpoint.SecurityMode}).");
    }

    /// <summary>Reuses the current session if connected, otherwise opens a new one.</summary>
    private async Task<Session> EnsureSessionAsync()
    {
        if (_session != null && _session.Connected)
        {
            return _session;
        }

        ApplyConnectionToSettings();
        var config = await OpcUaHelper.BuildConfigurationAsync();
        CloseSession();
        _session = await OpcUaHelper.CreateSessionAsync(config, _settings);
        return _session;
    }

    private async Task ConnectAndBrowseAsync()
    {
        try
        {
            UseWaitCursor = true;
            SetStatus("Connecting ...");

            // Force a fresh session so browsing reflects the current settings.
            ApplyConnectionToSettings();
            CloseSession();
            await EnsureSessionAsync();

            _tree.Nodes.Clear();
            var root = new TreeNode("Objects")
            {
                Tag = new NodeTag { Id = ObjectIds.ObjectsFolder, IsVariable = false }
            };
            root.Nodes.Add(new TreeNode("...")); // placeholder so it can expand
            _tree.Nodes.Add(root);
            root.Expand();

            SetStatus("Connected. Expand the tree and add variable nodes as tags.");
        }
        catch (Exception ex)
        {
            SetStatus("Connect failed.");
            MessageBox.Show(this, ex.Message, "Connect failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void Tree_BeforeExpand(object sender, TreeViewCancelEventArgs e)
    {
        var node = e.Node;
        if (node.Nodes.Count != 1 || node.Nodes[0].Text != "...")
        {
            return; // already populated
        }
        if (_session == null || !(node.Tag is NodeTag tag))
        {
            return;
        }

        node.Nodes.Clear();
        try
        {
            var refs = OpcUaHelper.Browse(_session, tag.Id);
            foreach (var r in refs)
            {
                var childId = ExpandedNodeId.ToNodeId(r.NodeId, _session.NamespaceUris);
                bool isVariable = r.NodeClass == NodeClass.Variable;
                var child = new TreeNode(r.DisplayName.Text)
                {
                    Tag = new NodeTag { Id = childId, IsVariable = isVariable },
                    ForeColor = isVariable ? Color.DarkGreen : Color.Black
                };
                if (!isVariable)
                {
                    child.Nodes.Add(new TreeNode("...")); // objects may have children
                }
                node.Nodes.Add(child);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Browse failed: {ex.Message}");
        }
    }

    private void AddSelectedTag()
    {
        AddNode(_tree.SelectedNode);
    }

    private void AddNode(TreeNode node)
    {
        if (node?.Tag is NodeTag tag && tag.Id != null)
        {
            string id = tag.Id.ToString();
            if (_lstTags.Items.Contains(id))
            {
                SetStatus($"{id} is already in the list.");
                return;
            }

            _lstTags.Items.Add(id);
            SetStatus(tag.IsVariable
                ? $"Added {id}."
                : $"Added {id}  (note: not a variable node, may not be readable).");
        }
        else
        {
            SetStatus("Select a node in the tree first, then click Add (variables are shown in green).");
        }
    }

    private void RemoveSelectedTag()
    {
        if (_lstTags.SelectedItem is string s)
        {
            _lstTags.Items.Remove(s);
            SetStatus($"Removed {s}.");
        }
    }

    private void BrowseCsvPath()
    {
        using (var dlg = new SaveFileDialog
        {
            Title = "CSV output file",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = _txtCsvPath.Text
        })
        {
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _txtCsvPath.Text = dlg.FileName;
            }
        }
    }

    private void ApplyConnectionToSettings()
    {
        _settings.Opc.DiscoveryUrl = _txtDiscoveryUrl.Text.Trim();
        _settings.Opc.EndpointUrl = _txtEndpointUrl.Text.Trim();
        _settings.Opc.UseSecurity = _chkUseSecurity.Checked;
        _settings.Opc.Anonymous = _rbAnonymous.Checked;
        _settings.Opc.Username = _txtUser.Text;
        _settings.Opc.Password = _txtPass.Text;
    }

    private void SyncHistorianFromUi()
    {
        _settings.Historian.CsvPath = _txtCsvPath.Text.Trim();
        _settings.Historian.ScanIntervalMs = (int)_numScan.Value;

        _settings.Historian.Tags.Clear();
        foreach (var item in _lstTags.Items)
        {
            _settings.Historian.Tags.Add(item.ToString());
        }
    }

    private void Save()
    {
        ApplyConnectionToSettings();
        SyncHistorianFromUi();

        try
        {
            _settings.Save();
            SetStatus($"Saved to {AppSettings.ConfigPath}");
            MessageBox.Show(this,
                $"Configuration saved to:\n{AppSettings.ConfigPath}\n\nRun the historian with:\n    dotnet run",
                "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CloseSession()
    {
        try
        {
            _session?.Close();
            _session?.Dispose();
        }
        catch
        {
            // ignore teardown errors
        }
        _session = null;
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopLive();
        CloseSession();
        base.OnFormClosed(e);
    }
}
