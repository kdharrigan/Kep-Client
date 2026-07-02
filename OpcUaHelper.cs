using System;
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
