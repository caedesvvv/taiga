using System;
using System.Collections.Generic;
using System.Text;
using HttpServer;
using System.IO;
using System.Xml;
using System.Net;
using System.Xml.XPath;

namespace WebDAVSharp
{
    public enum LockScope
    {
        exclusive,
        shared
    }

    public enum LockType
    {
        write
    }

    public class LockCommand : ICommand
    {
        WebDAVListener server;

        #region ICommand Members

        public void Start(WebDAVListener server, string path)
        {
            this.server = server;

            server.HttpServer.AddHandler("LOCK", null, path, LockHandler);
        }

        #endregion

        void LockHandler(IHttpClientContext context, IHttpRequest request, IHttpResponse response)
        {
            string username;
            if (server.AuthenticateRequest(request, response, out username))
            {
                WebDAVLockRequest lockRequest = ParseRequest(request);

                WebDAVLockResponse lockResponse = server.OnLockConnector(lockRequest);
                if (lockResponse == null)
                {
                    response.Status = HttpStatusCode.InternalServerError;
                    return;
                }

                if (lockResponse.HttpStatusCode == HttpStatusCode.OK || lockResponse.HttpStatusCode == HttpStatusCode.Created)
                {
                    using (MemoryStream responseStream = new MemoryStream())
                    {
                        XmlTextWriter xmlWriter = new XmlTextWriter(responseStream, Encoding.ASCII); //, Encoding.UTF8);

                        xmlWriter.Formatting = Formatting.Indented;
                        xmlWriter.IndentChar = '\t';
                        xmlWriter.Indentation = 1;
                        xmlWriter.WriteStartDocument();

                        xmlWriter.WriteStartElement("D", "prop", "DAV:");
                        xmlWriter.WriteStartElement("lockdiscovery", "DAV:");
                        xmlWriter.WriteStartElement("activelock", "DAV:");

                        xmlWriter.WriteStartElement("locktype", "DAV:");
                        xmlWriter.WriteElementString(Enum.GetName(typeof(LockType), lockResponse.LockType), "DAV:", String.Empty); //only legal value available. future extensions might support others
                        xmlWriter.WriteEndElement(); //locktype

                        xmlWriter.WriteStartElement("lockscope", "DAV:");
                        xmlWriter.WriteElementString(Enum.GetName(typeof(LockScope), lockResponse.LockScope), "DAV:", String.Empty); //possible values "exclusive" or "shared"
                        xmlWriter.WriteEndElement(); //lockscope

                        xmlWriter.WriteStartElement("depth", "DAV:");
                        xmlWriter.WriteValue(lockResponse.Depth);
                        xmlWriter.WriteEndElement(); //depth

                        //The server must save this identifier and return it in the lockdiscovery property so that other users and other client software can see some information about the lock creator.
                        xmlWriter.WriteStartElement("owner", "DAV:");
                        if (lockResponse.OwnerNamespaceUri != null && lockResponse.OwnerNamespaceUri != String.Empty)
                        {
                            xmlWriter.WriteAttributeString("xmlns:a", lockResponse.OwnerNamespaceUri);
                        }
                        if (lockResponse.OwnerValue != null && lockResponse.OwnerValue != String.Empty)
                        {
                            xmlWriter.WriteValue(lockResponse.OwnerValue);
                        }
                        else
                        {
                            foreach (KeyValuePair<string, string> kvp in lockResponse.OwnerValues)
                            {
                                xmlWriter.WriteElementString(kvp.Key, "DAV:", kvp.Value);
                            }
                        }
                        //xmlWriter.WriteElementString("lock-user", "DAV:", username);
                        //xmlWriter.WriteElementString("created-by", "DAV:", "Some user");
                        xmlWriter.WriteEndElement(); //owner

                        xmlWriter.WriteElementString("timeout", "DAV:", lockResponse.Timeout);

                        xmlWriter.WriteStartElement("locktoken", "DAV:");
                        xmlWriter.WriteElementString("href", "DAV:", lockResponse.LockToken);
                        xmlWriter.WriteEndElement();
                        response.AddHeader("Lock-Token", lockResponse.LockToken); //add lock token also to header

                        xmlWriter.WriteEndElement(); //activelock
                        xmlWriter.WriteEndElement(); //lockdiscovery
                        xmlWriter.WriteEndElement(); //prop

                        xmlWriter.WriteEndDocument();
                        xmlWriter.Flush();

                        response.Status = lockResponse.HttpStatusCode;

                        byte[] bytes = responseStream.ToArray();
                        response.ContentLength = bytes.Length;
                        //Console.WriteLine(Encoding.UTF8.GetString(bytes));
                        response.Body.Write(bytes, 0, bytes.Length);

                        xmlWriter.Close();
                    }
                }
                else
                {
                    response.Status = lockResponse.HttpStatusCode;
                }
            }
        }

        WebDAVLockRequest ParseRequest(IHttpRequest request)
        {
            WebDAVLockRequest lockRequest = new WebDAVLockRequest();
            lockRequest.Path = request.UriPath;
            lockRequest.RequestedTimeout = request.Headers.GetValues("timeout"); //can contain multiple values, for example: "Timeout: Infinite, Second-604800"

            if (request.ContentLength != 0)
            {
                XPathNavigator requestNavigator = new XPathDocument(request.Body).CreateNavigator();
                XPathNodeIterator propNodeIterator = requestNavigator.SelectDescendants("lockinfo", "DAV:", false);
                if (propNodeIterator.MoveNext())
                {
                    XPathNodeIterator nodeChildren = propNodeIterator.Current.SelectChildren(XPathNodeType.All);
                    while (nodeChildren.MoveNext())
                    {
                        XPathNavigator currentNode = nodeChildren.Current;

                        if (currentNode.LocalName == "lockscope")
                        {
                            if (currentNode.HasChildren)
                            {
                                if (currentNode.MoveToFirstChild())
                                {
                                    lockRequest.LockScope = (LockScope)Enum.Parse(typeof(LockScope), currentNode.LocalName.ToLower());
                                    currentNode.MoveToParent();
                                }
                            }
                        }
                        else if (currentNode.LocalName == "owner")
                        {
                            lockRequest.OwnerNamespaceUri = currentNode.NamespaceURI;
                            if (currentNode.Value != null && currentNode.Value != String.Empty)
                                lockRequest.OwnerValue = currentNode.Value;
                            if (currentNode.MoveToFirstChild())
                            {
                                lockRequest.OwnerValues.Add(currentNode.LocalName, currentNode.Value);
                                while (currentNode.MoveToNext())
                                {
                                    lockRequest.OwnerValues.Add(currentNode.LocalName, currentNode.Value);
                                }
                                currentNode.MoveToParent();
                            }
                        }
                        else if (currentNode.LocalName == "locktype")
                        {
                            if (currentNode.MoveToFirstChild())
                            {
                                lockRequest.LockType = (LockType)Enum.Parse(typeof(LockType), currentNode.LocalName.ToLower());
                                currentNode.MoveToParent();
                            }
                        }
                    }
                }
            }

            return lockRequest;
        }
    }
}
