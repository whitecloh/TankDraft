#nullable disable
using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TankDraft.Infrastructure.FusionTransport
{
    // Explicit closed-test protocol: identity and bearer credentials never belong in the client message.
    public static class FusionQaProtocol
    {
        public static JObject ReadRequest(byte[] bytes)
        {
            var root = Read(bytes, 8192);
            if (root.Count != 2 || root["Operation"]?.Type != JTokenType.String || !(root["Body"] is JObject body)) throw new InvalidDataException();
            string[] fields;
            switch ((string)root["Operation"])
            {
                case "Lobby": fields = new[] { "OperationId" }; break;
                case "Session": fields = new[] { "ContentVersion", "OperationId" }; break;
                case "Join": fields = new[] { "InstanceId", "ContentVersion", "OperationId" }; break;
                case "Status": fields = new[] { "InstanceId" }; break;
                case "Cancel": fields = new[] { "InstanceId", "TicketId" }; break;
                case "Leave": fields = new[] { "InstanceId", "MatchId" }; break;
                case "ProfileGet": fields = new[] { "InstanceId", "ContentVersion" }; break;
                case "ProgressionGet": fields = new[] { "InstanceId", "ContentVersion" }; break;
                case "ProgressionExecute": fields = new[] { "InstanceId", "ContentVersion", "Kind", "TargetId", "OperationId", "ExpectedSequence" }; break;
                case "ProfileSave": fields = new[] { "InstanceId", "ContentVersion", "ExpectedProfileVersion", "OperationId", "UnitIds", "OrderIds" }; break;
                case "SocketOpen": fields = Array.Empty<string>(); break;
                case "SocketExchange":
                    switch ((string)body["Kind"])
                    {
                        case "Hello": fields = new[] { "Kind", "Protocol", "ContentVersion" }; break;
                        case "Poll": fields = new[] { "Kind", "AfterEventSequence" }; break;
                        case "Command": fields = new[] { "Kind", "Command" }; break;
                        case "EnableParallelRefresh": case "Reauthenticate": fields = new[] { "Kind" }; break;
                        default: throw new InvalidDataException();
                    }
                    break;
                default: throw new InvalidDataException();
            }
            if (!body.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(fields.OrderBy(n => n, StringComparer.Ordinal))) throw new InvalidDataException();
            RejectCredentials(root);
            return root;
        }
        public static JObject ReadResponse(byte[] bytes)
        {
            var root = Read(bytes, 1048704); RejectCredentials(root);
            if (root.Count != 2 || root["Status"]?.Type != JTokenType.Integer || !(root["Body"] is JObject) || root.Value<int>("Status") < 100 || root.Value<int>("Status") > 599) throw new InvalidDataException();
            return root;
        }
        static void RejectCredentials(JToken value)
        {
            if (value is JObject obj)
                foreach (var property in obj.Properties())
                {
                    switch (property.Name.ToLowerInvariant())
                    {
                        case "authorization": case "sessionticket": case "photontoken": case "gatewaykey": case "accesstoken": case "lobbytoken": case "accountid": case "userid": throw new InvalidDataException();
                    }
                    RejectCredentials(property.Value);
                }
            else if (value is JArray array) foreach (var item in array) RejectCredentials(item);
        }
        internal static JObject Read(byte[] bytes, int maximum)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > maximum) throw new InvalidDataException();
            using (var reader = new JsonTextReader(new StringReader(new UTF8Encoding(false, true).GetString(bytes))) { MaxDepth = 32, DateParseHandling = DateParseHandling.None })
            {
                var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new InvalidDataException();
                return root;
            }
        }
    }
}
