// snapshot interpolation algorithms only,
// independent from Unity/NetworkTransform/MonoBehaviour/Mirror/etc.
// the goal is to remove all the magic from it.
// => a standalone snapshot interpolation algorithm
// => that can be simulated with unit tests easily
using System.Collections.Generic;
using UnityEngine;

namespace Mirror
{
    public static class SnapshotInterpolation
    {
        // insert into snapshot buffer if newer than first entry
        // this should ALWAYS be used when inserting into a snapshot buffer!
        public static void InsertIfNewEnough(Snapshot snapshot, SortedList<double, Snapshot> buffer)
        {
            // we need to drop any snapshot which is older ('<=')
            // the snapshots we are already working with.

            // if size == 1, then we used the first one to initialize remote
            // time etc. already. so only add snapshots that are newer.
            if (buffer.Count == 1 &&
                snapshot.timestamp <= buffer.Values[0].timestamp)
                return;

            // for size >= 2, we are already interpolating between the first two
            // so only add snapshots that are newer than the second entry.
            // aka the 'ACB' problem:
            //   if we have a snapshot A at t=0 and C at t=2,
            //   we start interpolating between them.
            //   if suddenly B at t=1 comes in unexpectely,
            //   we should NOT suddenly steer towards B.
            if (buffer.Count >= 2 &&
                snapshot.timestamp <= buffer.Values[1].timestamp)
                return;

            // otherwise sort it into the list
            buffer.Add(snapshot.timestamp, snapshot);
        }

        // interpolate all components of a snapshot
        // t is interpolation step [0,1]
        //
        // unclamped for maximum transition smoothness.
        // although the caller should switch to next snapshot if t >= 1 instead
        // of calling this with a t >= 1!
        public static Snapshot Interpolate(Snapshot from, Snapshot to, double t)
        {
            // NOTE:
            // Vector3 & Quaternion components are float anyway, so we can
            // keep using the functions with 't' as float instead of double.
            return new Snapshot(
                Mathd.LerpUnclamped(from.timestamp, to.timestamp, t),
                Vector3.LerpUnclamped(from.transform.position, to.transform.position, (float)t),
                // IMPORTANT: LerpUnclamped(0, 60, 1.5) extrapolates to ~86.
                //            SlerpUnclamped(0, 60, 1.5) extrapolates to 90!
                //            (0, 90, 1.5) is even worse. for Lerp.
                //            => Slerp works way better for our euler angles.
                Quaternion.SlerpUnclamped(from.transform.rotation, to.transform.rotation, (float)t),
                Vector3.LerpUnclamped(from.transform.scale, to.transform.scale, (float)t)
            );
        }

        // the core snapshot interpolation algorithm.
        // for a given remoteTime, interpolationTime and buffer,
        // we tick the snapshot simulation once.
        // => it's the same one on server and client
        // => should be called every Update() depending on authority
        //
        // bufferTime: time in seconds that we buffer snapshots.
        // deltaTime: Time.deltaTime from Unity. parameter for easier tests.
        // remoteTime: the remote's time, moved along deltaTime every compute.
        // interpolationTime: time in interpolation. moved along deltaTime.
        // buffer: our buffer of snapshots.
        //         Compute() assumes full integrity of the snapshots.
        //         for example, when interpolating between A=0 and C=2,
        //         make sure that you don't add B=1 between A and C if that
        //         snapshot arrived after we already started interpolating.
        //      => InsertIfNewEnough needs to protect against the 'ACB' problem
        //
        // returns
        //   'true' if it spit out a snapshot to apply.
        //   'false' means computation moved along, but nothing to apply.
        public static bool Compute(
            double bufferTime,
            double deltaTime,
            ref double remoteTime,
            ref double interpolationTime,
            SortedList<double, Snapshot> buffer,
            out Snapshot computed)
        {
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

            computed = default;
            //Debug.Log($"{name} snapshotbuffer={buffer.Count}");

            // if remote time wasn't initialized yet
            if (remoteTime == 0)
            {
                // then set it to first snapshot received (if any)
                if (buffer.Count > 0)
                {
                    Snapshot first = buffer.Values[0];
                    remoteTime = first.timestamp;
                    //Debug.Log("remoteTime initialized to " + first.timestamp);
                }
                // otherwise wait for the first one
                else return false;
            }

            // move remote time along deltaTime
            // (probably need to speed this up based on buffer size later)
            remoteTime += deltaTime;

            // interpolation always requires at least two snapshots
            if (buffer.Count >= 2)
            {
                Snapshot first = buffer.Values[0];
                Snapshot second = buffer.Values[1];

                // and they both need to be older than bufferTime
                // (because we always buffer for 'bufferTime' seconds first)
                // => first is always older than second
                // => only check if second is old enough
                // => by definition, first is older anyway
                double threshold = remoteTime - bufferTime;
                //Debug.Log($"second timestamp={second.timestamp} threshold={threshold} because remoteTime={remoteTime} - bufferTime={bufferTime}");
                if (second.timestamp <= threshold)
                {
                    // we can't use remoteTime for interpolation because we always
                    // interpolate on two old snapshots.
                    //   | first.time | second.time | remoteTime |
                    // translating remoteTime - bufferTime into the past isn't exact.
                    // let's keep a separate interpolation time that is set when the
                    // interpolation starts
                    interpolationTime += deltaTime;

                    // delta is needed a lot
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
                        // otherwise we do nothing.
                        // best bet is to assume that we keep moving into the
                        // same direction, aka extrapolate further.
                    }

                    // first, second, interpolationTime are all absolute values.
                    // inverse lerp calculate relative 't' interpolation factor.
                    // TODO store 't' directly instead of all this magic. or not.
                    //
                    // IMPORTANT: InverseLerp CLAMPS t [0,1] => NO EXTRAPOLATION
                    //            InverseLerpUnclamped extrapolates.
                    //            => if we don't have additional snapshots,
                    //               extrapolation is the best guess!
                    //
                    //Debug.Log($"InverseLerp({first.timestamp}, {second.timestamp}, {first.timestamp} + {interpolationTime})");
                    double t = Mathd.InverseLerpUnclamped(first.timestamp, second.timestamp, first.timestamp + interpolationTime);
                    //Debug.Log($"first={first.timestamp:F2} second={second.timestamp:F2} remoteTime={remoteTime:F2} interpolationTime={interpolationTime:F2} t={t:F2} snapshotbuffer={buffer.Count}");

                    // TODO catchup

                    // interpolate snapshot
                    computed = Interpolate(first, second, t);

                    // TODO should we set remoteTime = second.time for precision?
                    // probably better not. we are not exactly at second.time.

                    // a new snapshot was computed to be applied
                    return true;
                }
            }

            // no new snapshot was computed
            return false;
        }
    }
}
