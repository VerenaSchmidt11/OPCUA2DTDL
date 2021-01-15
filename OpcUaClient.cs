﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System.Threading;
using System.Diagnostics;
using System.Collections.ObjectModel;
using OPCUA2DTDL.Models;

namespace OPCUA2DTDL
{

    public enum ExitCode : int
    {
        Ok = 0,
        ErrorCreateApplication = 0x11,
        ErrorDiscoverEndpoints = 0x12,
        ErrorCreateSession = 0x13,
        ErrorBrowseNamespace = 0x14,
        ErrorCreateSubscription = 0x15,
        ErrorMonitoredItem = 0x16,
        ErrorAddSubscription = 0x17,
        ErrorRunning = 0x18,
        ErrorNoKeepAlive = 0x30,
        ErrorInvalidCommandLine = 0x100
    };

    class OpcUaClient
    {

        private string _appName;
        private const int _reconnectPeriod = 10;
        private Session _session;
        private string _endpointURL;
        private int _clientRunTime = Timeout.Infinite;
        private static bool _autoAccept = false;
        private static ExitCode _exitCode;
        private OpcUaNodeList _list = new OpcUaNodeList();
        private static Dictionary<string, string> _map = new Dictionary<string, string>();

        public OpcUaClient(string endpointURL, bool autoAccept, int stopTimeout, string appName)
        {

            _endpointURL = endpointURL;
            _autoAccept = autoAccept;
            _clientRunTime = stopTimeout <= 0 ? Timeout.Infinite : stopTimeout * 1000;
            _appName = appName;

        }

        static OpcUaClient()
        {

            CreateSchemaMap();

        }

        public static ExitCode ExitCode { get => _exitCode; }

        public async Task<Session> Connect()
        {

            _exitCode = ExitCode.ErrorCreateApplication;

            ApplicationInstance application = new ApplicationInstance
            {

                ApplicationName = _appName,
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = Utils.IsRunningOnMono() ? "Opc.Ua.MonoSampleClient" : "Opc.Ua.SampleClient"

            };

            // Load the application configuration.
            ApplicationConfiguration config = await application.LoadApplicationConfiguration(false);

            // Check the application certificate.
            bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(false, 0);

            if (!haveAppCertificate)
            {

                throw new Exception("Application instance certificate invalid!");

            }

            if (haveAppCertificate)
            {

                config.ApplicationUri = Utils.GetApplicationUriFromCertificate(config.SecurityConfiguration.ApplicationCertificate.Certificate);
                
                if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                {
                    // TODO - Fix this section. Should be passed into constructor
                    _autoAccept = true;

                }

                config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            
            }
            else
            {

                Debug.WriteLine("Missing application certificate, using unsecure connection.");

            }

            _exitCode = ExitCode.ErrorDiscoverEndpoints;

            var selectedEndpoint = CoreClientUtils.SelectEndpoint(_endpointURL, haveAppCertificate, 15000);

            _exitCode = ExitCode.ErrorCreateSession;

            var endpointConfiguration = EndpointConfiguration.Create(config);

            var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);

            return _session = await Session.Create(config, endpoint, false, _appName, 60000, new UserIdentity(new AnonymousIdentityToken()), null);

        }

        public async Task<OpcUaNodeList> Browse()
        {

            _exitCode = ExitCode.ErrorBrowseNamespace;
            ReferenceDescriptionCollection references;
            Byte[] continuationPoint;

            //Browse root nodes
            _session.Browse(
                null,
                null,
                ObjectIds.RootFolder, 
                0u,
                BrowseDirection.Forward,
                ReferenceTypeIds.HierarchicalReferences,
                true,
                (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method | (uint)NodeClass.DataType | (uint)NodeClass.ObjectType | (uint)NodeClass.ReferenceType | (uint)NodeClass.VariableType, 
                out continuationPoint,
                out references);

            // Obtain details of each node in the root and add it to the list
            foreach (var rd in references)
            {

                OpcUaNode item = new OpcUaNode();
                item.DisplayName = rd.DisplayName.ToString();
                item.BrowseName = rd.BrowseName.ToString();
                item.NodeClass = rd.NodeClass.ToString();
                item.NodeId = rd.NodeId.ToString();
                item.ReferenceTypeId = rd.ReferenceTypeId.ToString();
                item.TypeDefinition = rd.TypeDefinition.ToString();
                item.DataType = GetDataType(_session, ExpandedNodeId.ToNodeId(rd.NodeId, _session.NamespaceUris));
                item.Children = new ObservableCollection<OpcUaNode>();

                // Add top level folders to list
                _list.Add(item);

                // For each top level folder, browse the next level down
                BrowseNext(ExpandedNodeId.ToNodeId(rd.NodeId, _session.NamespaceUris), item);

            }

            return _list;
        }

        public void BrowseNext(NodeId nodeid, OpcUaNode item)
        {
            _exitCode = ExitCode.ErrorBrowseNamespace;
            ReferenceDescriptionCollection references;
            Byte[] continuationPoint;

            _session.Browse(
                null,
                null,
                nodeid,
                0u,
                BrowseDirection.Forward,
                ReferenceTypeIds.HierarchicalReferences, 
                true,
                (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method | (uint)NodeClass.DataType | (uint)NodeClass.ObjectType | (uint)NodeClass.ReferenceType | (uint)NodeClass.VariableType,
                out continuationPoint,
                out references);

            foreach (var rd in references)
            {

                try
                {
                    
                    OpcUaNode childItem = new OpcUaNode();
                    childItem.DisplayName = rd.DisplayName.ToString();
                    childItem.BrowseName = rd.BrowseName.ToString();
                    childItem.NodeClass = rd.NodeClass.ToString();
                    childItem.NodeId = rd.NodeId.ToString();
                    childItem.DataType = GetDataType(_session, ExpandedNodeId.ToNodeId(rd.NodeId, _session.NamespaceUris));
                    childItem.ReferenceTypeId = rd.ReferenceTypeId.ToString();
                    childItem.TypeDefinition = rd.TypeDefinition.ToString();
                    childItem.Children = new ObservableCollection<OpcUaNode>();

                    // Add it to the parent
                    item.Children.Add(childItem);

                    // Recursion
                    BrowseNext(ExpandedNodeId.ToNodeId(rd.NodeId, _session.NamespaceUris), childItem);

                }
                catch (Exception e)
                {
                    //Debug.WriteLine(e.Message);
                }
            }
        }

        // TODO - Remove this method and NodeReferenceModel.cs
        public List<NodeReferenceData> BrowseReferences(NodeId startId)
        {
            List<NodeReferenceData> nodeRefs = new List<NodeReferenceData>();

            if (NodeId.IsNull(startId))
            {
                return nodeRefs;
            }

            Browser browser = new Browser(_session);

            browser.BrowseDirection = BrowseDirection.Forward;
            browser.ContinueUntilDone = true;
            browser.ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences;

            foreach (ReferenceDescription reference in browser.Browse(startId))
            {
                Node target = _session.NodeCache.Find(reference.NodeId) as Node;

                if (target == null)
                {
                    continue;
                }

                ReferenceTypeNode referenceType = _session.NodeCache.Find(reference.ReferenceTypeId) as ReferenceTypeNode;

                Node typeDefinition = null;

                if ((target.NodeClass & (NodeClass.Variable | NodeClass.Method)) != 0)
                {
                    typeDefinition = _session.NodeCache.Find(reference.TypeDefinition) as Node;
                }
                else
                {
                    typeDefinition = _session.NodeCache.Find(_session.NodeCache.TypeTree.FindSuperType(target.NodeId)) as Node;
                }

                NodeReferenceData nodeRef = new NodeReferenceData
                {
                    ReferenceType = referenceType,
                    IsInverse = !reference.IsForward,
                    Target = target,
                    TypeDefinition = typeDefinition
                };

                if ((target.NodeClass & (NodeClass.Variable | NodeClass.Method)) != 0)
                {
                    nodeRefs.Add(nodeRef);
                }
            }

            return nodeRefs;
        }

        private string GetDataType(Session session, NodeId nodeId)
        {

            // Build list of attributes to read.
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            foreach (uint attributeId in new uint[] { Attributes.DataType, Attributes.ValueRank })
            {

                ReadValueId nodeToRead = new ReadValueId();
                nodeToRead.NodeId = nodeId;
                nodeToRead.AttributeId = attributeId;
                nodesToRead.Add(nodeToRead);

            }

            // Read the attributes.
            DataValueCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            session.Read(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            // This call checks for error and checks the data type of the value.
            // If an error or mismatch occurs the default value is returned.
            NodeId dataTypeId = results[0].GetValue<NodeId>(null);

            // Use the local type cache to look up the base type for the data type.
            BuiltInType builtInType = DataTypes.GetBuiltInType(dataTypeId, session.NodeCache.TypeTree);

            // The type info object is used in cast and compare functions.
            return builtInType.ToString();

        }

        private static void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {

            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {

                e.Accept = _autoAccept;

                if (_autoAccept)
                {

                    Debug.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);

                }
                else
                {

                    Debug.WriteLine("Rejected Certificate: {0}", e.Certificate.Subject);

                }

            }

        }

        private static void CreateSchemaMap()
        {

            // TODO See if there's a way to resolve names using the OPC UA SDK
            _map.Add("i=35", "Organizes");
            _map.Add("i=40", "HasTypeDefinition");
            _map.Add("i=46", "HasProperty");
            _map.Add("i=47", "HasComponent");
            _map.Add("i=61", "FolderType");
            _map.Add("i=62", "BaseVariableType");
            _map.Add("i=63", "BaseDataVariableType");
            _map.Add("i=68", "PropertyType");

        }

        public static string GetNameFromNodeId(string id)
        {

            try
            {

                return _map[id];

            }
            catch
            {

                return "Unknown";

            }

        }

    }
}
