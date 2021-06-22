using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Mirror.Tests.NetworkTransform2k
{
    public class SnapshotUtilsTests
    {
        [Test]
        public void InsertIfNewEnough()
        {
            // empty buffer
            SortedList<double, Snapshot> buffer = new SortedList<double, Snapshot>();

            // inserting a first value should always work
            Snapshot first = new Snapshot(1, Vector3.zero, Quaternion.identity, Vector3.one);
            SnapshotUtils.InsertIfNewEnough(first, buffer);
            Assert.That(buffer.Count, Is.EqualTo(1));

            // insert before first should not work
            Snapshot before = new Snapshot(0.5, Vector3.zero, Quaternion.identity, Vector3.one);
            SnapshotUtils.InsertIfNewEnough(before, buffer);
            Assert.That(buffer.Count, Is.EqualTo(1));

            // insert after first should work
            Snapshot second = new Snapshot(2, Vector3.left, Quaternion.identity, Vector3.right);
            SnapshotUtils.InsertIfNewEnough(second, buffer);
            Assert.That(buffer.Count, Is.EqualTo(2));
            Assert.That(buffer.Values[0], Is.EqualTo(first));
            Assert.That(buffer.Values[1], Is.EqualTo(second));

            // insert between first and second should work (for now)
            Snapshot between = new Snapshot(1.5, Vector3.zero, Quaternion.identity, Vector3.one);
            SnapshotUtils.InsertIfNewEnough(between, buffer);
            Assert.That(buffer.Count, Is.EqualTo(3));
            Assert.That(buffer.Values[0], Is.EqualTo(first));
            Assert.That(buffer.Values[1], Is.EqualTo(between));
            Assert.That(buffer.Values[2], Is.EqualTo(second));

            // insert after second should work
            Snapshot after = new Snapshot(2.5, Vector3.zero, Quaternion.identity, Vector3.one);
            SnapshotUtils.InsertIfNewEnough(after, buffer);
            Assert.That(buffer.Count, Is.EqualTo(4));
            Assert.That(buffer.Values[0], Is.EqualTo(first));
            Assert.That(buffer.Values[1], Is.EqualTo(between));
            Assert.That(buffer.Values[2], Is.EqualTo(second));
            Assert.That(buffer.Values[3], Is.EqualTo(after));
        }

        [Test]
        public void InterpolateSnapshot()
        {
            Snapshot from = new Snapshot(
                1,
                new Vector3(1, 1, 1),
                Quaternion.Euler(new Vector3(0, 0, 0)),
                new Vector3(3, 3, 3)
            );

            Snapshot to = new Snapshot(
                2,
                new Vector3(2, 2, 2),
                Quaternion.Euler(new Vector3(0, 90, 0)),
                new Vector3(4, 4, 4)
            );

            // interpolate
            Snapshot between = SnapshotUtils.InterpolateSnapshot(from, to, 0.5);

            // check time
            Assert.That(between.timestamp, Is.EqualTo(1.5).Within(Mathf.Epsilon));

            // check position
            Assert.That(between.transform.position.x, Is.EqualTo(1.5).Within(Mathf.Epsilon));
            Assert.That(between.transform.position.y, Is.EqualTo(1.5).Within(Mathf.Epsilon));
            Assert.That(between.transform.position.z, Is.EqualTo(1.5).Within(Mathf.Epsilon));

            // check rotation
            Assert.That(between.transform.rotation.eulerAngles.x, Is.EqualTo(0).Within(Mathf.Epsilon));
            Assert.That(between.transform.rotation.eulerAngles.y, Is.EqualTo(45).Within(Mathf.Epsilon));
            Assert.That(between.transform.rotation.eulerAngles.z, Is.EqualTo(0).Within(Mathf.Epsilon));

            // check scale
            Assert.That(between.transform.scale.x, Is.EqualTo(3.5).Within(Mathf.Epsilon));
            Assert.That(between.transform.scale.y, Is.EqualTo(3.5).Within(Mathf.Epsilon));
            Assert.That(between.transform.scale.z, Is.EqualTo(3.5).Within(Mathf.Epsilon));
        }
    }
}
