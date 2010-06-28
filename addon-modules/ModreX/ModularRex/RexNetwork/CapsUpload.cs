﻿using System;
using System.Collections.Generic;
using System.Text;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Framework;
using OpenSim.Framework.Capabilities;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenMetaverse;
using Caps = OpenSim.Framework.Capabilities.Caps;
using log4net;
using System.Reflection;
using OpenSim.Services.Interfaces;

namespace ModularRex.RexNetwork
{
    public class CapsUpload : IRegionModule
    {
        private Scene m_scene;
        private ICapabilitiesModule m_capsmodule;
        protected Dictionary<UUID, RexCaps> m_capsHandlers = new Dictionary<UUID, RexCaps>();

        #region IRegionModule Members

        public void Close()
        {
            m_scene.EventManager.OnRegisterCaps -= OnClientRegisterCaps;
        }

        public void Initialise(Scene scene, Nini.Config.IConfigSource source)
        {
            m_scene = scene;
        }

        public bool IsSharedModule
        {
            get { return false; }
        }

        public string Name
        {
            get { return "rex uploader module"; }
        }

        public void PostInitialise()
        {
            m_capsmodule = m_scene.RequestModuleInterface<ICapabilitiesModule>();
            m_scene.EventManager.OnRegisterCaps += OnClientRegisterCaps;
        }

        #endregion

        private void OnClientRegisterCaps(OpenMetaverse.UUID agentID, Caps caps)
        {
            RexCaps rexcaps = new RexCaps(
                m_scene.AssetService,
                MainServer.Instance,
                m_scene.RegionInfo.ExternalHostName,
                MainServer.Instance.Port,
                m_scene.DumpAssetsToFile);
            rexcaps.UUID = agentID;
            rexcaps.Caps = caps;
            rexcaps.GetClient = m_scene.SceneContents.GetControllingClient;

            rexcaps.OverloadHandlers();
            
            m_capsHandlers[agentID] = rexcaps;
        }
    }

    public class RexCaps
    {
        private static readonly string m_newInventory = "0002/";
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IHttpServer m_httpListener;
        private string m_httpListenerHostName;
        private uint m_httpListenPort;
        private bool m_dumpAssetsToFile;
        private IAssetService m_assetCache;
        private UUID m_agentID;

        public GetClientDelegate GetClient = null;

        public RexCaps(IAssetService assetCache, IHttpServer httpServer, string httpListen, uint httpPort, bool dumbAssetsToFile)
        {
            m_assetCache = assetCache;
            m_httpListener = httpServer;
            m_httpListenerHostName = httpListen;
            m_httpListenPort = httpPort;
            m_dumpAssetsToFile = dumbAssetsToFile;
        }

        public Caps Caps;
        public UUID UUID
        {
            get { return m_agentID; }
            set { m_agentID = value;}
        }

        public void OverloadHandlers()
        {
            string capsBase = "/CAPS/" + Caps.CapsObjectPath;
            Caps.CapsHandlers["NewFileAgentInventory"] =
                new LLSDStreamhandler<LLSDAssetUploadRequest, LLSDAssetUploadResponse>("POST",
                                                                                           capsBase + m_newInventory,
                                                                                           NewAgentInventoryRequest);
        }

        public LLSDAssetUploadResponse NewAgentInventoryRequest(LLSDAssetUploadRequest llsdRequest)
        {
            //m_log.Debug("[CAPS]: NewAgentInventoryRequest Request is: " + llsdRequest.ToString());
            //m_log.Debug("asset upload request via CAPS" + llsdRequest.inventory_type + " , " + llsdRequest.asset_type);

            IClientAPI client = null;
            if (GetClient != null)
            {
                client = GetClient(m_agentID);
            }

            #region Upload permissions
            if (client != null)
            {
                IUploadPermissions uploadPermissionModule = client.Scene.RequestModuleInterface<IUploadPermissions>();
                if (uploadPermissionModule != null)
                {
                    if (!uploadPermissionModule.CanUpload(m_agentID))
                    {
                        client.SendAgentAlertMessage("Unable to upload asset. Insufficient permissions.", false);

                        LLSDAssetUploadResponse errorResponse = new LLSDAssetUploadResponse();
                        errorResponse.uploader = "";
                        errorResponse.state = "error";
                        return errorResponse;
                    }
                }
            }
            #endregion

            #region Fancy money module stuff:
            if (llsdRequest.asset_type == "texture" ||
                llsdRequest.asset_type == "animation" ||
                llsdRequest.asset_type == "sound" ||
                llsdRequest.asset_type == "ogremesh" ||
                llsdRequest.asset_type == "flashani")
            {
                IScene scene = null;
                if (client != null)
                {
                    scene = client.Scene;

                    IMoneyModule mm = scene.RequestModuleInterface<IMoneyModule>();

                    if (mm != null)
                    {
                        if (!mm.UploadCovered(client))
                        {
                            if (client != null)
                                client.SendAgentAlertMessage("Unable to upload asset. Insufficient funds.", false);

                            LLSDAssetUploadResponse errorResponse = new LLSDAssetUploadResponse();
                            errorResponse.uploader = "";
                            errorResponse.state = "error";
                            return errorResponse;
                        }
                    }
                }
            }
            #endregion

            string assetName = llsdRequest.name;
            string assetDes = llsdRequest.description;
            string capsBase = "/CAPS/" + Caps.CapsObjectPath;
            UUID newAsset = UUID.Random();
            UUID newInvItem = UUID.Random();
            UUID parentFolder = llsdRequest.folder_id;
            string uploaderPath = Util.RandomClass.Next(5000, 8000).ToString("0000");

            Caps.AssetUploader uploader =
                new Caps.AssetUploader(assetName, assetDes, newAsset, newInvItem, parentFolder, llsdRequest.inventory_type,
                                  llsdRequest.asset_type, capsBase + uploaderPath, m_httpListener, m_dumpAssetsToFile);
            m_httpListener.AddStreamHandler(
                new BinaryStreamHandler("POST", capsBase + uploaderPath, uploader.uploaderCaps));

            string protocol = "http://";

            if (m_httpListener.UseSSL)
                protocol = "https://";

            string uploaderURL = protocol + m_httpListenerHostName + ":" + m_httpListenPort.ToString() + capsBase +
                                 uploaderPath;

            LLSDAssetUploadResponse uploadResponse = new LLSDAssetUploadResponse();
            uploadResponse.uploader = uploaderURL;
            uploadResponse.state = "upload";
            uploader.OnUpLoad += UploadCompleteHandler;
            return uploadResponse;
        }

        public void UploadCompleteHandler(string assetName, string assetDescription, UUID assetID,
                                          UUID inventoryItem, UUID parentFolder, byte[] data, string inventoryType,
                                          string assetType)
        {
            sbyte assType = 0;
            sbyte inType = 0;

            if (inventoryType == "texture")
            {
            }
            else if (inventoryType == "sound")
            {
                inType = 1;
                assType = 1;
            }
            else if (inventoryType == "animation")
            {
                inType = 19;
                assType = 20;
            }
            else if (inventoryType == "wearable")
            {
                inType = 18;
                switch (assetType)
                {
                    case "bodypart":
                        assType = 13;
                        break;
                    case "clothing":
                        assType = 5;
                        break;
                }
            }
            else
            {
                ParseAssetAndInventoryType(assetType, inventoryType, out assType, out inType);
                if (assType == 0 || inType == 0)
                {
                    m_log.WarnFormat("[REXCAPS]: Unknown inventory type {0}. Asset type ", inventoryType, assetType);
                }
            }

            AssetBase asset;
            asset = new AssetBase(assetID, assetName, assType);
            asset.Data = data;
            if (Caps.AddNewAsset != null)
                Caps.AddNewAsset(asset);
            else if (m_assetCache != null)
                m_assetCache.Store(asset);

            InventoryItemBase item = new InventoryItemBase();
            item.Owner = m_agentID;
            item.CreatorId = m_agentID.ToString();
            item.ID = inventoryItem;
            item.AssetID = asset.FullID;
            item.Description = assetDescription;
            item.Name = assetName;
            item.AssetType = assType;
            item.InvType = inType;
            item.Folder = parentFolder;
            item.CurrentPermissions = 2147483647;
            item.BasePermissions = 2147483647;
            item.EveryOnePermissions = 0;
            item.NextPermissions = 2147483647;
            item.CreationDate = Util.UnixTimeSinceEpoch();

            if (Caps.AddNewInventoryItem != null)
            {
                Caps.AddNewInventoryItem(m_agentID, item);
            }
        }

        private void ParseAssetAndInventoryType(string assetType, string inventoryType, out sbyte assType, out sbyte inType)
        {
            inType = 0;
            assType = 0;

            if (inventoryType == "sound")
            {
                inType = 1;
                assType = 1;
            }
            else if (inventoryType == "animation")
            {
                inType = 19;
                assType = 20;
            }

            if (assetType == "ogremesh")
            {
                inType = 6;
                assType = 43;
            }

            if (assetType == "ogreskel")
            {
                inType = 19;
                assType = 44;
            }

            if (assetType == "ogrepart")
            {
                inType = 41;
                assType = 47;
            }

            if (assetType == "ogremate")
            {
                inType = 41;
                assType = 45;
            }
            if (assetType == "flashani")
            {
                inType = 42;
                assType = 49;
            }
            if (assetType == "g.avatar")
            {
                assType = 46;
                inType = 18;
            }
        }
    }
}
