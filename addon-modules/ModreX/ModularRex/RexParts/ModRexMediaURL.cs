﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using ModularRex.RexFramework;
using ModularRex.NHibernate;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using log4net;
using ModularRex.RexNetwork;

namespace ModularRex.RexParts
{
    public class ModRexMediaURL : IRegionModule
    {

        private List<Scene> m_scenes = new List<Scene>();
        private string m_db_connectionstring;
        private NHibernateRexAssetData m_db;
        private Dictionary<UUID, RexAssetData> m_assets = new Dictionary<UUID, RexAssetData>();
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        #region IRegionModule Members

        public void Close()
        {
            //Force store on close
            foreach (KeyValuePair<UUID, RexAssetData> data in m_assets)
            {
                m_db.StoreObject(data.Value);
            }

            foreach (Scene s in m_scenes)
            {
                s.EventManager.OnNewClient -= OnNewClient;
            }
        }

        public void Initialise(Scene scene, IConfigSource source)
        {
            m_scenes.Add(scene);

            scene.EventManager.OnNewClient += OnNewClient;

            if (m_db == null)
            {
                m_db = new NHibernateRexAssetData();
            }

            string default_connection_string = "SQLiteDialect;SQLite20Driver;Data Source=RexObjects.db;Version=3";
            try
            {
                m_db_connectionstring = source.Configs["realXtend"].GetString("db_connectionstring", default_connection_string);
            }
            catch (Exception)
            {
                m_db_connectionstring = default_connection_string;
            }
        }

        public bool IsSharedModule
        {
            get { return true; }
        }

        public string Name
        {
            get { return "AssetMediaURLModule"; }
        }

        public void PostInitialise()
        {
            //Initialize the DB connection
            lock (m_db)
            {
                if (!m_db.Inizialized)
                {
                    m_db.Initialise(m_db_connectionstring);
                }
            }

            //Fetch all assets to "cache" on startup
            if (m_assets.Count == 0)
            {
                List<RexAssetData> objects = m_db.LoadAllObjects();
                foreach (RexAssetData rad in objects)
                {
                    m_assets.Add(rad.AssetID, rad);
                }
            }
        }

        #endregion

        #region Event handlers

        private void OnNewClient(IClientAPI client)
        {
            if (client is RexNetwork.IClientMediaURL)
            {
                ((RexNetwork.IClientMediaURL)client).OnReceiveRexMediaURL += OnReceiveRexMediaURL;
                client.OnLogout += OnRexClientLogout;

                foreach (KeyValuePair<UUID, RexAssetData> asset in m_assets)
                {
                    SendMediaURLtoUser(((RexNetwork.IClientMediaURL)client), asset.Key);
                }
            }
        }

        private void OnRexClientLogout(IClientAPI obj)
        {
            ((RexNetwork.IClientMediaURL)obj).OnReceiveRexMediaURL -= OnReceiveRexMediaURL;
        }

        private void OnReceiveRexMediaURL(IClientMediaURL remoteClient, UUID agentID, UUID itemID, string mediaURL, byte refreshRate)
        {
            //TODO: check priviledges

            SetAssetData(itemID, mediaURL, refreshRate);
            SendMediaURLtoAll(itemID);
        }

        #endregion

        public RexAssetData GetAssetData(UUID assetId)
        {
            RexAssetData data;
            if (!m_assets.TryGetValue(assetId, out data))
            {
                data = m_db.LoadObject(assetId);
                if (data == null)
                {
                    data = new RexAssetData(assetId);
                }
                m_assets.Add(assetId, data);
            }
            return data;
        }

        public void SetAssetData(UUID assetID, string mediaURL, byte refreshRate)
        {
            if (m_assets.ContainsKey(assetID))
            {
                m_assets[assetID].MediaURL = mediaURL;
                m_assets[assetID].RefreshRate = refreshRate;
            }
            else
            {
                RexAssetData data = new RexAssetData(assetID, mediaURL, refreshRate);
                m_assets.Add(assetID, data);
            }

            m_db.StoreObject(m_assets[assetID]);
        }

        public void SetAssetData(RexAssetData data)
        {
            if (m_assets[data.AssetID] != null)
            {
                m_log.InfoFormat("[REXASSET]: Replacing old RexAssetData {0}", data.AssetID);
            }
            m_assets[data.AssetID] = data;
            m_db.StoreObject(data);
        }

        public void SendMediaURLtoAll(UUID assetID)
        {
            foreach (Scene s in m_scenes)
            {
                s.ForEachScenePresence(
                    delegate(ScenePresence avatar)
                    {
                        RexNetwork.RexClientViewBase rex;
                        if (avatar.ClientView.TryGet(out rex))
                        {
                            SendMediaURLtoUser(rex, assetID);
                        }
                    });
            }
        }

        public void SendMediaURLtoUser(RexNetwork.IClientMediaURL user, UUID assetID)
        {
            if (m_assets[assetID] != null)
            {
                user.SendMediaURL(assetID, m_assets[assetID].MediaURL, m_assets[assetID].RefreshRate);
            }
            else
            {
                m_log.Warn("[MEDIAURL]: MediaURL was null.");
            }
        }
    }
}
