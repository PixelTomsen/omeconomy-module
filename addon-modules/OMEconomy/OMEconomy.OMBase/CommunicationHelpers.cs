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

using System.Collections.Generic;
using OpenMetaverse;
using System;
using System.Collections;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using log4net;
using System.Reflection;
using System.Text;
using System.Net;
using System.IO;
using LitJson;
using System.Security.Cryptography;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace OMEconomy.OMBase
{
    public class CommunicationHelpers
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);


        public static String normaliseURL(String url)
        {
            url = url.EndsWith("/") ? url : (url + "/");
            url = url.StartsWith("http://") ? url : ("http://" + url);
            return url;
        }

        public static String hashParameters(Hashtable parameters, string secret)
        {
            StringBuilder concat = new StringBuilder();

            //Ensure that the parameters are in the correct order
            SortedList<string, string> sortedParameters = new SortedList<string, string>();
            foreach (DictionaryEntry parameter in parameters)
            {
                sortedParameters.Add((string)parameter.Key, (string)parameter.Value);
            }

            foreach (KeyValuePair<string, string> de in sortedParameters)
            {
                concat.Append((string)de.Key + (string)de.Value);
            }
            return hashString(concat.ToString(), secret);
        }

        public static String hashString(string message, string secret)
        {
            SHA1 hashFunction = new SHA1Managed();
            byte[] hashValue = hashFunction.ComputeHash(Encoding.UTF8.GetBytes(message + secret));

            string hashHex = "";
            foreach (byte b in hashValue)
            {
                hashHex += String.Format("{0:x2}", b);
            }

            return hashHex;
        }

        public static String serializeDictionary(Dictionary<string, string> data)
        {
            string value = String.Empty;
            foreach (KeyValuePair<string, string> pair in data)
            {
                value += pair.Key + "=" + pair.Value + "&";
            }
            return value.Remove(value.Length - 1);
        }

        public static Dictionary<string, string> doRequest(string url, Dictionary<string, string> postParameters)
        {
            string postData = postParameters == null ? "" : CommunicationHelpers.serializeDictionary(postParameters);
            ASCIIEncoding encoding = new ASCIIEncoding();
            byte[] data = encoding.GetBytes(postData);
            String str = String.Empty;

#if DEBUG
            m_log.Debug("[OMECONOMY] Request: " + url + "?" + postData);
#endif

            try
            {
#if INSOMNIA
          ServicePointManager.ServerCertificateValidationCallback = delegate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) { return true; };
#endif

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.Timeout = 5000;
                request.ContentType = "application/x-www-form-urlencoded";
                request.ContentLength = data.Length;
                Stream requestStream = request.GetRequestStream();

                requestStream.Write(data, 0, data.Length);
                requestStream.Close();

                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                Stream responseStream = response.GetResponseStream();

                StreamReader reader = new StreamReader(responseStream, Encoding.Default);
                str = reader.ReadToEnd();
                reader.Close();
                responseStream.Flush();
                responseStream.Close();
                response.Close();

#if DEBUG
                m_log.Debug("[OMECONOMY] Response: " + str);
#endif

                Dictionary<string, string> returnValue = JsonMapper.ToObject<Dictionary<string, string>>(str);
                return returnValue != null ? returnValue : new Dictionary<string, string>();

            }
            catch (Exception e)
            {
                m_log.Error("[OMBASE]: Could not parse response " + e);
                return null;
            }
        }

        public static bool validateRequest(Hashtable communicationData, Hashtable requestData, string gatewayURL)
        {
            OMBaseModule omBase = new OMBaseModule();

            string hashValue = (string)(communicationData)["hashValue"];
            UUID regionUUID = UUID.Parse((string)(communicationData)["regionUUID"]);
            UInt32 nonce = UInt32.Parse((string)(communicationData)["nonce"]);
            string notificationID = (string)(communicationData)["notificationID"];

            Dictionary<string, string> d = new Dictionary<string, string>();
            d.Add("method", "verifyNotification");
            d.Add("notificationID", notificationID);
            d.Add("regionUUID", regionUUID.ToString());
            d.Add("hashValue", hashString(nonce++.ToString(), omBase.getRegionSecret(regionUUID)));
            Dictionary<string, string> response = doRequest(gatewayURL, d);
            string secret = (string)response["secret"];

            if (hashValue == hashParameters(requestData, secret))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static string getGatewayURL(string initURL, string name, string moduleVersion, string gatewayEnvironment)
        {
#if DEBUG
            m_log.Debug(String.Format("[OMECONOMY] getGatewayURL({0}, {1}, {2}, {3})", initURL, name, moduleVersion, gatewayEnvironment));
#endif

            Dictionary<string, string> d = new Dictionary<string, string>();
            d.Add("moduleName", name);
            d.Add("moduleVersion", moduleVersion);
            d.Add("gatewayEnvironment", gatewayEnvironment);

            Dictionary<string, string> response = CommunicationHelpers.doRequest(initURL, d);
            string gatewayURL = (string)response["gatewayURL"];

            if (gatewayURL != null)
            {
                m_log.Info("[" + name + "]: GatewayURL: " + gatewayURL);
            }
            else
            {
                m_log.Error("[" + name + "]: Could not set the GatewayURL - Please restart or contact the module vendor");
            }
            return gatewayURL;
        }
    }
}
