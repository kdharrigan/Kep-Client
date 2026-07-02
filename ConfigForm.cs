using System;
using System.Drawing;
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
        tabs.TabPages.Add(BuildConnectionTab());
        tabs.TabPages.Add(BuildTagsTab());
        tabs.TabPages.Add(BuildSettingsTab());

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

        _tree = new TreeView { Left = 15, Top = 46, Width = 400, Height = 490, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left };
        _tree.BeforeExpand += Tree_BeforeExpand;

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

    private async Task ConnectAndBrowseAsync()
    {
        // Persist the current connection choices so CreateSession uses them.
        ApplyConnectionToSettings();

        try
        {
            UseWaitCursor = true;
            SetStatus("Connecting ...");

            var config = await OpcUaHelper.BuildConfigurationAsync();
            CloseSession();
            _session = await OpcUaHelper.CreateSessionAsync(config, _settings);

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
        var node = _tree.SelectedNode;
        if (node?.Tag is NodeTag tag && tag.IsVariable)
        {
            string id = tag.Id.ToString();
            if (!_lstTags.Items.Contains(id))
            {
                _lstTags.Items.Add(id);
                SetStatus($"Added {id}.");
            }
        }
        else
        {
            SetStatus("Select a variable node (shown in green) to add.");
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

    private void Save()
    {
        ApplyConnectionToSettings();

        _settings.Historian.CsvPath = _txtCsvPath.Text.Trim();
        _settings.Historian.ScanIntervalMs = (int)_numScan.Value;

        _settings.Historian.Tags.Clear();
        foreach (var item in _lstTags.Items)
        {
            _settings.Historian.Tags.Add(item.ToString());
        }

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
        CloseSession();
        base.OnFormClosed(e);
    }
}
