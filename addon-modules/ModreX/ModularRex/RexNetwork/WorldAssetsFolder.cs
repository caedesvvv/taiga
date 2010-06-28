﻿using System;
using System.Collections.Generic;
using System.Text;
using OpenSim.Framework.Communications.Cache;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Interfaces;
using Nini.Config;
using ModularRex.RexParts;
using ModularRex.RexFramework;
using ModularRex.RexParts.Helpers;

namespace ModularRex.RexNetwork
{
    public class WorldAssetsFolder : InventoryFolderImpl, IRegionModule
    {
        private UUID libOwner = new UUID("11111111-1111-0000-0000-000100bba000");
        private InventoryFolderImpl m_WorldTexturesFolder;
        private InventoryFolderImpl m_World3DModelsFolder;
        private InventoryFolderImpl m_WorldMaterialScriptsFolder;
        private InventoryFolderImpl m_World3DModelAnimationsFolder;
        private InventoryFolderImpl m_WorldParticleScriptsFolder;
        private InventoryFolderImpl m_WorldSoundsFolder;
        private InventoryFolderImpl m_WorldFlashFolder;
        public List<InventoryFolderImpl> WorldFolders = new List<InventoryFolderImpl>();

        protected List<Scene> m_scenes = new List<Scene>();
        protected bool m_enabled = false;

        private static readonly log4net.ILog m_log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public WorldAssetsFolder() { }

        private void EventManager_OnRegisterCaps(UUID agentID, OpenSim.Framework.Capabilities.Caps caps)
        {
            caps.CAPSFetchInventoryDescendents = HandleFetchInventoryDescendentsCAPS;
        }

        private void EventManager_OnNewClient(IClientAPI client)
        {
            if (client.Scene is Scene)
            {
                Scene scene = (Scene)client.Scene;
                ScenePresence avatar = scene.GetScenePresence(client.AgentId);
                if (avatar != null)
                {
                    //Deattach the existing FetchInventoryDescendents Handler
                    //and attatch our own
                    avatar.ControllingClient.OnFetchInventoryDescendents -= scene.HandleFetchInventoryDescendents;
                    avatar.ControllingClient.OnFetchInventoryDescendents += ControllingClient_OnFetchInventoryDescendents;
                }
            }
        }

        private InventoryCollection HandleFetchInventoryDescendentsCAPS(UUID agentID, UUID folderID, UUID ownerID,
                                                   bool fetchFolders, bool fetchItems, int sortOrder, out int version)
        {
            //            m_log.DebugFormat(
            //                "[INVENTORY CACHE]: Fetching folders ({0}), items ({1}) from {2} for agent {3}",
            //                fetchFolders, fetchItems, folderID, agentID);

            // FIXME MAYBE: We're not handling sortOrder!

            InventoryFolderImpl fold;

            // rex
            if (folderID == this.ID)
            {
                version = 0;
                InventoryCollection ret = new InventoryCollection();
                ret.Folders = new List<InventoryFolderBase>();
                ret.Items = this.RequestListOfItems();
                return ret;
            }
            if ((fold = this.FindFolder(folderID)) != null)
            {
                version = 0;
                InventoryCollection ret = new InventoryCollection();
                ret.Folders = new List<InventoryFolderBase>();
                ret.Items = fold.RequestListOfItems();
                return ret;
            }
            // rex-end 
            
            if ((fold = m_scenes[0].CommsManager.UserProfileCacheService.LibraryRoot.FindFolder(folderID)) != null)
            {
                version = 0;
                InventoryCollection ret = new InventoryCollection();
                ret.Folders = new List<InventoryFolderBase>();
                ret.Items = fold.RequestListOfItems();

                return ret;
            }

            InventoryCollection contents = new InventoryCollection();

            if (folderID != UUID.Zero)
            {
                contents = m_scenes[0].InventoryService.GetFolderContent(agentID, folderID);
                InventoryFolderBase containingFolder = new InventoryFolderBase();
                containingFolder.ID = folderID;
                containingFolder.Owner = agentID;
                containingFolder = m_scenes[0].InventoryService.GetFolder(containingFolder);
                version = containingFolder.Version;
            }
            else
            {
                // Lost itemsm don't really need a version
                version = 1;
            }

            return contents;

            #region Old code. This is for reference for now. Remove when the method is properly tested and working.
            // TODO: This code for looking in the folder for the library should be folded back into the
            // CachedUserInfo so that this class doesn't have to know the details (and so that multiple libraries, etc.
            // can be handled transparently).            
            //InventoryFolderImpl fold;
            //version = 0; //TODO: Set the actual correct version somewhere
            //// rex
            
            //    if (folderID == this.ID)
            //    {
            //        return this.RequestListOfItems();//libraryRoot.RequestListOfItems();
            //    }
            //    if ((fold = this.FindFolder(folderID)) != null)
            //    {
            //        return fold.RequestListOfItems();
            //    }
            
            //// rex-end 


            //if ((fold = m_scenes[0].CommsManager.UserProfileCacheService.LibraryRoot.FindFolder(folderID)) != null)
            //{
            //    return fold.RequestListOfItems();
            //}

            //CachedUserInfo userProfile = m_scenes[0].CommsManager.UserProfileCacheService.GetUserDetails(agentID);

            //if (null == userProfile)
            //{
            //    m_log.ErrorFormat("[AGENT INVENTORY]: Could not find user profile for {0}", agentID);
            //    return null;
            //}

            //// XXX: When a client crosses into a scene, their entire inventory is fetched
            //// asynchronously.  If the client makes a request before the inventory is received, we need
            //// to give the inventory a chance to come in.
            ////
            //// This is a crude way of dealing with that by retrying the lookup.  It's not quite as bad
            //// in CAPS as doing this with the udp request, since here it won't hold up other packets.
            //// In fact, here we'll be generous and try for longer.
            //if (!userProfile.HasReceivedInventory)
            //{
            //    int attempts = 0;
            //    while (attempts++ < 30)
            //    {
            //        m_log.DebugFormat(
            //             "[INVENTORY CACHE]: Poll number {0} for inventory items in folder {1} for user {2}",
            //             attempts, folderID, agentID);

            //        System.Threading.Thread.Sleep(2000);

            //        if (userProfile.HasReceivedInventory)
            //        {
            //            break;
            //        }
            //    }
            //}

            //if (userProfile.HasReceivedInventory)
            //{
            //    if ((fold = userProfile.RootFolder.FindFolder(folderID)) != null)
            //    {
            //        return fold.RequestListOfItems();
            //    }
            //    else
            //    {
            //        m_log.WarnFormat(
            //            "[AGENT INVENTORY]: Could not find folder {0} requested by user {1}",
            //            folderID, agentID);
            //        return null;
            //    }
            //}
            //else
            //{
            //    m_log.ErrorFormat("[INVENTORY CACHE]: Could not find root folder for user {0}", agentID);
            //    return null;
            //}
            #endregion
        }

        private void ControllingClient_OnFetchInventoryDescendents(IClientAPI remoteClient, UUID folderID, UUID ownerID, bool fetchFolders, bool fetchItems, int sortOrder)
        {
            InventoryFolderImpl fold = null;
            Scene scene;
            if (remoteClient.Scene is Scene)
            {
                scene = (Scene)remoteClient.Scene;
            }
            else
            {
                scene = m_scenes[0];
            }

            if (folderID == this.ID)
            {
                remoteClient.SendInventoryFolderDetails(
                    //worldlibraryRoot.agentID, worldlibraryRoot.ID, worldlibraryRoot.RequestListOfItems(),
                    this.Owner, this.ID, this.RequestListOfItems(),
                    this.RequestListOfFolders(), this.Version, fetchFolders, fetchItems);
                return;
            }
            if ((fold = this.FindFolder(folderID)) != null)
            {
                this.UpdateWorldAssetFolders(scene);
                remoteClient.SendInventoryFolderDetails(
                    //worldlibraryRoot.agentID, folderID, fold.RequestListOfItems(),
                    this.Owner, folderID, fold.RequestListOfItems(),
                    fold.RequestListOfFolders(), this.Version, fetchFolders, fetchItems);

                return;
            }
            
            if ((fold = scene.CommsManager.UserProfileCacheService.LibraryRoot.FindFolder(folderID)) != null)
            {
                remoteClient.SendInventoryFolderDetails(
                    fold.Owner, folderID, fold.RequestListOfItems(),
                    fold.RequestListOfFolders(), this.Version, fetchFolders, fetchItems);
                return;
            }

            // We're going to send the reply async, because there may be
            // an enormous quantity of packets -- basically the entire inventory!
            // We don't want to block the client thread while all that is happening.
            SendInventoryDelegate d = SendInventoryAsync;
            d.BeginInvoke(remoteClient, folderID, ownerID, fetchFolders, fetchItems, sortOrder, SendInventoryComplete, d);
            #region Old code as reference
            //CachedUserInfo userProfile = scene.CommsManager.UserProfileCacheService.GetUserDetails(remoteClient.AgentId);

            //if (null == userProfile)
            //{
            //    m_log.ErrorFormat(
            //        "[AGENT INVENTORY]: Could not find user profile for {0} {1}",
            //        remoteClient.Name, remoteClient.AgentId);
            //    return;
            //}

            //userProfile.SendInventoryDecendents(remoteClient, folderID, this.Version, fetchFolders, fetchItems);
            #endregion
        }

        #region Async methods
        delegate void SendInventoryDelegate(IClientAPI remoteClient, UUID folderID, UUID ownerID, bool fetchFolders, bool fetchItems, int sortOrder);

        void SendInventoryAsync(IClientAPI remoteClient, UUID folderID, UUID ownerID, bool fetchFolders, bool fetchItems, int sortOrder)
        {
            SendInventoryUpdate(remoteClient, new InventoryFolderBase(folderID), fetchFolders, fetchItems);
        }

        void SendInventoryComplete(IAsyncResult iar)
        {
            SendInventoryDelegate d = (SendInventoryDelegate)iar.AsyncState;
            d.EndInvoke(iar);
        }
        #endregion

        private void SendInventoryUpdate(IClientAPI client, InventoryFolderBase folder, bool fetchFolders, bool fetchItems)
        {
            m_log.DebugFormat("[AGENT INVENTORY]: Send Inventory Folder {0} Update to {1} {2}", folder.Name, client.FirstName, client.LastName);
            InventoryCollection contents = m_scenes[0].InventoryService.GetFolderContent(client.AgentId, folder.ID);
            InventoryFolderBase containingFolder = new InventoryFolderBase();
            containingFolder.ID = folder.ID;
            containingFolder.Owner = client.AgentId;
            containingFolder = m_scenes[0].InventoryService.GetFolder(containingFolder);
            int version = containingFolder.Version;

            client.SendInventoryFolderDetails(client.AgentId, folder.ID, contents.Items, contents.Folders, version, fetchFolders, fetchItems);
        }

        public InventoryItemBase CreateItem(UUID inventoryID, UUID assetID, string name, string description,
                                            int assetType, int invType, UUID parentFolderID)
        {
            InventoryItemBase item = new InventoryItemBase();
            // item.avatarID = libOwner;
            item.Owner = libOwner;
            item.CreatorId = libOwner.ToString();
            //item.inventoryID = inventoryID;
            item.ID = inventoryID;
            item.AssetID = assetID;
            item.Description = description;
            item.Name = name;
            //item.inventoryName = name;
            item.AssetType = assetType;
            //item.assetType = assetType;
            item.InvType = invType;
            //item.invType = invType;
            //item.parentFolderID = parentFolderID;
            item.Folder = parentFolderID;

            //item.inventoryBasePermissions = 0x7FFFFFFF;
            item.BasePermissions = 0x7FFFFFFF;
            //item.inventoryEveryOnePermissions = 0x7FFFFFFF;
            item.EveryOnePermissions = 0x7FFFFFFF;

            //item.inventoryCurrentPermissions = 0x7FFFFFFF;
            item.CurrentPermissions = 0x7FFFFFFF;
            //item.inventoryNextPermissions = 0x7FFFFFFF;
            item.NextPermissions = 0x7FFFFFFF;
            return item;
        }


        public void UpdateWorldAssetFolders(Scene scene)
        {
            // Textures
            Dictionary<UUID, AssetBase> allTex = AssetsHelper.GetAssetList(scene, 0);
            m_WorldTexturesFolder.Purge();
            InventoryItemBase item;
            foreach (AssetBase asset in allTex.Values)
            {
                item = CreateItem(UUID.Random(), asset.FullID, asset.Name, asset.Description, (int)AssetType.Texture, (int)InventoryType.Texture, m_WorldTexturesFolder.ID);
                m_WorldTexturesFolder.Items.Add(item.ID, item);
            }

            // 3D Models
            Dictionary<UUID, AssetBase> allModels = AssetsHelper.GetAssetList(scene, 6);
            m_World3DModelsFolder.Purge();
            foreach (AssetBase asset in allModels.Values)
            {
                if (asset.Name != "Primitive")
                {
                    item = CreateItem(UUID.Random(), asset.FullID, asset.Name, asset.Description, 43, 6, m_World3DModelsFolder.ID);
                    m_World3DModelsFolder.Items.Add(item.ID, item);
                }
            }

            // Material scripts
            Dictionary<UUID, AssetBase> allMaterials = AssetsHelper.GetAssetList(scene, 45);
            m_WorldMaterialScriptsFolder.Purge();
            foreach (AssetBase asset in allMaterials.Values)
            {
                if (asset.Type == 45)
                {
                    item = CreateItem(UUID.Random(), asset.FullID, asset.Name, asset.Description, 45, 41, m_WorldMaterialScriptsFolder.ID);
                    m_WorldMaterialScriptsFolder.Items.Add(item.ID, item);
                }
            }

            // 3D Model animations
            Dictionary<UUID, AssetBase> allAnims = AssetsHelper.GetAssetList(scene, 19);
            m_World3DModelAnimationsFolder.Purge();
            foreach (AssetBase asset in allAnims.Values)
            {
                if (asset.Type == 44)
                {
                    item = CreateItem(UUID.Random(), asset.FullID, asset.Name, asset.Description, 44, 19, m_World3DModelAnimationsFolder.ID);
                    m_World3DModelAnimationsFolder.Items.Add(item.ID, item);
                }
            }

            // Particles
            Dictionary<UUID, AssetBase> allParticles = AssetsHelper.GetAssetList(scene, 41);
            m_WorldParticleScriptsFolder.Purge();
            foreach (AssetBase asset in allParticles.Values)
            {
                if (asset.Type == 47)
                {
                    item = CreateItem(UUID.Random(), asset.FullID, asset.Name, asset.Description, 47, 41, m_WorldParticleScriptsFolder.ID);
                    m_WorldParticleScriptsFolder.Items.Add(item.ID, item);
                }
            }

            // Sounds
            Dictionary<UUID, AssetBase> allSounds = AssetsHelper.GetAssetList(scene, 1);
            m_WorldSoundsFolder.Purge();
            foreach (AssetBase asset in allSounds.Values)
            {
                item = CreateItem(UUID.Random(), asset.FullID, asset.Name, asset.Description, (int)AssetType.Sound, (int)InventoryType.Sound, m_WorldSoundsFolder.ID);
                m_WorldSoundsFolder.Items.Add(item.ID, item);
            }

            // Flash anims
            Dictionary<UUID, AssetBase> allFlashs = AssetsHelper.GetAssetList(scene, 42);
            m_WorldFlashFolder.Purge();
            AssetBase ass = new AssetBase(UUID.Random(), "README", (sbyte)AssetType.Notecard);
            ass.Data = Utils.StringToBytes("Flash folder in World Library not in use with ModreX.");
            scene.AssetService.Store(ass);
            item = CreateItem(UUID.Random(), ass.FullID, ass.Name, ass.Description, (int)AssetType.Notecard, (int)InventoryType.Notecard, m_WorldFlashFolder.ID);
            m_WorldFlashFolder.Items.Add(item.ID, item);
            //foreach (AssetBase asset in allFlashs)
            //{
            //    if (asset.Type == 49)
            //    {
            //        item = CreateItem(UUID.Random(), asset.FullID, asset.Name, asset.Description, 49, 42, m_WorldFlashFolder.ID);
            //        m_WorldFlashFolder.Items.Add(item.ID, item);
            //    }
            //}
        }

        #region IRegionModule Members


        public void Close()
        {
            if (m_enabled)
            {
                foreach (Scene scene in m_scenes)
                {
                    scene.EventManager.OnNewClient -= EventManager_OnNewClient;
                    scene.EventManager.OnRegisterCaps -= EventManager_OnRegisterCaps;
                }
            }
        }

        public void Initialise(Scene scene, IConfigSource source)
        {
            if (m_scenes.Count == 0)
            {
                //Check that we are enabled and shit
                IConfig moduleConfig = source.Configs["realXtend"];
                if (moduleConfig != null)
                {
                    m_enabled = moduleConfig.GetBoolean("WorldLibrary", false);
                }

                if (m_enabled)
                {
                    addProperties();
                }
            }

            if (m_enabled)
            {
                scene.RegisterModuleInterface<WorldAssetsFolder>(this);
                m_scenes.Add(scene);
                scene.EventManager.OnNewClient += EventManager_OnNewClient;
                scene.EventManager.OnRegisterCaps += EventManager_OnRegisterCaps;
            }
        }

        public bool IsSharedModule
        {
            get { return true; }
        }

        public void PostInitialise()
        {
        }

        #endregion

        public override string Name
        {
            get
            {
                return "World Library";
            }
            set
            {
                ;
            }
        }

        private void addProperties()
        {
            Owner = libOwner;
            ID = new UUID("00000112-000f-0000-0000-000100bba005");
            this.Name = "World Library";
            this.ParentID = new UUID("00000112-000f-0000-0000-000100bba000");
            Type = (short)8;
            Version = (ushort)1;

            m_WorldTexturesFolder = CreateChildFolder(new UUID("00000112-000f-0000-0000-000100bba006"), "Textures", (ushort)8);

            m_World3DModelsFolder = CreateChildFolder(new UUID("00000112-000f-0000-0000-000100bba007"), "3D Models", (ushort)8);

            m_WorldMaterialScriptsFolder = CreateChildFolder(new UUID("00000112-000f-0000-0000-000100bba008"), "Material Scripts", (ushort)8);

            m_World3DModelAnimationsFolder = CreateChildFolder(new UUID("00000112-000f-0000-0000-000100bba009"), "3D Model Animations", (ushort)8);

            m_WorldParticleScriptsFolder = CreateChildFolder(new UUID("00000112-000f-0000-0000-000100bba010"), "Particle Scripts", (ushort)8);

            m_WorldSoundsFolder = CreateChildFolder(new UUID("00000112-000f-0000-0000-000100bba011"), "Sounds", (ushort)8);

            m_WorldFlashFolder = CreateChildFolder(new UUID("00000112-000f-0000-0000-000100bba012"), "Flash Animations", (ushort)8);

            WorldFolders.Add(m_WorldTexturesFolder);
            WorldFolders.Add(m_World3DModelsFolder);
            WorldFolders.Add(m_WorldMaterialScriptsFolder);
            WorldFolders.Add(m_World3DModelAnimationsFolder);
            WorldFolders.Add(m_WorldParticleScriptsFolder);
            WorldFolders.Add(m_WorldSoundsFolder);
            WorldFolders.Add(m_WorldFlashFolder);
            WorldFolders.Add(this);
        }
    }
}
