// snapshot interpolation helper functions.
// static for easy testing.
using System.Collections.Generic;
using UnityEngine;

namespace Mirror
{
    public static class SnapshotUtils
    {
        // insert into snapshot buffer if newer than first entry
        public static void InsertIfNewEnough(Snapshot snapshot, SortedList<double, Snapshot> buffer)
        {
            // drop it if it's older than the first snapshot
            if (buffer.Count > 0 &&
                buffer.Values[0].timestamp > snapshot.timestamp)
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
                Quaternion.LerpUnclamped(from.transform.rotation, to.transform.rotation, (float)t),
                Vector3.LerpUnclamped(from.transform.scale, to.transform.scale, (float)t)
            );
        }
    }
}
