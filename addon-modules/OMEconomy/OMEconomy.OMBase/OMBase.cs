/*
 * Michael E. Steurer, 2010
 * Institute for Information Systems and Computer Media
 * Graz University of Technology
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using LitJson;
using Mono.Addins;
using OpenMetaverse;
using OpenSim;
using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

[assembly: Addin("OMBaseModule", "0.1")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace OMEconomy.OMBase
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule")]
    public class OMBaseModule : ISharedRegionModule
    {
        private String MODULE_NAME = "OMBase";
        public String MODULE_VERSION = "0.03.0003";

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Dictionary<UUID, string> regionSecrets = new Dictionary<UUID, string>();
        private SceneHandler sceneHandler = SceneHandler.getInstance();

        private bool m_Enabled = false;
        private string gridURL = String.Empty;
        private string gridID = String.Empty;
        internal String gatewayURL = String.Empty;
        private String initURL = String.Empty;
        private String gatewayEnvironment = String.Empty;

        private delegate void delegateAsynchronousClaimUser(String gatewayURL, Dictionary<string, string> data);


        #region ISharedRegion implementation
        public string Name { get { return MODULE_NAME; } }

        public void Initialise(IConfigSource config)
        {
            IConfig cfg = config.Configs["OpenMetaverseEconomy"];

            if (cfg != null)
                m_Enabled = cfg.GetBoolean("enabled", false);
            if (!m_Enabled)
                return;

            if (gatewayURL.Equals(String.Empty))
            {
                gridID = config.Configs["OpenMetaverseEconomy"].GetString("GridID", String.Empty);
                gridURL = config.Configs["GridService"].GetString("GridServerURI", String.Empty);

                gridURL = CommunicationHelpers.normaliseURL(gridURL);

                try
                {
                    IConfig startupConfig = config.Configs["OpenMetaverseEconomy"];
                    gatewayEnvironment = startupConfig.GetString("OMBaseEnvironment", "TEST");
                    initURL = startupConfig.GetString("OMEconomyInitialize", String.Empty);

                }
                catch (Exception e)
                {
                    m_log.Error("[OMBASE]: " + e);
                }

                gatewayURL = CommunicationHelpers.getGatewayURL(initURL, MODULE_NAME, MODULE_VERSION, gatewayEnvironment);
            }

            MainServer.Instance.AddXmlRPCHandler("OMBaseNotification", genericNotify, false);
        }

        public void PostInitialise() { }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;
            sceneHandler.addScene(scene);
            scene.EventManager.OnMakeRootAgent += AddAvatar;
            scene.EventManager.OnClientClosed += LeaveAvatar;
            String regionIP = sceneHandler.getRegionIP(scene);
            String regionName = scene.RegionInfo.RegionName;
            String regionUUID = scene.RegionInfo.originRegionID.ToString();

            initializeRegion(regionIP, regionName, regionUUID);
            scene.AddCommand(this, "OMBaseTest", "Test Open Metaverse Economy Connection", "Test Open Metaverse Economy Connection", testConnection);
            scene.AddCommand(this, "OMRegister", "Registers the Metaverse Economy Module", "Registers the Metaverse Economy Module", registerModule);
        }

        public void RegionLoaded(Scene scene) { }

        public void RemoveRegion(Scene scene) { }

        public void Close()
        {
            if (m_Enabled)
            {
                List<string> regions = sceneHandler.getUniqueRegions().ConvertAll<String>(UUIDToString);
                Dictionary<string, string> d = new Dictionary<string, string>();
                d.Add("method", "closeRegion");
                d.Add("gridURL", gridURL);
                d.Add("regions", JsonMapper.ToJson(regions));
                CommunicationHelpers.doRequest(gatewayURL, d);
            }
        }

        #endregion

        internal void initializeRegion(String regionIP, String regionName, String regionUUID)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            d.Add("method", "initializeRegion");
            d.Add("regionIP", regionIP);
            d.Add("regionName", regionName);
            d.Add("regionUUID", regionUUID);
            d.Add("gridURL", gridURL);
            d.Add("simulatorVersion", VersionInfo.Version);
            d.Add("moduleVersion", MODULE_VERSION);
            Dictionary<string, string> response = CommunicationHelpers.doRequest(gatewayURL, d);

            if (response == null)
            {
                m_log.Error("[OMBASE]: The Service is not Available");
            }
            else
            {
                if (regionSecrets.ContainsKey(UUID.Parse(regionUUID)))
                {
                    m_log.Error("[OMBASE]: The secret for region " + regionUUID + " is already set");
                }
                else
                {
                    regionSecrets.Add(UUID.Parse(regionUUID), (string)response["regionSecret"]);
                }
                m_log.Info("[OMBASE]: The Service is Available");
            }
        }


        private void registerModule(string module, string[] args)
        {
            m_log.Info("[OMECONOMY]: +-");
            m_log.Info("[OMECONOMY]: | Your grid identifier is \"" + gridURL + "\"");
            String shortName = MainConsole.Instance.CmdPrompt("           [OMECONOMY]: | Please enter the grid's nick name");
            String longName = MainConsole.Instance.CmdPrompt("           [OMECONOMY]: | Please enter the grid's full name");

            Dictionary<string, string> d = new Dictionary<string, string>();
            d.Add("method", "registerScript");
            d.Add("gridShortName", shortName);
            d.Add("gridLongName", longName);
            d.Add("gridDescription", "");
            d.Add("gridURL", gridURL);

            Dictionary<string, string> response = CommunicationHelpers.doRequest(gatewayURL, d);
            if (response.ContainsKey("success") && response["success"] == "TRUE")
            {
                m_log.Info("[OMECONOMY]: +-");
                m_log.Info("[OMECONOMY]: | Please visit");
                m_log.Info("[OMECONOMY]: |   " + response["scriptURL"]);
                m_log.Info("[OMECONOMY]: | to get the Terminal's script");
                m_log.Info("[OMECONOMY]: +-");
            }
            else
            {
                m_log.Error("Could not active the grid. Please check the parameters and try again");
            }
        }

        private string UUIDToString(UUID item)
        {
            return item.ToString();
        }

        private void testConnection(string module, string[] args)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            d.Add("method", "checkStatus");
            bool status = false;
            try
            {
                Dictionary<string, string> response = CommunicationHelpers.doRequest(gatewayURL, d);
                if (response.ContainsKey("status") && response["status"] == "INSOMNIA")
                {
                    status = true;
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[OMBase] - Exception: {0}", e.Message);
            }

            m_log.Info("[OMECONOMY]: +---------------------------------------");
            m_log.Info("[OMECONOMY]: | gridID: " + gridURL);
            m_log.Info("[OMECONOMY]: | connectionStatus: " + status);
            m_log.Info("[OMECONOMY]: +---------------------------------------");
        }

        public String getRegionSecret(UUID regionUUID)
        {
            return regionSecrets.ContainsKey(regionUUID) ? regionSecrets[regionUUID] : String.Empty;
        }

        internal void AddAvatar(ScenePresence avatar)
        {
            IClientAPI client = sceneHandler.LocateClientObject(avatar.UUID);
            Scene currentScene = sceneHandler.LocateSceneClientIn(avatar.UUID);

            Dictionary<string, string> dd = new Dictionary<string, string>();
            dd.Add("method", "claimUser");
            dd.Add("avatarUUID", avatar.UUID.ToString());
            dd.Add("avatarName", avatar.Name);
            dd.Add("language", "ENG");
            dd.Add("viewer", "HIPPO");
            dd.Add("clientIP", "http://" + client.GetClientEP().ToString() + "/");
            dd.Add("regionUUID", sceneHandler.LocateSceneClientIn(avatar.UUID).RegionInfo.RegionID.ToString());
            dd.Add("gridURL", gridURL);
            dd.Add("regionIP", sceneHandler.getRegionIP(currentScene));

            delegateAsynchronousClaimUser a = new delegateAsynchronousClaimUser(asynchronousClaimUser);
            a.BeginInvoke(gatewayURL, dd, null, null);
        }

        private void asynchronousClaimUser(String gatewayURL, Dictionary<string, string> data)
        {
            if (CommunicationHelpers.doRequest(gatewayURL, data) == null)
            {
                String message = "The currency service is not available. Please try again later.";
                sceneHandler.LocateClientObject(new UUID(data["avatarUUID"])).SendBlueBoxMessage(UUID.Zero, "", message);
            }
        }

        public String getGridUIRL() { return gridURL; }

        private void serviceNotAvailable(UUID avatarUUID)
        {
            String message = "The currency service is not available. Please try again later.";
            sceneHandler.LocateClientObject(avatarUUID).SendBlueBoxMessage(UUID.Zero, "", message);
        }

        internal void LeaveAvatar(UUID clientID, Scene scene1)
        {
            try
            {
                Dictionary<string, string> d = new Dictionary<string, string>();
                d.Add("method", "leaveUser");
                d.Add("avatarUUID", clientID.ToString());
                d.Add("regionUUID", sceneHandler.LocateSceneClientIn(clientID).RegionInfo.RegionID.ToString());
                CommunicationHelpers.doRequest(gatewayURL, d);
            }
            catch (Exception e)
            {
                m_log.InfoFormat("[OMBASE]: LeaveAvatar(): " + e.Message);
            }
        }

        public XmlRpcResponse genericNotify(XmlRpcRequest request, IPEndPoint ep)
        {
            XmlRpcResponse r = new XmlRpcResponse();
            try
            {
                Hashtable requestData = (Hashtable)request.Params[0];
                Hashtable communicationData = (Hashtable)request.Params[1];

#if DEBUG
                m_log.Debug("[OMBASE]: genericNotify(...)");
                foreach (DictionaryEntry requestDatum in requestData)
                {
                    m_log.Debug("[OMBASE]:   " + requestDatum.Key.ToString() + " " + (string)requestDatum.Value);
                }
                foreach (DictionaryEntry communicationDatum in communicationData)
                {
                    m_log.Debug("[OMBASE]:   " + communicationDatum.Key.ToString() + " " + (string)communicationDatum.Value);
                }
#endif

                String method = (string)requestData["method"];
                requestData.Remove("method");

                if (CommunicationHelpers.validateRequest(communicationData, requestData, gatewayURL))
                {
                    switch (method)
                    {
                        case "notifyUser": r.Value = userInteract(requestData); break;
                        case "writeLog": r.Value = writeLog(requestData); break;
                        case "notifyIsAlive": r.Value = isAlive(requestData); break;
                        default: m_log.Error("[OMBASE]: Method " + method + " is not supported"); break;
                    }
                }
                else
                {
                    throw new Exception("Hash values do not match");
                }
            }
            catch (Exception e)
            {
                m_log.Error("[OMBASE]: genericNotify(): " + e.Message);
                r.SetFault(1, "Could not parse the requested method");
            }
            return r;
        }

        private Hashtable userInteract(Hashtable requestData)
        {
            Hashtable rparms = new Hashtable();
            try
            {
                UUID receiverUUID = UUID.Parse((string)requestData["receiverUUID"]);
                Int32 type = Int32.Parse((string)requestData["type"]);
                String payloadID = (string)requestData["payloadID"];

                Dictionary<string, string> d = new Dictionary<string, string>();
                d.Add("method", "getNotificationMessage");
                d.Add("payloadID", payloadID);

                Dictionary<string, string> messageItems = CommunicationHelpers.doRequest(gatewayURL, d);
                if (messageItems == null)
                {
                    throw new Exception("Could not fetch payload with ID " + payloadID);
                }

#if DEBUG
                foreach (KeyValuePair<string, string> pair in messageItems)
                {
                    m_log.Error(pair.Key + "  " + pair.Value);
                }
#endif

                IClientAPI client = sceneHandler.LocateClientObject(receiverUUID);
                if (client == null)
                {
                    throw new Exception("Could not locate the specified avatar");
                }

                Scene userScene = sceneHandler.GetSceneByUUID(client.Scene.RegionInfo.originRegionID);
                if (userScene == null)
                {
                    throw new Exception("Could not locate the specified scene");
                }

                String message = messageItems["message"];

                UUID senderUUID = UUID.Zero;
                String senderName = String.Empty;
                IDialogModule dm = null;
                IClientAPI sender = null;

                IUserManagement userManager = sceneHandler.GetRandomScene().RequestModuleInterface<IUserManagement>();
                if (userManager == null)
                {
                    throw new Exception("Could not locate UserMangement Interface");
                }

                switch (type)
                {
                    case (int)NotificationType.LOAD_URL:
                        String url = messageItems["url"];

                        dm = userScene.RequestModuleInterface<IDialogModule>();
                        dm.SendUrlToUser(receiverUUID, "OMEconomy", UUID.Zero, UUID.Zero, false, message, url);
                        break;

                    case (int)NotificationType.CHAT_MESSAGE:
                        senderUUID = UUID.Parse(messageItems["senderUUID"]);
                        senderName = userManager.GetUserName(senderUUID);


                        client.SendChatMessage(message, (byte)ChatTypeEnum.Say, Vector3.Zero, senderName, senderUUID, (byte)ChatSourceType.Agent, (byte)ChatAudibleLevel.Fully);

                        sender = sceneHandler.LocateClientObject(senderUUID);
                        if (sender != null)
                        {
                            sender.SendChatMessage(message, (byte)ChatTypeEnum.Say, Vector3.Zero, senderName, senderUUID, (byte)ChatSourceType.Agent, (byte)ChatAudibleLevel.Fully);
                        }
                        break;

                    case (int)NotificationType.ALERT:
                        dm = userScene.RequestModuleInterface<IDialogModule>();
                        dm.SendAlertToUser(receiverUUID, message);
                        break;

                    case (int)NotificationType.DIALOG:
                        client.SendBlueBoxMessage(UUID.Zero, "", message);
                        break;

                    case (int)NotificationType.GIVE_NOTECARD:
                        break;

                    case (int)NotificationType.INSTANT_MESSAGE:
                        senderUUID = UUID.Parse(messageItems["senderUUID"]);
                        UUID sessionUUID = UUID.Parse(messageItems["sessionUUID"]);
                        if (messageItems.ContainsKey("senderName"))
                        {
                            senderName = messageItems["senderName"];
                        }
                        else
                        {
                            senderName = userManager.GetUserName(UUID.Parse((string)messageItems["senderUUID"]));

                        }

                        GridInstantMessage msg = new GridInstantMessage();
                        msg.fromAgentID = senderUUID.Guid;
                        msg.toAgentID = receiverUUID.Guid;
                        msg.imSessionID = sessionUUID.Guid;
                        msg.fromAgentName = senderName;
                        msg.message = (message != null && message.Length > 1024) ? msg.message = message.Substring(0, 1024) : message;
                        msg.dialog = (byte)InstantMessageDialog.MessageFromAgent;
                        msg.fromGroup = false;
                        msg.offline = (byte)0;
                        msg.ParentEstateID = 0;
                        msg.Position = Vector3.Zero;
                        msg.RegionID = userScene.RegionInfo.RegionID.Guid;


                        client.SendInstantMessage(msg);

                        sender = sceneHandler.LocateClientObject(senderUUID);
                        if (sender != null)
                        {
                            sender.SendInstantMessage(msg);
                        }
                        break;

                    default:
                        break;
                }

                rparms["success"] = true;
            }
            catch (Exception e)
            {
                m_log.Error("[OMBASE]: userInteract() " + e.Message);
                rparms["success"] = false;
            }
            return rparms;
        }

        private Hashtable writeLog(Hashtable requestData)
        {
            Hashtable rparms = new Hashtable();
            try
            {
                String message = (string)requestData["message"];
                m_log.Error("[OMBASE]: " + message);
                rparms["success"] = true;
            }
            catch (Exception)
            {
                rparms["success"] = false;
            }
            return rparms;
        }

        private Hashtable isAlive(Hashtable requestData)
        {
            Hashtable rparms = new Hashtable();
            rparms["success"] = false;
            if (requestData.ContainsKey("avatarUUID"))
            {
                UUID avatarUUID = UUID.Parse((string)requestData["avatarUUID"]);
                if (sceneHandler.LocateClientObject(avatarUUID) != null)
                {
                    rparms["success"] = true;
                }
            }
            else
            {
                rparms["success"] = true;
                rparms["version"] = MODULE_VERSION;
            }
            return rparms;
        }
    }

    public enum NotificationType : int
    {
        LOAD_URL = 1,
        INSTANT_MESSAGE = 2,
        ALERT = 3,
        DIALOG = 4,
        GIVE_NOTECARD = 5,
        CHAT_MESSAGE = 6,
    }
}
