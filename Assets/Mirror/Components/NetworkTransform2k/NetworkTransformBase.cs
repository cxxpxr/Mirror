// NetworkTransform V2 by vis2k
// based on Glenn Fielder https://gafferongames.com/post/snapshot_interpolation/
//
// Base class for NetworkTransform and NetworkTransformChild.
// => simple unreliable sync without any interpolation for now.
// => which means we don't need teleport detection either
using System.Collections.Generic;
using UnityEngine;

namespace Mirror
{
    public abstract class NetworkTransformBase : NetworkBehaviour
    {
        // TODO SyncDirection { CLIENT_TO_SERVER, SERVER_TO_CLIENT } is easier?
        [Header("Authority")]
        [Tooltip("Set to true if moves come from owner client, set to false if moves always come from server")]
        public bool clientAuthority;

        // Is this a client with authority over this transform?
        // This component could be on the player object or any object that has been assigned authority to this client.
        bool IsClientWithAuthority => hasAuthority && clientAuthority;

        // target transform to sync. can be on a child.
        protected abstract Transform targetComponent { get; }

        [Header("Sync")]
        [Tooltip("Reliable(=0) by default, along with the rest of Mirror. Feel free to use Unreliable (=1).")]
        public int channelId = Channels.Reliable;
        [Range(0, 1)] public float sendInterval = 0.050f;
        double lastClientSendTime;
        double lastServerSendTime;

        // snapshot timestamps are _remote_ time
        // we need to interpolate and calculate buffer lifetimes based on it.
        // -> we don't know remote's current time
        // -> NetworkTime.time fluctuates too much, that's no good
        // -> we _could_ calculate an offset when the first snapshot arrives,
        //    but if there was high latency then we'll always calculate time
        //    with high latency
        // -> at any given time, we are interpolating from snapshot A to B
        // => seems like A.timestamp += deltaTime is a good way to do it
        // => let's store it in two variables:
        // => DOUBLE for long term accuracy & batching gives us double anyway
        double serverRemoteClientTime;
        double clientRemoteServerTime;

        // "Experimentally I’ve found that the amount of delay that works best
        //  at 2-5% packet loss is 3X the packet send rate"
        [Tooltip("Snapshots are buffered for sendInterval * multiplier seconds. At 2-5% packet loss, 3x supposedly works best.")]
        public int bufferTimeMultiplier = 3;
        public float bufferTime => sendInterval * bufferTimeMultiplier;

        // snapshots sorted by timestamp
        // in the original article, glenn fiedler drops any snapshots older than
        // the last received snapshot.
        // -> instead, we insert into a sorted buffer
        // -> the higher the buffer information density, the better
        // -> we still drop anything older than the first element in the buffer
        SortedList<double, Snapshot> serverBuffer = new SortedList<double, Snapshot>();
        SortedList<double, Snapshot> clientBuffer = new SortedList<double, Snapshot>();

        // absolute interpolation time, moved along with deltaTime
        // TODO might be possible to use only remoteTime - bufferTime later?
        double serverInterpolationTime;
        double clientInterpolationTime;

        [Header("Debug")]
        public bool showGizmos;
        public bool showOverlay;
        public Color overlayColor = new Color(0, 0, 0, 0.5f);

        // snapshot functions //////////////////////////////////////////////////
        // construct a snapshot of the current state
        Snapshot ConstructSnapshot()
        {
            // NetworkTime.localTime for double precision until Unity has it too
            return new Snapshot(
                NetworkTime.localTime,
                targetComponent.localPosition,
                targetComponent.localRotation,
                targetComponent.localScale
            );
        }

        // set position carefully depending on the target component
        void ApplySnapshot(Snapshot snapshot)
        {
            // local position/rotation for VR support
            targetComponent.localPosition = snapshot.transform.position;
            targetComponent.localRotation = snapshot.transform.rotation;
            targetComponent.localScale = snapshot.transform.scale;
        }

        // helper function to apply snapshots.
        // we use the same one on server and client.
        // => called every Update() depending on authority.
        void ApplySnapshots(ref double remoteTime, ref double interpolationTime, SortedList<double, Snapshot> buffer)
        {
            //Debug.Log($"{name} snapshotbuffer={buffer.Count}");

            // we buffer snapshots for 'bufferTime'
            // for example:
            //   * we buffer for 3 x sendInterval = 300ms
            //   * the idea is to wait long enough so we at least have a few
            //     snapshots to interpolate between
            //   * we process anything older 100ms immediately
            //
            // IMPORTANT: snapshot timestamps are _remote_ time
            // we need to interpolate and calculate buffer lifetimes based on it.
            // -> we don't know remote's current time
            // -> NetworkTime.time fluctuates too much, that's no good
            // -> we _could_ calculate an offset when the first snapshot arrives,
            //    but if there was high latency then we'll always calculate time
            //    with high latency
            // -> at any given time, we are interpolating from snapshot A to B
            // => seems like A.timestamp += deltaTime is a good way to do it

            // if remote time wasn't initialized yet
            if (remoteTime == 0)
            {
                // then set it to first snapshot received (if any)
                if (buffer.Count > 0)
                {
                    Snapshot first = buffer.Values[0];
                    remoteTime = first.timestamp;
                    Debug.Log("remoteTime initialized to " + first.timestamp);
                }
                // otherwise wait for the first one
                else return;
            }

            // move remote time along deltaTime
            // TODO we don't have Time.deltaTime double (yet). float delta is fine.
            // (probably need to speed this up based on buffer size later)
            remoteTime += Time.deltaTime;

            // interpolation always requires at least two snapshots
            if (buffer.Count >= 2)
            {
                Snapshot first = buffer.Values[0];
                Snapshot second = buffer.Values[1];

                // and they both need to be older than bufferTime
                // (because we always buffer for 'bufferTime' seconds first)
                // (second is always older than first. only check second's time)
                double threshold = remoteTime - bufferTime;
                if (second.timestamp <= threshold)
                {
                    // we can't use remoteTime for interpolation because we always
                    // interpolate on two old snapshots.
                    //   | first.time | second.time | remoteTime |
                    // translating remoteTime - bufferTime into the past isn't exact.
                    // let's keep a separate interpolation time that is set when the
                    // interpolation starts
                    // TODO we don't have Time.deltaTime double (yet). float delta is fine.
                    interpolationTime += Time.deltaTime;

                    // delta time is needed a lot
                    double delta = second.timestamp - first.timestamp;

                    // if interpolation time is already >= delta, then remove
                    // the snapshot BEFORE we interpolate.
                    // otherwise we might:
                    // * overshoot the interpolation to 'second' because t > 1
                    // * see jitter where InverseLerp clamps t > 1 to t = 1
                    //   and we miss out on some smooth movement
                    if (interpolationTime >= delta)
                    {
                        // we can only interpolate between the next two, if
                        // there are actually two remaining after removing one
                        if (buffer.Count >= 3)
                        {
                            // subtract exactly delta from interpolation time
                            // instead of setting to '0', where we would lose the
                            // overshoot part and see jitter again.
                            interpolationTime -= delta;
                            //Debug.LogWarning($"{name} overshot and is now at: {interpolationTime}");

                            // remove first one from buffer
                            buffer.RemoveAt(0);

                            // reassign first, second
                            first = buffer.Values[0];
                            second = buffer.Values[1];

                            // TODO what if we overshoot more than one? handle that too.
                        }
                        // TODO otherwise what?
                        //      extrapolate and hope for the best?
                        //      don't interpolate anymore because it would overshoot?
                    }

                    // first, second, interpolationTime are all absolute values.
                    // inverse lerp calculate relative 't' interpolation factor.
                    // TODO store 't' directly instead of all this magic. or not.
                    // IMPORTANT: this clamps. but we already handle overshoot
                    //            above
                    double t = Mathd.InverseLerp(first.timestamp, second.timestamp, first.timestamp + interpolationTime);

                    // TODO catchup

                    //Debug.Log($"{name} first={first.timestamp:F2} second={second.timestamp:F2} remoteTime={remoteTime:F2} interpolationTime={interpolationTime:F2} t={t:F2} snapshotbuffer={buffer.Count}");

                    // interpolate snapshot
                    Snapshot interpolated = SnapshotInterpolation.Interpolate(first, second, t);

                    // apply snapshot
                    ApplySnapshot(interpolated);

                    // TODO should we set remoteTime = second.time for precision?
                    // probably better not. we are not exactly at second.time.
                }
            }
        }

        // cmd /////////////////////////////////////////////////////////////////
        // Cmds for both channels depending on configuration
        // => only send position/rotation/scale.
        //    use timestamp from batch to save bandwidth.
        [Command(channel = Channels.Reliable)]
        void CmdClientToServerSync_Reliable(SnapshotTransform snapshotTransform) => OnClientToServerSync(snapshotTransform);
        [Command(channel = Channels.Unreliable)]
        void CmdClientToServerSync_Unreliable(SnapshotTransform snapshotTransform) => OnClientToServerSync(snapshotTransform);

        // local authority client sends sync message to server for broadcasting
        void OnClientToServerSync(SnapshotTransform snapshotTransform)
        {
            // apply if in client authority mode
            if (clientAuthority)
            {
                // only player owned objects (with a connection) can send to
                // server. we can get the timestamp from the connection.
                double timestamp = connectionToClient.remoteTimeStamp;

                // construct snapshot with batch timestamp to save bandwidth
                Snapshot snapshot = new Snapshot(
                    timestamp,
                    snapshotTransform.position,
                    snapshotTransform.rotation,
                    snapshotTransform.scale
                );

                // add to buffer (or drop if older than first element)
                SnapshotInterpolation.InsertIfNewEnough(snapshot, serverBuffer);
            }
        }

        // rpc /////////////////////////////////////////////////////////////////
        // Rpcs for both channels depending on configuration
        [ClientRpc(channel = Channels.Reliable)]
        void RpcServerToClientSync_Reliable(SnapshotTransform snapshotTransform) => OnServerToClientSync(snapshotTransform);
        [ClientRpc(channel = Channels.Unreliable)]
        void RpcServerToClientSync_Unreliable(SnapshotTransform snapshotTransform) => OnServerToClientSync(snapshotTransform);

        // server broadcasts sync message to all clients
        void OnServerToClientSync(SnapshotTransform snapshotTransform)
        {
            // in host mode, the server sends rpcs to all clients.
            // the host client itself will receive them too.
            // -> host server is always the source of truth
            // -> we can ignore any rpc on the host client
            // => otherwise host objects would have ever growing clientBuffers
            // (rpc goes to clients. if isServer is true too then we are host)
            if (isServer) return;

            // apply for all objects except local player with authority
            if (!IsClientWithAuthority)
            {
                // on the client, we receive rpcs for all entities.
                // not all of them have a connectionToServer.
                // but all of them go through NetworkClient.connection.
                // we can get the timestamp from there.
                double timestamp = NetworkClient.connection.remoteTimeStamp;

                // construct snapshot with batch timestamp to save bandwidth
                Snapshot snapshot = new Snapshot(
                    timestamp,
                    snapshotTransform.position,
                    snapshotTransform.rotation,
                    snapshotTransform.scale
                );

                // add to buffer (or drop if older than first element)
                SnapshotInterpolation.InsertIfNewEnough(snapshot, clientBuffer);
            }
        }

        // update //////////////////////////////////////////////////////////////
        void UpdateServer()
        {
            // broadcast to all clients each 'sendInterval'
            // (client with authority will drop the rpc)
            // NetworkTime.localTime for double precision until Unity has it too
            if (NetworkTime.localTime >= lastServerSendTime + sendInterval)
            {
                Snapshot snapshot = ConstructSnapshot();

                // send snapshot without timestamp.
                // receiver gets it from batch timestamp to save bandwidth.
                if (channelId == Channels.Reliable)
                    RpcServerToClientSync_Reliable(snapshot.transform);
                else
                    RpcServerToClientSync_Unreliable(snapshot.transform);

                lastServerSendTime = NetworkTime.localTime;
            }

            // apply buffered snapshots IF client authority
            // -> in server authority, server moves the object
            //    so no need to apply any snapshots there.
            // -> don't apply for host mode player either, even if in
            //    client authority mode. if it doesn't go over the network,
            //    then we don't need to do anything.
            if (clientAuthority && !isLocalPlayer)
            {
                // apply snapshots
                ApplySnapshots(ref serverRemoteClientTime, ref serverInterpolationTime, serverBuffer);
            }
        }

        void UpdateClient()
        {
            // client authority, and local player (= allowed to move myself)?
            if (IsClientWithAuthority)
            {
                // send to server each 'sendInterval'
                // NetworkTime.localTime for double precision until Unity has it too
                if (NetworkTime.localTime >= lastClientSendTime + sendInterval)
                {
                    Snapshot snapshot = ConstructSnapshot();

                    // send snapshot without timestamp.
                    // receiver gets it from batch timestamp to save bandwidth.
                    if (channelId == Channels.Reliable)
                        CmdClientToServerSync_Reliable(snapshot.transform);
                    else
                        CmdClientToServerSync_Unreliable(snapshot.transform);

                    lastClientSendTime = NetworkTime.localTime;
                }
            }
            // for all other clients (and for local player if !authority),
            // we need to apply snapshots from the buffer
            else
            {
                // apply snapshots
                ApplySnapshots(ref clientRemoteServerTime, ref clientInterpolationTime, clientBuffer);
            }
        }

        void Update()
        {
            // if server then always sync to others.
            if (isServer) UpdateServer();
            // 'else if' because host mode shouldn't send anything to server.
            // it is the server. don't overwrite anything there.
            else if (isClient) UpdateClient();
        }

        void Reset()
        {
            // disabled objects aren't updated anymore.
            // so let's clear the buffers.
            serverBuffer.Clear();
            clientBuffer.Clear();

            // and reset remoteTime so it's initialized to first snapshot again
            clientRemoteServerTime = 0;
            serverRemoteClientTime = 0;
        }

        void OnDisable() => Reset();
        void OnEnable() => Reset();

        // debug ///////////////////////////////////////////////////////////////
        void OnGUI()
        {
            if (!showOverlay) return;

            // show data next to player for easier debugging. this is very useful!
            // IMPORTANT: this is basically an ESP hack for shooter games.
            //            DO NOT make this available with a hotkey in release builds
            if (Debug.isDebugBuild)
            {
                // project position to screen
                Vector3 point = Camera.main.WorldToScreenPoint(transform.position);

                // enough alpha, in front of camera and in screen?
                if (point.z >= 0 && Utils.IsPointInScreen(point))
                {
                    GUI.color = overlayColor;
                    GUILayout.BeginArea(new Rect(point.x, Screen.height - point.y, 160, 100));
                    GUILayout.Label($"NT SB:{serverBuffer.Count} CB:{clientBuffer.Count}");
                    GUILayout.EndArea();
                    GUI.color = Color.white;
                }
            }
        }

        void DrawGizmos(SortedList<double, Snapshot> buffer)
        {
            // draw start if we have at least two entries
            if (buffer.Count >= 2)
            {
                // start: transparent white
                Snapshot start = buffer.Values[0];
                Gizmos.color = new Color(1, 1, 1, 0.5f);
                Gizmos.DrawCube(start.transform.position, Vector3.one);

                // line: start to position
                Gizmos.DrawLine(start.transform.position, transform.position);

                // goal: transparent green
                Snapshot goal = buffer.Values[1];
                Gizmos.color = new Color(0, 1, 0, 0.5f);
                Gizmos.DrawCube(goal.transform.position, Vector3.one);

                // line: position to goal
                Gizmos.DrawLine(transform.position, goal.transform.position);

                // draw the whole buffer for easier debugging.
                // it's worth seeing how much we have buffered ahead already
                for (int i = 2; i < buffer.Count; ++i)
                {
                    // transparent gray
                    Snapshot entry = buffer.Values[i];
                    Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                    Gizmos.DrawCube(entry.transform.position, Vector3.one);
                }
            }
        }

        void OnDrawGizmos()
        {
            if (!showGizmos) return;

            if (isServer) DrawGizmos(serverBuffer);
            if (isClient) DrawGizmos(clientBuffer);
        }
    }
}
