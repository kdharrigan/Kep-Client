using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

/// <summary>
/// Shared OPC UA plumbing used by both the historian and the configuration
/// form: building the application configuration (PKI + client certificate),
/// discovering servers/endpoints, creating sessions, and browsing.
/// </summary>
public static class OpcUaHelper
{
    /// <summary>
    /// Builds and validates an ApplicationConfiguration and ensures a
    /// self-signed client instance certificate exists. Required for any
    /// connection, secured or not.
    /// </summary>
    public static async Task<ApplicationConfiguration> BuildConfigurationAsync()
    {
        // PKI stores must be specified even for an unsecured connection,
        // otherwise config validation throws
        // "TrustedIssuerCertificates StorePath must be specified."
        const string pkiRoot =
            @"%LocalApplicationData%\KepwareHistorianClient\pki";

        var config = new ApplicationConfiguration()
        {
            ApplicationName = "KepwareHistorianClient",
            ApplicationUri = "urn:localhost:KepwareHistorianClient",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                AutoAcceptUntrustedCertificates = true,
                AddAppCertToTrustedStore = true,
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = "Directory",
                    StorePath = pkiRoot + @"\own",
                    SubjectName = "CN=KepwareHistorianClient, DC=localhost"
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = pkiRoot + @"\issuer"
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = pkiRoot + @"\trusted"
                },
                RejectedCertificateStore = new CertificateStoreIdentifier
                {
                    StoreType = "Directory",
                    StorePath = pkiRoot + @"\rejected"
                }
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

        // The stack requires a client application instance certificate
        // even for unsecured sessions. Create a self-signed one in the
        // "own" store on first run if it doesn't already exist.
        var application = new ApplicationInstance
        {
            ApplicationName = config.ApplicationName,
            ApplicationType = ApplicationType.Client,
            ApplicationConfiguration = config
        };
        bool haveCert = await application.CheckApplicationInstanceCertificate(false, 2048);
        if (!haveCert)
        {
            throw new Exception("Application instance certificate could not be created.");
        }

        return config;
    }

    /// <summary>Enumerates the OPC UA servers reachable at a discovery URL.</summary>
    public static ApplicationDescriptionCollection DiscoverServers(string discoveryUrl)
    {
        using (var client = DiscoveryClient.Create(new Uri(discoveryUrl), EndpointConfiguration.Create()))
        {
            return client.FindServers(null);
        }
    }

    /// <summary>Lists the endpoints (security policies/modes) offered by a server.</summary>
    public static EndpointDescriptionCollection GetEndpoints(string discoveryUrl)
    {
        using (var client = DiscoveryClient.Create(new Uri(discoveryUrl), EndpointConfiguration.Create()))
        {
            return client.GetEndpoints(null);
        }
    }

    /// <summary>Creates a session to the configured endpoint using the configured identity.</summary>
    public static async Task<Session> CreateSessionAsync(ApplicationConfiguration config, AppSettings settings)
    {
        var endpointDescription = CoreClientUtils.SelectEndpoint(
            config,
            settings.Opc.EndpointUrl,
            settings.Opc.UseSecurity);

        var endpoint = new ConfiguredEndpoint(
            null,
            endpointDescription,
            EndpointConfiguration.Create(config));

        IUserIdentity identity = settings.Opc.Anonymous
            ? new UserIdentity()
            : new UserIdentity(settings.Opc.Username, settings.Opc.Password);

        return await Session.Create(
            config,
            endpoint,
            false,
            "KepwareSession",
            60000,
            identity,
            null);
    }

    /// <summary>
    /// Reads raw historical values for a node between two UTC times via OPC UA
    /// Historical Access, following continuation points until the range is
    /// exhausted. Works against Kepware's Local Historian for historized tags.
    /// </summary>
    public static List<DataValue> ReadHistory(Session session, NodeId nodeId, DateTime startUtc, DateTime endUtc)
    {
        var values = new List<DataValue>();

        var details = new ReadRawModifiedDetails
        {
            IsReadModified = false,
            StartTime = startUtc,
            EndTime = endUtc,
            NumValuesPerNode = 0, // 0 = no limit; server may still page
            ReturnBounds = false
        };

        var nodesToRead = new HistoryReadValueIdCollection
        {
            new HistoryReadValueId { NodeId = nodeId }
        };

        byte[] continuationPoint = null;
        do
        {
            nodesToRead[0].ContinuationPoint = continuationPoint;

            session.HistoryRead(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Source,
                false,
                nodesToRead,
                out HistoryReadResultCollection results,
                out DiagnosticInfoCollection _);

            if (results == null || results.Count == 0)
            {
                break;
            }

            HistoryReadResult result = results[0];
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode);
            }

            continuationPoint = result.ContinuationPoint;

            if (ExtensionObject.ToEncodeable(result.HistoryData) is HistoryData data)
            {
                values.AddRange(data.DataValues);
            }
        }
        while (continuationPoint != null && continuationPoint.Length > 0);

        return values;
    }

    /// <summary>
    /// Reads the Historizing attribute for a set of variable nodes in one call,
    /// returning a map of NodeId string -> whether the node has history enabled.
    /// </summary>
    public static Dictionary<string, bool> ReadHistorizingFlags(Session session, IList<NodeId> nodeIds)
    {
        var map = new Dictionary<string, bool>();

        var toRead = new ReadValueIdCollection();
        foreach (var id in nodeIds)
        {
            toRead.Add(new ReadValueId { NodeId = id, AttributeId = Attributes.Historizing });
        }

        session.Read(
            null,
            0,
            TimestampsToReturn.Neither,
            toRead,
            out DataValueCollection results,
            out DiagnosticInfoCollection _);

        for (int i = 0; i < nodeIds.Count && i < results.Count; i++)
        {
            bool historizing = false;
            DataValue dv = results[i];
            if (StatusCode.IsGood(dv.StatusCode) && dv.Value is bool b)
            {
                historizing = b;
            }
            map[nodeIds[i].ToString()] = historizing;
        }

        return map;
    }

    /// <summary>Browses the hierarchical children (objects and variables) of a node.</summary>
    public static ReferenceDescriptionCollection Browse(Session session, NodeId nodeId)
    {
        session.Browse(
            null,
            null,
            nodeId,
            0u,
            BrowseDirection.Forward,
            ReferenceTypeIds.HierarchicalReferences,
            true,
            (uint)(NodeClass.Object | NodeClass.Variable),
            out _,
            out ReferenceDescriptionCollection references);

        return references;
    }
}
