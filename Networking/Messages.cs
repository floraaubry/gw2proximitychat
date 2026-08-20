namespace GW2ProximityChat
{
    // JSON (de)serialized via System.Web.Script.Serialization.JavaScriptSerializer, which is
    // part of the .NET Framework GAC (System.Web.Extensions) rather than a NuGet package. That
    // avoids pulling a JSON library into the shared Blish HUD host AppDomain where an assembly
    // version mismatch with anything the host already loaded could break module loading.

    public static class Protocol
    {
        // major.minor must match the server exactly; patch difference is ignored.
        public const string Version = "1.0.0";
    }

    public class MessageEnvelope
    {
        public string Type { get; set; }
    }

    public class StateMessage
    {
        public string Type { get; set; } = "state";
        public string PlayerId { get; set; }
        public string Name { get; set; }
        public int MapId { get; set; }
        public string InstanceKey { get; set; }
        public float[] Pos { get; set; }
        public float[] Facing { get; set; }
        public long Ts { get; set; }

        // Only checked by the server on the first state message of a connection (when identity
        // is established); sent on every message anyway since it's cheap and simpler than a
        // separate handshake step.
        public string Password { get; set; }
        public string Version { get; set; } = Protocol.Version;
    }

    public class PeerInfo
    {
        public string PlayerId { get; set; }
        public string Name { get; set; }
        public float[] Pos { get; set; }
        public float[] Facing { get; set; }
    }

    public class PeersMessage
    {
        public string Type { get; set; } = "peers";
        public PeerInfo[] Peers { get; set; }
    }

    public class HelloMessage
    {
        public string Type { get; set; } = "hello";
        public string ServerName { get; set; }
        public string ServerVersion { get; set; }
    }

    public class AuthFailedMessage
    {
        public string Type { get; set; } = "auth_failed";
        public string Reason { get; set; }
    }

    public class VersionMismatchMessage
    {
        public string Type { get; set; } = "version_mismatch";
        public string ServerVersion { get; set; }
        public string ClientVersion { get; set; }
    }

    public class ServerFullMessage
    {
        public string Type { get; set; } = "server_full";
    }
}
