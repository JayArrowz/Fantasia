using System;
using Godot;
using Fantasia.Server;

namespace Fantasia;

/// Autoload at /root/Net. Owns the ENet peer and routes messages between the client and the
/// authoritative ServerWorld. When hosting, the local player talks to the server via direct calls
/// (still JSON-serialized so both paths behave identically).
public partial class Net : Node
{
    public static Net I { get; private set; }

    public ServerWorld Server { get; private set; }
    public bool IsDedicated { get; private set; }
    public bool Connected { get; private set; }

    public event Action<Snapshot> SnapshotReceived;
    public event Action<PrivateState> StateReceived;
    public event Action<ServerMsg> MessageReceived;
    public event Action<string> ConnectionLost;

    string pendingName, pendingPass;

    public override void _EnterTree() => I = this;

    public override void _Ready()
    {
        Multiplayer.ConnectedToServer += OnConnected;
        Multiplayer.ConnectionFailed += () => Fail("Could not connect to the server.");
        Multiplayer.ServerDisconnected += () => Fail("Lost connection to the server.");
        Multiplayer.PeerDisconnected += id => Server?.OnPeerDisconnected(id);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationPredelete) Server?.SaveAll();
    }

    void Fail(string why)
    {
        Connected = false;
        Shutdown();
        ConnectionLost?.Invoke(why);
    }

    // ================= start / stop =================

    public Error Host(int port, bool dedicated)
    {
        Shutdown();
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(port, GameConst.MaxPlayers);
        if (err != Error.Ok) return err;
        Multiplayer.MultiplayerPeer = peer;
        IsDedicated = dedicated;
        Server = new ServerWorld { Name = "ServerWorld" };
        Server.SendSnapshot = (peerId, json) => Deliver(peerId, json, 0);
        Server.SendState = (peerId, json) => Deliver(peerId, json, 1);
        Server.SendMsg = (peerId, json) => Deliver(peerId, json, 2);
        Server.Kick = peerId => (Multiplayer.MultiplayerPeer as ENetMultiplayerPeer)?.DisconnectPeer((int)peerId);
        AddChild(Server);
        Connected = true;
        GD.Print($"[Net] Server listening on port {port}{(dedicated ? " (dedicated)" : "")}.");
        return Error.Ok;
    }

    public Error Join(string address, int port, string name, string pass)
    {
        Shutdown();
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateClient(address, port);
        if (err != Error.Ok) return err;
        Multiplayer.MultiplayerPeer = peer;
        pendingName = name; pendingPass = pass;
        return Error.Ok;
    }

    public void LoginLocal(string name, string pass) =>
        Send(new ClientMsg { T = C2S.Login, S = name, S2 = pass });

    void OnConnected()
    {
        Connected = true;
        Send(new ClientMsg { T = C2S.Login, S = pendingName, S2 = pendingPass });
        pendingPass = null;
    }

    public void Shutdown()
    {
        if (Server != null)
        {
            Server.SaveAll();
            Server.QueueFree();
            Server = null;
        }
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer p) p.Close();
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        Connected = false;
    }

    // ================= client -> server =================

    public void Send(ClientMsg m)
    {
        var json = Json.Write(m);
        if (Server != null) Server.Handle(Multiplayer.GetUniqueId(), Json.Read<ClientMsg>(json));
        else if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer) RpcId(1, MethodName.RpcC2S, json);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RpcC2S(string json)
    {
        if (Server == null || json == null || json.Length > 1024) return;
        ClientMsg m;
        try { m = Json.Read<ClientMsg>(json); } catch { return; }
        Server.Handle(Multiplayer.GetRemoteSenderId(), m);
    }

    // ================= server -> client =================

    void Deliver(long peerId, string json, int kind)
    {
        if (peerId == Multiplayer.GetUniqueId())
        {
            if (!IsDedicated) Receive(json, kind);
            return;
        }
        switch (kind)
        {
            case 0: RpcId(peerId, MethodName.RpcSnap, json); break;
            case 1: RpcId(peerId, MethodName.RpcState, json); break;
            default: RpcId(peerId, MethodName.RpcMsg, json); break;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 1)]
    void RpcSnap(string json) => Receive(json, 0);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 2)]
    void RpcState(string json) => Receive(json, 1);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 2)]
    void RpcMsg(string json) => Receive(json, 2);

    void Receive(string json, int kind)
    {
        try
        {
            switch (kind)
            {
                case 0: SnapshotReceived?.Invoke(Json.Read<Snapshot>(json)); break;
                case 1: StateReceived?.Invoke(Json.Read<PrivateState>(json)); break;
                default: MessageReceived?.Invoke(Json.Read<ServerMsg>(json)); break;
            }
        }
        catch (Exception e) { GD.PrintErr($"[Net] Bad packet: {e.Message}"); }
    }
}
