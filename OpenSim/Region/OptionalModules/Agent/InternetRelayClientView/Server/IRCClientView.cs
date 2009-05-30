﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using OpenMetaverse;
using OpenMetaverse.Packets;
using OpenSim.Framework;
using OpenSim.Framework.Client;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.OptionalModules.Agent.InternetRelayClientView.Server
{
    class IRCClientView : IClientAPI, IClientCore, IClientIPEndpoint
    {
        private readonly TcpClient m_client;
        private readonly Scene m_scene;

        private string m_username;

        private bool m_hasNick = false;
        private bool m_hasUser = false;

        public IRCClientView(TcpClient client, Scene scene)
        {
            m_client = client;
            m_scene = scene;
            
            Thread loopThread = new Thread(InternalLoop);
            loopThread.Start();
        }

        private void SendCommand(string command)
        {
            lock(m_client)
            {
                byte[] buf = Encoding.UTF8.GetBytes(command + "\r\n");

                m_client.GetStream().Write(buf, 0, buf.Length);
            }
        }

        private string IrcRegionName
        {
            // I know &Channel is more technically correct, but people are used to seeing #Channel
            // Dont shoot me!
            get { return "#" + m_scene.RegionInfo.RegionName.Replace(" ", "-"); }
        }

        private void InternalLoop()
        {
            string strbuf = "";

            while(true)
            {
                string line;
                byte[] buf = new byte[520]; // RFC1459 defines max message size as 512.

                lock (m_client)
                {
                    int count = m_client.GetStream().Read(buf, 0, buf.Length);
                    line = Encoding.UTF8.GetString(buf, 0, count);
                }

                strbuf += line;

                string message = ExtractMessage(strbuf);
                if(message != null)
                {
                    // Remove from buffer
                    strbuf = strbuf.Remove(0, message.Length);

                    // Extract command sequence
                    string command = ExtractCommand(message);
                    ProcessInMessage(message, command);
                }

                Thread.Sleep(0);
            }
        }

        private void ProcessInMessage(string message, string command)
        {
            if(command != null)
            {
                switch(command)
                {
                    case "ADMIN":
                    case "AWAY":
                    case "CONNECT":
                    case "DIE":
                    case "ERROR":
                    case "INFO":
                    case "INVITE":
                    case "ISON":
                    case "KICK":
                    case "KILL":
                    case "LINKS":
                    case "LUSERS":
                    case "MODE":
                    case "OPER":
                    case "PART":
                    case "REHASH":
                    case "SERVICE":
                    case "SERVLIST":
                    case "SERVER":
                    case "SQUERY":
                    case "SQUIT":
                    case "STATS":
                    case "SUMMON":
                    case "TIME":
                    case "TRACE":
                    case "USERHOST":
                    case "VERSION":
                    case "WALLOPS":
                    case "WHOIS":
                    case "WHOWAS":
                        SendCommand("421 ERR_UNKNOWNCOMMAND \"" + command + " :Command unimplemented\"");
                        break;

                    // Connection Commands
                    case "PASS":
                        break; // Ignore for now. I want to implement authentication later however.

                    case "JOIN":
                        break;

                    case "USER":
                        IRC_ProcessUser(message);
                        IRC_SendReplyJoin();

                        break;
                    case "NICK":
                        IRC_ProcessNick(message);
                        IRC_SendReplyJoin();

                        break;
                    case "TOPIC":
                        IRC_SendReplyTopic();
                        break;
                    case "USERS":
                        IRC_SendReplyUsers();
                        break;

                    case "LIST":
                        break; // TODO

                    case "MOTD":
                        IRC_SendMOTD();
                        break;

                    case "NOTICE": // TODO
                    case "WHO": // TODO
                        break;

                    case "PING":
                        IRC_ProcessPing(message);
                        break;

                    // Special case, ignore this completely.
                    case "PONG":
                        break;

                    case "QUIT":
                        if (OnDisconnectUser != null)
                            OnDisconnectUser();
                        break;

                    case "NAMES":
                        IRC_SendNamesReply();
                        break;
                    case "PRIVMSG":
                        IRC_ProcessPrivmsg(message);
                        break;

                    default:
                        SendCommand("421 ERR_UNKNOWNCOMMAND \"" + command + " :Unknown command\"");
                        break;
                }
            }
        }

        private void IRC_SendReplyJoin()
        {
            if (m_hasUser && m_hasNick)
            {
                IRC_SendReplyTopic();
                IRC_SendNamesReply();
            }
        }

        private void IRC_ProcessUser(string message)
        {
            string[] userArgs = ExtractParameters(message);
            string username = userArgs[0];
            string hostname = userArgs[1];
            string servername = userArgs[2];
            string realname = userArgs[3];

            m_username = realname;
            m_hasUser = true;
        }

        private void IRC_ProcessNick(string message)
        {
            string[] nickArgs = ExtractParameters(message);
            string nickname = nickArgs[0];
            m_hasNick = true;
        }

        private void IRC_ProcessPing(string message)
        {
            string[] pingArgs = ExtractParameters(message);
            string pingHost = pingArgs[0];
            SendCommand("PONG " + pingHost);
        }

        private void IRC_ProcessPrivmsg(string message)
        {
            string[] privmsgArgs = ExtractParameters(message);
            if (privmsgArgs[0] == IrcRegionName)
            {
                if (OnChatFromClient != null)
                {
                    OSChatMessage msg = new OSChatMessage();
                    msg.Sender = this;
                    msg.Channel = 0;
                    msg.From = this.Name;
                    msg.Message = privmsgArgs[1];
                    msg.Position = Vector3.Zero;
                    msg.Scene = m_scene;
                    msg.SenderObject = null;
                    msg.SenderUUID = this.AgentId;
                    msg.Type = ChatTypeEnum.Broadcast;

                    OnChatFromClient(this, msg);
                }
            }
            else
            {
                // Handle as an IM, later.
            }
        }

        private void IRC_SendNamesReply()
        {
            List<EntityBase> users = m_scene.Entities.GetAllByType<ScenePresence>();

            foreach (EntityBase user in users)
            {
                SendCommand("353 RPL_NAMREPLY \"" + IrcRegionName + " :+" + user.Name.Replace(" ", ""));
            }
            SendCommand("366 RPL_ENDOFNAMES \"" + IrcRegionName + " :End of /NAMES list\"");
        }

        private void IRC_SendMOTD()
        {
            SendCommand("375 RPL_MOTDSTART \":- OpenSimulator Message of the day -");
            SendCommand("372 RPL_MOTD \":- Hiya!");
            SendCommand("376 RPL_ENDOFMOTD \":End of /MOTD command\"");
        }

        private void IRC_SendReplyTopic()
        {
            SendCommand("332 RPL_TOPIC \"" + IrcRegionName + " :OpenSimulator IRC Server\"");
        }

        private void IRC_SendReplyUsers()
        {
            List<EntityBase> users = m_scene.Entities.GetAllByType<ScenePresence>();
                        
            SendCommand("392 RPL_USERSSTART \":UserID   Terminal  Host\"");
            foreach (EntityBase user in users)
            {
                char[] nom = new char[8];
                char[] term = "terminal_".ToCharArray();
                char[] host = "hostname".ToCharArray();

                string userName = user.Name.Replace(" ","");
                for (int i = 0; i < nom.Length; i++)
                {
                    if (userName.Length < i)
                        nom[i] = userName[i];
                    else
                        nom[i] = ' ';
                }

                SendCommand("393 RPL_USERS \":" + nom + " " + term + " " + host + "\"");
            }

            SendCommand("394 RPL_ENDOFUSERS \":End of users\"");
        }

        private static string ExtractMessage(string buffer)
        {
            int pos = buffer.IndexOf("\r\n");

            if (pos == -1)
                return null;

            string command = buffer.Substring(0, pos + 1);

            return command;
        }

        private static string ExtractCommand(string msg)
        {
            string[] msgs = msg.Split(' ');

            if(msgs.Length < 2)
                return null;

            if (msgs[0].StartsWith(":"))
                return msgs[1];

            return msgs[0];
        }

        private static string[] ExtractParameters(string msg)
        {
            string[] msgs = msg.Split(' ');
            List<string> parms = new List<string>(msgs.Length);

            bool foundCommand = false;
            string command = ExtractCommand(msg);


            for(int i=0;i<msgs.Length;i++)
            {
                if(msgs[i] == command)
                {
                    foundCommand = true;
                    continue;
                }

                if(foundCommand != true)
                    continue;

                if(i != 0 && msgs[i].StartsWith(":"))
                {
                    List<string> tmp = new List<string>();
                    for(int j=i;j<msgs.Length;j++)
                    {
                        tmp.Add(msgs[j]);
                    }
                    parms.Add(string.Join(" ", tmp.ToArray()));
                    break;
                }

                parms.Add(msgs[i]);
            }

            return parms.ToArray();
        }

        #region Implementation of IClientAPI

        public Vector3 StartPos
        {
            get { throw new System.NotImplementedException(); }
            set { throw new System.NotImplementedException(); }
        }

        public bool TryGet<T>(out T iface)
        {
            throw new System.NotImplementedException();
        }

        public T Get<T>()
        {
            throw new System.NotImplementedException();
        }

        public UUID AgentId
        {
            get { return UUID.Zero; }
        }

        public void Disconnect(string reason)
        {
            throw new System.NotImplementedException();
        }

        public void Disconnect()
        {
            throw new System.NotImplementedException();
        }

        public UUID SessionId
        {
            get { throw new System.NotImplementedException(); }
        }

        public UUID SecureSessionId
        {
            get { throw new System.NotImplementedException(); }
        }

        public UUID ActiveGroupId
        {
            get { throw new System.NotImplementedException(); }
        }

        public string ActiveGroupName
        {
            get { throw new System.NotImplementedException(); }
        }

        public ulong ActiveGroupPowers
        {
            get { throw new System.NotImplementedException(); }
        }

        public ulong GetGroupPowers(UUID groupID)
        {
            throw new System.NotImplementedException();
        }

        public bool IsGroupMember(UUID GroupID)
        {
            throw new System.NotImplementedException();
        }

        public string FirstName
        {
            get { throw new System.NotImplementedException(); }
        }

        public string LastName
        {
            get { throw new System.NotImplementedException(); }
        }

        public IScene Scene
        {
            get { throw new System.NotImplementedException(); }
        }

        public int NextAnimationSequenceNumber
        {
            get { throw new System.NotImplementedException(); }
        }

        public string Name
        {
            get { throw new System.NotImplementedException(); }
        }

        public bool IsActive
        {
            get { throw new System.NotImplementedException(); }
            set { throw new System.NotImplementedException(); }
        }

        public bool SendLogoutPacketWhenClosing
        {
            set { throw new System.NotImplementedException(); }
        }

        public uint CircuitCode
        {
            get { throw new System.NotImplementedException(); }
        }

        public event GenericMessage OnGenericMessage;
        public event ImprovedInstantMessage OnInstantMessage;
        public event ChatMessage OnChatFromClient;
        public event TextureRequest OnRequestTexture;
        public event RezObject OnRezObject;
        public event ModifyTerrain OnModifyTerrain;
        public event BakeTerrain OnBakeTerrain;
        public event EstateChangeInfo OnEstateChangeInfo;
        public event SetAppearance OnSetAppearance;
        public event AvatarNowWearing OnAvatarNowWearing;
        public event RezSingleAttachmentFromInv OnRezSingleAttachmentFromInv;
        public event RezMultipleAttachmentsFromInv OnRezMultipleAttachmentsFromInv;
        public event UUIDNameRequest OnDetachAttachmentIntoInv;
        public event ObjectAttach OnObjectAttach;
        public event ObjectDeselect OnObjectDetach;
        public event ObjectDrop OnObjectDrop;
        public event StartAnim OnStartAnim;
        public event StopAnim OnStopAnim;
        public event LinkObjects OnLinkObjects;
        public event DelinkObjects OnDelinkObjects;
        public event RequestMapBlocks OnRequestMapBlocks;
        public event RequestMapName OnMapNameRequest;
        public event TeleportLocationRequest OnTeleportLocationRequest;
        public event DisconnectUser OnDisconnectUser;
        public event RequestAvatarProperties OnRequestAvatarProperties;
        public event SetAlwaysRun OnSetAlwaysRun;
        public event TeleportLandmarkRequest OnTeleportLandmarkRequest;
        public event DeRezObject OnDeRezObject;
        public event Action<IClientAPI> OnRegionHandShakeReply;
        public event GenericCall2 OnRequestWearables;
        public event GenericCall2 OnCompleteMovementToRegion;
        public event UpdateAgent OnAgentUpdate;
        public event AgentRequestSit OnAgentRequestSit;
        public event AgentSit OnAgentSit;
        public event AvatarPickerRequest OnAvatarPickerRequest;
        public event Action<IClientAPI> OnRequestAvatarsData;
        public event AddNewPrim OnAddPrim;
        public event FetchInventory OnAgentDataUpdateRequest;
        public event TeleportLocationRequest OnSetStartLocationRequest;
        public event RequestGodlikePowers OnRequestGodlikePowers;
        public event GodKickUser OnGodKickUser;
        public event ObjectDuplicate OnObjectDuplicate;
        public event ObjectDuplicateOnRay OnObjectDuplicateOnRay;
        public event GrabObject OnGrabObject;
        public event ObjectSelect OnDeGrabObject;
        public event MoveObject OnGrabUpdate;
        public event SpinStart OnSpinStart;
        public event SpinObject OnSpinUpdate;
        public event SpinStop OnSpinStop;
        public event UpdateShape OnUpdatePrimShape;
        public event ObjectExtraParams OnUpdateExtraParams;
        public event ObjectSelect OnObjectSelect;
        public event ObjectDeselect OnObjectDeselect;
        public event GenericCall7 OnObjectDescription;
        public event GenericCall7 OnObjectName;
        public event GenericCall7 OnObjectClickAction;
        public event GenericCall7 OnObjectMaterial;
        public event RequestObjectPropertiesFamily OnRequestObjectPropertiesFamily;
        public event UpdatePrimFlags OnUpdatePrimFlags;
        public event UpdatePrimTexture OnUpdatePrimTexture;
        public event UpdateVector OnUpdatePrimGroupPosition;
        public event UpdateVector OnUpdatePrimSinglePosition;
        public event UpdatePrimRotation OnUpdatePrimGroupRotation;
        public event UpdatePrimSingleRotation OnUpdatePrimSingleRotation;
        public event UpdatePrimGroupRotation OnUpdatePrimGroupMouseRotation;
        public event UpdateVector OnUpdatePrimScale;
        public event UpdateVector OnUpdatePrimGroupScale;
        public event StatusChange OnChildAgentStatus;
        public event GenericCall2 OnStopMovement;
        public event Action<UUID> OnRemoveAvatar;
        public event ObjectPermissions OnObjectPermissions;
        public event CreateNewInventoryItem OnCreateNewInventoryItem;
        public event CreateInventoryFolder OnCreateNewInventoryFolder;
        public event UpdateInventoryFolder OnUpdateInventoryFolder;
        public event MoveInventoryFolder OnMoveInventoryFolder;
        public event FetchInventoryDescendents OnFetchInventoryDescendents;
        public event PurgeInventoryDescendents OnPurgeInventoryDescendents;
        public event FetchInventory OnFetchInventory;
        public event RequestTaskInventory OnRequestTaskInventory;
        public event UpdateInventoryItem OnUpdateInventoryItem;
        public event CopyInventoryItem OnCopyInventoryItem;
        public event MoveInventoryItem OnMoveInventoryItem;
        public event RemoveInventoryFolder OnRemoveInventoryFolder;
        public event RemoveInventoryItem OnRemoveInventoryItem;
        public event UDPAssetUploadRequest OnAssetUploadRequest;
        public event XferReceive OnXferReceive;
        public event RequestXfer OnRequestXfer;
        public event ConfirmXfer OnConfirmXfer;
        public event AbortXfer OnAbortXfer;
        public event RezScript OnRezScript;
        public event UpdateTaskInventory OnUpdateTaskInventory;
        public event MoveTaskInventory OnMoveTaskItem;
        public event RemoveTaskInventory OnRemoveTaskItem;
        public event RequestAsset OnRequestAsset;
        public event UUIDNameRequest OnNameFromUUIDRequest;
        public event ParcelAccessListRequest OnParcelAccessListRequest;
        public event ParcelAccessListUpdateRequest OnParcelAccessListUpdateRequest;
        public event ParcelPropertiesRequest OnParcelPropertiesRequest;
        public event ParcelDivideRequest OnParcelDivideRequest;
        public event ParcelJoinRequest OnParcelJoinRequest;
        public event ParcelPropertiesUpdateRequest OnParcelPropertiesUpdateRequest;
        public event ParcelSelectObjects OnParcelSelectObjects;
        public event ParcelObjectOwnerRequest OnParcelObjectOwnerRequest;
        public event ParcelAbandonRequest OnParcelAbandonRequest;
        public event ParcelGodForceOwner OnParcelGodForceOwner;
        public event ParcelReclaim OnParcelReclaim;
        public event ParcelReturnObjectsRequest OnParcelReturnObjectsRequest;
        public event ParcelDeedToGroup OnParcelDeedToGroup;
        public event RegionInfoRequest OnRegionInfoRequest;
        public event EstateCovenantRequest OnEstateCovenantRequest;
        public event FriendActionDelegate OnApproveFriendRequest;
        public event FriendActionDelegate OnDenyFriendRequest;
        public event FriendshipTermination OnTerminateFriendship;
        public event MoneyTransferRequest OnMoneyTransferRequest;
        public event EconomyDataRequest OnEconomyDataRequest;
        public event MoneyBalanceRequest OnMoneyBalanceRequest;
        public event UpdateAvatarProperties OnUpdateAvatarProperties;
        public event ParcelBuy OnParcelBuy;
        public event RequestPayPrice OnRequestPayPrice;
        public event ObjectSaleInfo OnObjectSaleInfo;
        public event ObjectBuy OnObjectBuy;
        public event BuyObjectInventory OnBuyObjectInventory;
        public event RequestTerrain OnRequestTerrain;
        public event RequestTerrain OnUploadTerrain;
        public event ObjectIncludeInSearch OnObjectIncludeInSearch;
        public event UUIDNameRequest OnTeleportHomeRequest;
        public event ScriptAnswer OnScriptAnswer;
        public event AgentSit OnUndo;
        public event ForceReleaseControls OnForceReleaseControls;
        public event GodLandStatRequest OnLandStatRequest;
        public event DetailedEstateDataRequest OnDetailedEstateDataRequest;
        public event SetEstateFlagsRequest OnSetEstateFlagsRequest;
        public event SetEstateTerrainBaseTexture OnSetEstateTerrainBaseTexture;
        public event SetEstateTerrainDetailTexture OnSetEstateTerrainDetailTexture;
        public event SetEstateTerrainTextureHeights OnSetEstateTerrainTextureHeights;
        public event CommitEstateTerrainTextureRequest OnCommitEstateTerrainTextureRequest;
        public event SetRegionTerrainSettings OnSetRegionTerrainSettings;
        public event EstateRestartSimRequest OnEstateRestartSimRequest;
        public event EstateChangeCovenantRequest OnEstateChangeCovenantRequest;
        public event UpdateEstateAccessDeltaRequest OnUpdateEstateAccessDeltaRequest;
        public event SimulatorBlueBoxMessageRequest OnSimulatorBlueBoxMessageRequest;
        public event EstateBlueBoxMessageRequest OnEstateBlueBoxMessageRequest;
        public event EstateDebugRegionRequest OnEstateDebugRegionRequest;
        public event EstateTeleportOneUserHomeRequest OnEstateTeleportOneUserHomeRequest;
        public event EstateTeleportAllUsersHomeRequest OnEstateTeleportAllUsersHomeRequest;
        public event UUIDNameRequest OnUUIDGroupNameRequest;
        public event RegionHandleRequest OnRegionHandleRequest;
        public event ParcelInfoRequest OnParcelInfoRequest;
        public event RequestObjectPropertiesFamily OnObjectGroupRequest;
        public event ScriptReset OnScriptReset;
        public event GetScriptRunning OnGetScriptRunning;
        public event SetScriptRunning OnSetScriptRunning;
        public event UpdateVector OnAutoPilotGo;
        public event TerrainUnacked OnUnackedTerrain;
        public event ActivateGesture OnActivateGesture;
        public event DeactivateGesture OnDeactivateGesture;
        public event ObjectOwner OnObjectOwner;
        public event DirPlacesQuery OnDirPlacesQuery;
        public event DirFindQuery OnDirFindQuery;
        public event DirLandQuery OnDirLandQuery;
        public event DirPopularQuery OnDirPopularQuery;
        public event DirClassifiedQuery OnDirClassifiedQuery;
        public event EventInfoRequest OnEventInfoRequest;
        public event ParcelSetOtherCleanTime OnParcelSetOtherCleanTime;
        public event MapItemRequest OnMapItemRequest;
        public event OfferCallingCard OnOfferCallingCard;
        public event AcceptCallingCard OnAcceptCallingCard;
        public event DeclineCallingCard OnDeclineCallingCard;
        public event SoundTrigger OnSoundTrigger;
        public event StartLure OnStartLure;
        public event TeleportLureRequest OnTeleportLureRequest;
        public event NetworkStats OnNetworkStatsUpdate;
        public event ClassifiedInfoRequest OnClassifiedInfoRequest;
        public event ClassifiedInfoUpdate OnClassifiedInfoUpdate;
        public event ClassifiedDelete OnClassifiedDelete;
        public event ClassifiedDelete OnClassifiedGodDelete;
        public event EventNotificationAddRequest OnEventNotificationAddRequest;
        public event EventNotificationRemoveRequest OnEventNotificationRemoveRequest;
        public event EventGodDelete OnEventGodDelete;
        public event ParcelDwellRequest OnParcelDwellRequest;
        public event UserInfoRequest OnUserInfoRequest;
        public event UpdateUserInfo OnUpdateUserInfo;
        public event RetrieveInstantMessages OnRetrieveInstantMessages;
        public event PickDelete OnPickDelete;
        public event PickGodDelete OnPickGodDelete;
        public event PickInfoUpdate OnPickInfoUpdate;
        public event AvatarNotesUpdate OnAvatarNotesUpdate;
        public event MuteListRequest OnMuteListRequest;
        public event PlacesQuery OnPlacesQuery;
        public void SetDebugPacketLevel(int newDebug)
        {
            throw new System.NotImplementedException();
        }

        public void InPacket(object NewPack)
        {
            throw new System.NotImplementedException();
        }

        public void ProcessInPacket(Packet NewPack)
        {
            throw new System.NotImplementedException();
        }

        public void Close(bool ShutdownCircuit)
        {
            throw new System.NotImplementedException();
        }

        public void Kick(string message)
        {
            throw new System.NotImplementedException();
        }

        public void Start()
        {
            throw new System.NotImplementedException();
        }

        public void Stop()
        {
            throw new System.NotImplementedException();
        }

        public void SendWearables(AvatarWearable[] wearables, int serial)
        {
            throw new System.NotImplementedException();
        }

        public void SendAppearance(UUID agentID, byte[] visualParams, byte[] textureEntry)
        {
            throw new System.NotImplementedException();
        }

        public void SendStartPingCheck(byte seq)
        {
            throw new System.NotImplementedException();
        }

        public void SendKillObject(ulong regionHandle, uint localID)
        {
            throw new System.NotImplementedException();
        }

        public void SendAnimations(UUID[] animID, int[] seqs, UUID sourceAgentId, UUID[] objectIDs)
        {
            throw new System.NotImplementedException();
        }

        public void SendRegionHandshake(RegionInfo regionInfo, RegionHandshakeArgs args)
        {
            throw new System.NotImplementedException();
        }

        public void SendChatMessage(string message, byte type, Vector3 fromPos, string fromName, UUID fromAgentID, byte source, byte audible)
        {
            throw new System.NotImplementedException();
        }

        public void SendInstantMessage(GridInstantMessage im)
        {
            throw new System.NotImplementedException();
        }

        public void SendGenericMessage(string method, List<string> message)
        {
            throw new System.NotImplementedException();
        }

        public void SendLayerData(float[] map)
        {
            throw new System.NotImplementedException();
        }

        public void SendLayerData(int px, int py, float[] map)
        {
            throw new System.NotImplementedException();
        }

        public void SendWindData(Vector2[] windSpeeds)
        {
            throw new System.NotImplementedException();
        }

        public void SendCloudData(float[] cloudCover)
        {
            throw new System.NotImplementedException();
        }

        public void MoveAgentIntoRegion(RegionInfo regInfo, Vector3 pos, Vector3 look)
        {
            throw new System.NotImplementedException();
        }

        public void InformClientOfNeighbour(ulong neighbourHandle, IPEndPoint neighbourExternalEndPoint)
        {
            throw new System.NotImplementedException();
        }

        public AgentCircuitData RequestClientInfo()
        {
            throw new System.NotImplementedException();
        }

        public void CrossRegion(ulong newRegionHandle, Vector3 pos, Vector3 lookAt, IPEndPoint newRegionExternalEndPoint, string capsURL)
        {
            throw new System.NotImplementedException();
        }

        public void SendMapBlock(List<MapBlockData> mapBlocks, uint flag)
        {
            throw new System.NotImplementedException();
        }

        public void SendLocalTeleport(Vector3 position, Vector3 lookAt, uint flags)
        {
            throw new System.NotImplementedException();
        }

        public void SendRegionTeleport(ulong regionHandle, byte simAccess, IPEndPoint regionExternalEndPoint, uint locationID, uint flags, string capsURL)
        {
            throw new System.NotImplementedException();
        }

        public void SendTeleportFailed(string reason)
        {
            throw new System.NotImplementedException();
        }

        public void SendTeleportLocationStart()
        {
            throw new System.NotImplementedException();
        }

        public void SendMoneyBalance(UUID transaction, bool success, byte[] description, int balance)
        {
            throw new System.NotImplementedException();
        }

        public void SendPayPrice(UUID objectID, int[] payPrice)
        {
            throw new System.NotImplementedException();
        }

        public void SendAvatarData(ulong regionHandle, string firstName, string lastName, string grouptitle, UUID avatarID, uint avatarLocalID, Vector3 Pos, byte[] textureEntry, uint parentID, Quaternion rotation)
        {
            throw new System.NotImplementedException();
        }

        public void SendAvatarTerseUpdate(ulong regionHandle, ushort timeDilation, uint localID, Vector3 position, Vector3 velocity, Quaternion rotation, UUID agentid)
        {
            throw new System.NotImplementedException();
        }

        public void SendCoarseLocationUpdate(List<UUID> users, List<Vector3> CoarseLocations)
        {
            throw new System.NotImplementedException();
        }

        public void AttachObject(uint localID, Quaternion rotation, byte attachPoint, UUID ownerID)
        {
            throw new System.NotImplementedException();
        }

        public void SetChildAgentThrottle(byte[] throttle)
        {
            throw new System.NotImplementedException();
        }

        public void SendPrimitiveToClient(ulong regionHandle, ushort timeDilation, uint localID, PrimitiveBaseShape primShape, Vector3 pos, Vector3 vel, Vector3 acc, Quaternion rotation, Vector3 rvel, uint flags, UUID objectID, UUID ownerID, string text, byte[] color, uint parentID, byte[] particleSystem, byte clickAction, byte material, byte[] textureanim, bool attachment, uint AttachPoint, UUID AssetId, UUID SoundId, double SoundVolume, byte SoundFlags, double SoundRadius)
        {
            throw new System.NotImplementedException();
        }

        public void SendPrimitiveToClient(ulong regionHandle, ushort timeDilation, uint localID, PrimitiveBaseShape primShape, Vector3 pos, Vector3 vel, Vector3 acc, Quaternion rotation, Vector3 rvel, uint flags, UUID objectID, UUID ownerID, string text, byte[] color, uint parentID, byte[] particleSystem, byte clickAction, byte material)
        {
            throw new System.NotImplementedException();
        }

        public void SendPrimTerseUpdate(ulong regionHandle, ushort timeDilation, uint localID, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 rotationalvelocity, byte state, UUID AssetId, UUID owner, int attachPoint)
        {
            throw new System.NotImplementedException();
        }

        public void SendInventoryFolderDetails(UUID ownerID, UUID folderID, List<InventoryItemBase> items, List<InventoryFolderBase> folders, bool fetchFolders, bool fetchItems)
        {
            throw new System.NotImplementedException();
        }

        public void FlushPrimUpdates()
        {
            throw new System.NotImplementedException();
        }

        public void SendInventoryItemDetails(UUID ownerID, InventoryItemBase item)
        {
            throw new System.NotImplementedException();
        }

        public void SendInventoryItemCreateUpdate(InventoryItemBase Item, uint callbackId)
        {
            throw new System.NotImplementedException();
        }

        public void SendRemoveInventoryItem(UUID itemID)
        {
            throw new System.NotImplementedException();
        }

        public void SendTakeControls(int controls, bool passToAgent, bool TakeControls)
        {
            throw new System.NotImplementedException();
        }

        public void SendTaskInventory(UUID taskID, short serial, byte[] fileName)
        {
            throw new System.NotImplementedException();
        }

        public void SendBulkUpdateInventory(InventoryNodeBase node)
        {
            throw new System.NotImplementedException();
        }

        public void SendXferPacket(ulong xferID, uint packet, byte[] data)
        {
            throw new System.NotImplementedException();
        }

        public void SendEconomyData(float EnergyEfficiency, int ObjectCapacity, int ObjectCount, int PriceEnergyUnit, int PriceGroupCreate, int PriceObjectClaim, float PriceObjectRent, float PriceObjectScaleFactor, int PriceParcelClaim, float PriceParcelClaimFactor, int PriceParcelRent, int PricePublicObjectDecay, int PricePublicObjectDelete, int PriceRentLight, int PriceUpload, int TeleportMinPrice, float TeleportPriceExponent)
        {
            throw new System.NotImplementedException();
        }

        public void SendAvatarPickerReply(AvatarPickerReplyAgentDataArgs AgentData, List<AvatarPickerReplyDataArgs> Data)
        {
            throw new System.NotImplementedException();
        }

        public void SendAgentDataUpdate(UUID agentid, UUID activegroupid, string firstname, string lastname, ulong grouppowers, string groupname, string grouptitle)
        {
            throw new System.NotImplementedException();
        }

        public void SendPreLoadSound(UUID objectID, UUID ownerID, UUID soundID)
        {
            throw new System.NotImplementedException();
        }

        public void SendPlayAttachedSound(UUID soundID, UUID objectID, UUID ownerID, float gain, byte flags)
        {
            throw new System.NotImplementedException();
        }

        public void SendTriggeredSound(UUID soundID, UUID ownerID, UUID objectID, UUID parentID, ulong handle, Vector3 position, float gain)
        {
            throw new System.NotImplementedException();
        }

        public void SendAttachedSoundGainChange(UUID objectID, float gain)
        {
            throw new System.NotImplementedException();
        }

        public void SendNameReply(UUID profileId, string firstname, string lastname)
        {
            throw new System.NotImplementedException();
        }

        public void SendAlertMessage(string message)
        {
            throw new System.NotImplementedException();
        }

        public void SendAgentAlertMessage(string message, bool modal)
        {
            throw new System.NotImplementedException();
        }

        public void SendLoadURL(string objectname, UUID objectID, UUID ownerID, bool groupOwned, string message, string url)
        {
            throw new System.NotImplementedException();
        }

        public void SendDialog(string objectname, UUID objectID, string ownerFirstName, string ownerLastName, string msg, UUID textureID, int ch, string[] buttonlabels)
        {
            throw new System.NotImplementedException();
        }

        public bool AddMoney(int debit)
        {
            throw new System.NotImplementedException();
        }

        public void SendSunPos(Vector3 sunPos, Vector3 sunVel, ulong CurrentTime, uint SecondsPerSunCycle, uint SecondsPerYear, float OrbitalPosition)
        {
            throw new System.NotImplementedException();
        }

        public void SendViewerEffect(ViewerEffectPacket.EffectBlock[] effectBlocks)
        {
            throw new System.NotImplementedException();
        }

        public void SendViewerTime(int phase)
        {
            throw new System.NotImplementedException();
        }

        public UUID GetDefaultAnimation(string name)
        {
            throw new System.NotImplementedException();
        }

        public void SendAvatarProperties(UUID avatarID, string aboutText, string bornOn, byte[] charterMember, string flAbout, uint flags, UUID flImageID, UUID imageID, string profileURL, UUID partnerID)
        {
            throw new System.NotImplementedException();
        }

        public void SendScriptQuestion(UUID taskID, string taskName, string ownerName, UUID itemID, int question)
        {
            throw new System.NotImplementedException();
        }

        public void SendHealth(float health)
        {
            throw new System.NotImplementedException();
        }

        public void SendEstateManagersList(UUID invoice, UUID[] EstateManagers, uint estateID)
        {
            throw new System.NotImplementedException();
        }

        public void SendBannedUserList(UUID invoice, EstateBan[] banlist, uint estateID)
        {
            throw new System.NotImplementedException();
        }

        public void SendRegionInfoToEstateMenu(RegionInfoForEstateMenuArgs args)
        {
            throw new System.NotImplementedException();
        }

        public void SendEstateCovenantInformation(UUID covenant)
        {
            throw new System.NotImplementedException();
        }

        public void SendDetailedEstateData(UUID invoice, string estateName, uint estateID, uint parentEstate, uint estateFlags, uint sunPosition, UUID covenant, string abuseEmail, UUID estateOwner)
        {
            throw new System.NotImplementedException();
        }

        public void SendLandProperties(int sequence_id, bool snap_selection, int request_result, LandData landData, float simObjectBonusFactor, int parcelObjectCapacity, int simObjectCapacity, uint regionFlags)
        {
            throw new System.NotImplementedException();
        }

        public void SendLandAccessListData(List<UUID> avatars, uint accessFlag, int localLandID)
        {
            throw new System.NotImplementedException();
        }

        public void SendForceClientSelectObjects(List<uint> objectIDs)
        {
            throw new System.NotImplementedException();
        }

        public void SendLandObjectOwners(LandData land, List<UUID> groups, Dictionary<UUID, int> ownersAndCount)
        {
            throw new System.NotImplementedException();
        }

        public void SendLandParcelOverlay(byte[] data, int sequence_id)
        {
            throw new System.NotImplementedException();
        }

        public void SendParcelMediaCommand(uint flags, ParcelMediaCommandEnum command, float time)
        {
            throw new System.NotImplementedException();
        }

        public void SendParcelMediaUpdate(string mediaUrl, UUID mediaTextureID, byte autoScale, string mediaType, string mediaDesc, int mediaWidth, int mediaHeight, byte mediaLoop)
        {
            throw new System.NotImplementedException();
        }

        public void SendAssetUploadCompleteMessage(sbyte AssetType, bool Success, UUID AssetFullID)
        {
            throw new System.NotImplementedException();
        }

        public void SendConfirmXfer(ulong xferID, uint PacketID)
        {
            throw new System.NotImplementedException();
        }

        public void SendXferRequest(ulong XferID, short AssetType, UUID vFileID, byte FilePath, byte[] FileName)
        {
            throw new System.NotImplementedException();
        }

        public void SendInitiateDownload(string simFileName, string clientFileName)
        {
            throw new System.NotImplementedException();
        }

        public void SendImageFirstPart(ushort numParts, UUID ImageUUID, uint ImageSize, byte[] ImageData, byte imageCodec)
        {
            throw new System.NotImplementedException();
        }

        public void SendImageNextPart(ushort partNumber, UUID imageUuid, byte[] imageData)
        {
            throw new System.NotImplementedException();
        }

        public void SendImageNotFound(UUID imageid)
        {
            throw new System.NotImplementedException();
        }

        public void SendShutdownConnectionNotice()
        {
            throw new System.NotImplementedException();
        }

        public void SendSimStats(SimStats stats)
        {
            throw new System.NotImplementedException();
        }

        public void SendObjectPropertiesFamilyData(uint RequestFlags, UUID ObjectUUID, UUID OwnerID, UUID GroupID, uint BaseMask, uint OwnerMask, uint GroupMask, uint EveryoneMask, uint NextOwnerMask, int OwnershipCost, byte SaleType, int SalePrice, uint Category, UUID LastOwnerID, string ObjectName, string Description)
        {
            throw new System.NotImplementedException();
        }

        public void SendObjectPropertiesReply(UUID ItemID, ulong CreationDate, UUID CreatorUUID, UUID FolderUUID, UUID FromTaskUUID, UUID GroupUUID, short InventorySerial, UUID LastOwnerUUID, UUID ObjectUUID, UUID OwnerUUID, string TouchTitle, byte[] TextureID, string SitTitle, string ItemName, string ItemDescription, uint OwnerMask, uint NextOwnerMask, uint GroupMask, uint EveryoneMask, uint BaseMask, byte saleType, int salePrice)
        {
            throw new System.NotImplementedException();
        }

        public void SendAgentOffline(UUID[] agentIDs)
        {
            throw new System.NotImplementedException();
        }

        public void SendAgentOnline(UUID[] agentIDs)
        {
            throw new System.NotImplementedException();
        }

        public void SendSitResponse(UUID TargetID, Vector3 OffsetPos, Quaternion SitOrientation, bool autopilot, Vector3 CameraAtOffset, Vector3 CameraEyeOffset, bool ForceMouseLook)
        {
            throw new System.NotImplementedException();
        }

        public void SendAdminResponse(UUID Token, uint AdminLevel)
        {
            throw new System.NotImplementedException();
        }

        public void SendGroupMembership(GroupMembershipData[] GroupMembership)
        {
            throw new System.NotImplementedException();
        }

        public void SendGroupNameReply(UUID groupLLUID, string GroupName)
        {
            throw new System.NotImplementedException();
        }

        public void SendJoinGroupReply(UUID groupID, bool success)
        {
            throw new System.NotImplementedException();
        }

        public void SendEjectGroupMemberReply(UUID agentID, UUID groupID, bool success)
        {
            throw new System.NotImplementedException();
        }

        public void SendLeaveGroupReply(UUID groupID, bool success)
        {
            throw new System.NotImplementedException();
        }

        public void SendCreateGroupReply(UUID groupID, bool success, string message)
        {
            throw new System.NotImplementedException();
        }

        public void SendLandStatReply(uint reportType, uint requestFlags, uint resultCount, LandStatReportItem[] lsrpia)
        {
            throw new System.NotImplementedException();
        }

        public void SendScriptRunningReply(UUID objectID, UUID itemID, bool running)
        {
            throw new System.NotImplementedException();
        }

        public void SendAsset(AssetRequestToClient req)
        {
            throw new System.NotImplementedException();
        }

        public void SendTexture(AssetBase TextureAsset)
        {
            throw new System.NotImplementedException();
        }

        public byte[] GetThrottlesPacked(float multiplier)
        {
            throw new System.NotImplementedException();
        }

        public event ViewerEffectEventHandler OnViewerEffect;
        public event Action<IClientAPI> OnLogout;
        public event Action<IClientAPI> OnConnectionClosed;
        public void SendBlueBoxMessage(UUID FromAvatarID, string FromAvatarName, string Message)
        {
            throw new System.NotImplementedException();
        }

        public void SendLogoutPacket()
        {
            throw new System.NotImplementedException();
        }

        public ClientInfo GetClientInfo()
        {
            throw new System.NotImplementedException();
        }

        public void SetClientInfo(ClientInfo info)
        {
            throw new System.NotImplementedException();
        }

        public void SetClientOption(string option, string value)
        {
            throw new System.NotImplementedException();
        }

        public string GetClientOption(string option)
        {
            throw new System.NotImplementedException();
        }

        public void Terminate()
        {
            throw new System.NotImplementedException();
        }

        public void SendSetFollowCamProperties(UUID objectID, SortedDictionary<int, float> parameters)
        {
            throw new System.NotImplementedException();
        }

        public void SendClearFollowCamProperties(UUID objectID)
        {
            throw new System.NotImplementedException();
        }

        public void SendRegionHandle(UUID regoinID, ulong handle)
        {
            throw new System.NotImplementedException();
        }

        public void SendParcelInfo(RegionInfo info, LandData land, UUID parcelID, uint x, uint y)
        {
            throw new System.NotImplementedException();
        }

        public void SendScriptTeleportRequest(string objName, string simName, Vector3 pos, Vector3 lookAt)
        {
            throw new System.NotImplementedException();
        }

        public void SendDirPlacesReply(UUID queryID, DirPlacesReplyData[] data)
        {
            throw new System.NotImplementedException();
        }

        public void SendDirPeopleReply(UUID queryID, DirPeopleReplyData[] data)
        {
            throw new System.NotImplementedException();
        }

        public void SendDirEventsReply(UUID queryID, DirEventsReplyData[] data)
        {
            throw new System.NotImplementedException();
        }

        public void SendDirGroupsReply(UUID queryID, DirGroupsReplyData[] data)
        {
            throw new System.NotImplementedException();
        }

        public void SendDirClassifiedReply(UUID queryID, DirClassifiedReplyData[] data)
        {
            throw new System.NotImplementedException();
        }

        public void SendDirLandReply(UUID queryID, DirLandReplyData[] data)
        {
            throw new System.NotImplementedException();
        }

        public void SendDirPopularReply(UUID queryID, DirPopularReplyData[] data)
        {
            throw new System.NotImplementedException();
        }

        public void SendEventInfoReply(EventData info)
        {
            throw new System.NotImplementedException();
        }

        public void SendMapItemReply(mapItemReply[] replies, uint mapitemtype, uint flags)
        {
            throw new System.NotImplementedException();
        }

        public void SendAvatarGroupsReply(UUID avatarID, GroupMembershipData[] data)
        {
            throw new System.NotImplementedException();
        }

        public void SendOfferCallingCard(UUID srcID, UUID transactionID)
        {
            throw new System.NotImplementedException();
        }

        public void SendAcceptCallingCard(UUID transactionID)
        {
            throw new System.NotImplementedException();
        }

        public void SendDeclineCallingCard(UUID transactionID)
        {
            throw new System.NotImplementedException();
        }

        public void SendTerminateFriend(UUID exFriendID)
        {
            throw new System.NotImplementedException();
        }

        public void SendAvatarClassifiedReply(UUID targetID, UUID[] classifiedID, string[] name)
        {
            throw new System.NotImplementedException();
        }

        public void SendClassifiedInfoReply(UUID classifiedID, UUID creatorID, uint creationDate, uint expirationDate, uint category, string name, string description, UUID parcelID, uint parentEstate, UUID snapshotID, string simName, Vector3 globalPos, string parcelName, byte classifiedFlags, int price)
        {
            throw new System.NotImplementedException();
        }

        public void SendAgentDropGroup(UUID groupID)
        {
            throw new System.NotImplementedException();
        }

        public void RefreshGroupMembership()
        {
            throw new System.NotImplementedException();
        }

        public void SendAvatarNotesReply(UUID targetID, string text)
        {
            throw new System.NotImplementedException();
        }

        public void SendAvatarPicksReply(UUID targetID, Dictionary<UUID, string> picks)
        {
            throw new System.NotImplementedException();
        }

        public void SendPickInfoReply(UUID pickID, UUID creatorID, bool topPick, UUID parcelID, string name, string desc, UUID snapshotID, string user, string originalName, string simName, Vector3 posGlobal, int sortOrder, bool enabled)
        {
            throw new System.NotImplementedException();
        }

        public void SendAvatarClassifiedReply(UUID targetID, Dictionary<UUID, string> classifieds)
        {
            throw new System.NotImplementedException();
        }

        public void SendParcelDwellReply(int localID, UUID parcelID, float dwell)
        {
            throw new System.NotImplementedException();
        }

        public void SendUserInfoReply(bool imViaEmail, bool visible, string email)
        {
            throw new System.NotImplementedException();
        }

        public void SendUseCachedMuteList()
        {
            throw new System.NotImplementedException();
        }

        public void SendMuteListUpdate(string filename)
        {
            throw new System.NotImplementedException();
        }

        public void KillEndDone()
        {
            throw new System.NotImplementedException();
        }

        public bool AddGenericPacketHandler(string MethodName, GenericMessage handler)
        {
            throw new System.NotImplementedException();
        }

        #endregion

        #region Implementation of IClientIPEndpoint

        public IPAddress EndPoint
        {
            get { throw new System.NotImplementedException(); }
        }

        #endregion
    }
}