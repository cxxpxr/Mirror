// snapshot for snapshot interpolation
// https://gafferongames.com/post/snapshot_interpolation/
// position, rotation, scale for compatibility for now.
using UnityEngine;

namespace Mirror
{
    // transform part of the Snapshot so we can send it over the wire without
    // timestamp.
    public struct SnapshotTransform
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        public SnapshotTransform(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
        }
    }

    public struct Snapshot
    {
        // time or sequence are needed to throw away older snapshots.
        //
        // glenn fiedler starts with a 16 bit sequence number.
        // supposedly this is meant as a simplified example.
        // in the end we need the remote timestamp for accurate interpolation
        // and buffering over time.
        //
        // note: in theory, IF server sends exactly(!) at the same interval then
        //       the 16 bit ushort timestamp would be enough to calculate the
        //       remote time (sequence * sendInterval). but Unity's update is
        //       not guaranteed to run on the exact intervals / do catchup.
        //       => remote timestamp is better for now
        //
        // [REMOTE TIME, NOT LOCAL TIME]
        // => DOUBLE for long term accuracy & batching gives us double anyway
        public double timestamp;

        public SnapshotTransform transform;

        public Snapshot(double timestamp, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            this.timestamp = timestamp;
            this.transform = new SnapshotTransform(position, rotation, scale);
        }
    }
}
