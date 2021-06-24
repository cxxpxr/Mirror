using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace Mirror.Tests.NetworkTransform2k
{
    public class SnapshotInterpolationTests
    {
        // buffer for convenience so we don't have to create it manually each time
        SortedList<double, Snapshot> buffer;

        [SetUp]
        public void SetUp()
        {
            buffer = new SortedList<double, Snapshot>();
        }

        [Test]
        public void InsertIfNewEnough()
        {
            // inserting a first value should always work
            Snapshot first = new Snapshot(1, Vector3.zero, Quaternion.identity, Vector3.one);
            SnapshotInterpolation.InsertIfNewEnough(first, buffer);
            Assert.That(buffer.Count, Is.EqualTo(1));

            // insert before first should not work
            Snapshot before = new Snapshot(0.5, Vector3.zero, Quaternion.identity, Vector3.one);
            SnapshotInterpolation.InsertIfNewEnough(before, buffer);
            Assert.That(buffer.Count, Is.EqualTo(1));

            // insert after first should work
            Snapshot second = new Snapshot(2, Vector3.left, Quaternion.identity, Vector3.right);
            SnapshotInterpolation.InsertIfNewEnough(second, buffer);
            Assert.That(buffer.Count, Is.EqualTo(2));
            Assert.That(buffer.Values[0], Is.EqualTo(first));
            Assert.That(buffer.Values[1], Is.EqualTo(second));

            // insert after second should work
            Snapshot after = new Snapshot(2.5, Vector3.zero, Quaternion.identity, Vector3.one);
            SnapshotInterpolation.InsertIfNewEnough(after, buffer);
            Assert.That(buffer.Count, Is.EqualTo(3));
            Assert.That(buffer.Values[0], Is.EqualTo(first));
            Assert.That(buffer.Values[1], Is.EqualTo(second));
            Assert.That(buffer.Values[2], Is.EqualTo(after));
        }

        // the 'ACB' problem:
        //   if we have a snapshot A at t=0 and C at t=2,
        //   we start interpolating between them.
        //   if suddenly B at t=1 comes in unexpectely,
        //   we should NOT suddenly steer towards B.
        // => inserting between the first two snapshot should never be allowed
        //    in order to avoid all kinds of edge cases.
        [Test]
        public void InsertIfNewEnough_ACB_Problem()
        {
            Snapshot a = new Snapshot(0, Vector3.zero, Quaternion.identity, Vector3.one);
            Snapshot b = new Snapshot(1, Vector3.zero, Quaternion.identity, Vector3.one);
            Snapshot c = new Snapshot(2, Vector3.zero, Quaternion.identity, Vector3.one);

            // insert A and C
            SnapshotInterpolation.InsertIfNewEnough(a, buffer);
            SnapshotInterpolation.InsertIfNewEnough(c, buffer);

            // trying to insert B between the first two snapshots shoudl fail
            SnapshotInterpolation.InsertIfNewEnough(b, buffer);
            Assert.That(buffer.Count, Is.EqualTo(2));
            Assert.That(buffer.Values[0], Is.EqualTo(a));
            Assert.That(buffer.Values[1], Is.EqualTo(c));
        }

        [Test]
        public void Interpolate()
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
            Snapshot between = SnapshotInterpolation.Interpolate(from, to, 0.5);

            // check time
            Assert.That(between.timestamp, Is.EqualTo(1.5).Within(Mathf.Epsilon));

            // check position
            Assert.That(between.transform.position.x, Is.EqualTo(1.5).Within(Mathf.Epsilon));
            Assert.That(between.transform.position.y, Is.EqualTo(1.5).Within(Mathf.Epsilon));
            Assert.That(between.transform.position.z, Is.EqualTo(1.5).Within(Mathf.Epsilon));

            // check rotation
            // (epsilon is slightly too small)
            Assert.That(between.transform.rotation.eulerAngles.x, Is.EqualTo(0).Within(Mathf.Epsilon));
            Assert.That(between.transform.rotation.eulerAngles.y, Is.EqualTo(45).Within(0.001));
            Assert.That(between.transform.rotation.eulerAngles.z, Is.EqualTo(0).Within(Mathf.Epsilon));

            // check scale
            Assert.That(between.transform.scale.x, Is.EqualTo(3.5).Within(Mathf.Epsilon));
            Assert.That(between.transform.scale.y, Is.EqualTo(3.5).Within(Mathf.Epsilon));
            Assert.That(between.transform.scale.z, Is.EqualTo(3.5).Within(Mathf.Epsilon));
        }

        // our interpolation should be capable of extrapolating beyond t=[0,1]
        // if necessary. especially for quaternion interpolation, this is not
        // obvious.
        [Test]
        public void Interpolate_Extrapolates()
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
                Quaternion.Euler(new Vector3(0, 60, 0)),
                new Vector3(4, 4, 4)
            );

            // interpolate with t=1.5, so it extrapolates by 1.5
            Snapshot between = SnapshotInterpolation.Interpolate(from, to, 1.5);

            // check time
            Assert.That(between.timestamp, Is.EqualTo(2.5).Within(Mathf.Epsilon));

            // check position
            Assert.That(between.transform.position.x, Is.EqualTo(2.5).Within(Mathf.Epsilon));
            Assert.That(between.transform.position.y, Is.EqualTo(2.5).Within(Mathf.Epsilon));
            Assert.That(between.transform.position.z, Is.EqualTo(2.5).Within(Mathf.Epsilon));

            // check rotation
            // IMPORTANT: Quaternion.LerpUnclamped gives ~86.
            //            Quaternion.SlerpUnclamped gives 90!
            //            => Slerp is better for our eule rangle interpolation.
            Assert.That(between.transform.rotation.eulerAngles.x, Is.EqualTo(0).Within(Mathf.Epsilon));
            Assert.That(between.transform.rotation.eulerAngles.y, Is.EqualTo(90).Within(Mathf.Epsilon));
            Assert.That(between.transform.rotation.eulerAngles.z, Is.EqualTo(0).Within(Mathf.Epsilon));

            // check scale
            Assert.That(between.transform.scale.x, Is.EqualTo(4.5).Within(Mathf.Epsilon));
            Assert.That(between.transform.scale.y, Is.EqualTo(4.5).Within(Mathf.Epsilon));
            Assert.That(between.transform.scale.z, Is.EqualTo(4.5).Within(Mathf.Epsilon));
        }

        // first step: with empty buffer and defaults, nothing should happen
        [Test]
        public void Compute_Step1_DefaultDoesNothing()
        {
            // compute with defaults
            float bufferTime = 0;
            double deltaTime = 0;
            double remoteTime = 0;
            double interpolationTime = 0;
            bool result = SnapshotInterpolation.Compute(bufferTime, deltaTime, ref remoteTime, ref interpolationTime, buffer, out Snapshot computed);

            // should not spit out any snapshot to apply
            Assert.That(result, Is.False);
            // parameters should all be untouched
            Assert.That(remoteTime, Is.EqualTo(0));
            // no interpolation should have happened yet
            Assert.That(interpolationTime, Is.EqualTo(0));
            // buffer should still be untouched
            Assert.That(buffer.Count, Is.EqualTo(0));
        }

        // second step: compute is supposed to initialize remote time as soon as
        //             the first buffer entry arrived
        [Test]
        public void Compute_Step2_FirstSnapshotInitializesRemoteTime()
        {
            // add first snapshot
            Snapshot first = new Snapshot(1, Vector3.zero, Quaternion.identity, Vector3.one);
            buffer.Add(first.timestamp, first);

            // compute with defaults except for a deltaTime
            float bufferTime = 0;
            double deltaTime = 0.5;
            double remoteTime = 0;
            double interpolationTime = 0;
            bool result = SnapshotInterpolation.Compute(bufferTime, deltaTime, ref remoteTime, ref interpolationTime, buffer, out Snapshot computed);

            // should not spit out any snapshot to apply
            Assert.That(result, Is.False);
            // remote time be initialize to first and moved along deltaTime
            Assert.That(remoteTime, Is.EqualTo(first.timestamp + deltaTime));
            // no interpolation should have happened yet
            Assert.That(interpolationTime, Is.EqualTo(0));
            // buffer should be untouched
            Assert.That(buffer.Count, Is.EqualTo(1));
        }

        // third step: compute should always wait until the first two snapshots
        //             are older than the time we buffer ('bufferTime')
        //             => test for both snapshots not old enough
        [Test]
        public void Compute_Step3_WaitsUntilBufferTime()
        {
            // with remoteTime = 2.5 and delta of 0.5,
            // compute sets remoteTime = 3.
            // bufferTime = 2.
            // so the threshold is bufferTime-remoteTime = 1.
            // => everything has to be older than 1 (aka <= 1)
            // => our buffers are 0.1 and 1.1, so not old enough just yet.
            Snapshot first = new Snapshot(0.1, Vector3.zero, Quaternion.identity, Vector3.one);
            Snapshot second = new Snapshot(1.1, Vector3.zero, Quaternion.identity, Vector3.one);
            buffer.Add(first.timestamp, first);
            buffer.Add(second.timestamp, second);

            // compute with initialized remoteTime and buffer time of 2 seconds
            // and a delta time to be sure that we move along it no matter what.
            float bufferTime = 2;
            double deltaTime = 0.5;
            double remoteTime = 2.5;
            double interpolationTime = 0;
            bool result = SnapshotInterpolation.Compute(bufferTime, deltaTime, ref remoteTime, ref interpolationTime, buffer, out Snapshot computed);

            // should not spit out any snapshot to apply
            Assert.That(result, Is.False);
            // remote time should be moved along deltaTime
            Assert.That(remoteTime, Is.EqualTo(2.5 + 0.5));
            // no interpolation should happen yet (not old enough)
            Assert.That(interpolationTime, Is.EqualTo(0));
            // buffer should be untouched
            Assert.That(buffer.Count, Is.EqualTo(2));
        }

        // third step: compute should always wait until the first two snapshots
        //             are older than the time we buffer ('bufferTime')
        //             => test for only one snapshot which is old enough
        [Test]
        public void Compute_Step3_WaitsUntilTwoOldEnoughSnapshots()
        {
            // add a snapshot at t=0
            Snapshot first = new Snapshot(0, Vector3.zero, Quaternion.identity, Vector3.one);
            buffer.Add(first.timestamp, first);

            // compute at remoteTime = 2 with bufferTime = 1
            // so the threshold is anything < t=1
            float bufferTime = 1;
            double deltaTime = 0;
            double remoteTime = 2;
            double interpolationTime = 0;
            bool result = SnapshotInterpolation.Compute(bufferTime, deltaTime, ref remoteTime, ref interpolationTime, buffer, out Snapshot computed);

            // should not spit out any snapshot to apply
            Assert.That(result, Is.False);
            // remoteTime should be same as before. deltaTime is 0.
            Assert.That(remoteTime, Is.EqualTo(2));
            // no interpolation should happen yet (not enough snapshots)
            Assert.That(interpolationTime, Is.EqualTo(0));
            // buffer should be untouched
            Assert.That(buffer.Count, Is.EqualTo(1));
        }

        // fourth step: compute should begin if we have two old enough snapshots
        [Test]
        public void Compute_Step4_InterpolateWithTwoOldEnoughSnapshots()
        {
            // with remoteTime = 2.5 and delta of 0.5,
            // compute sets remoteTime = 3.
            // bufferTime = 2.
            // so the threshold is bufferTime-remoteTime = 1.
            // => everything has to be older than 1 (aka <= 1)
            // => first at '0' is old enough
            // => second at '1' is _exactly_ old enough via <=
            Snapshot first = new Snapshot(0, new Vector3(1, 1, 1), Quaternion.Euler(new Vector3(0, 0, 0)), new Vector3(3, 3, 3));
            Snapshot second = new Snapshot(1, new Vector3(2, 2, 2), Quaternion.Euler(new Vector3(0, 60, 0)), new Vector3(4, 4, 4));
            buffer.Add(first.timestamp, first);
            buffer.Add(second.timestamp, second);

            // compute with initialized remoteTime and buffer time of 2 seconds
            // and a delta time to be sure that we move along it no matter what.
            float bufferTime = 2;
            double deltaTime = 0.5;
            double remoteTime = 2.5;
            double interpolationTime = 0;
            bool result = SnapshotInterpolation.Compute(bufferTime, deltaTime, ref remoteTime, ref interpolationTime, buffer, out Snapshot computed);

            // should spit out the interpolated snapshot
            Assert.That(result, Is.True);
            // remote time should be moved along deltaTime
            Assert.That(remoteTime, Is.EqualTo(2.5 + 0.5));
            // interpolation started just now, from 0.
            // and deltaTime is 0.5, so we should be at 0.5 now.
            Assert.That(interpolationTime, Is.EqualTo(0.5));
            // buffer should be untouched, we are still interpolating between the two
            Assert.That(buffer.Count, Is.EqualTo(2));
            // computed snapshot should be interpolated in the middle
            // check position
            Assert.That(computed.transform.position.x, Is.EqualTo(1.5).Within(Mathf.Epsilon));
            Assert.That(computed.transform.position.y, Is.EqualTo(1.5).Within(Mathf.Epsilon));
            Assert.That(computed.transform.position.z, Is.EqualTo(1.5).Within(Mathf.Epsilon));
            // check rotation (epsilon is not enough, it's slightly more off)
            Assert.That(computed.transform.rotation.eulerAngles.x, Is.EqualTo(0).Within(Mathf.Epsilon));
            Assert.That(computed.transform.rotation.eulerAngles.y, Is.EqualTo(30).Within(0.001));
            Assert.That(computed.transform.rotation.eulerAngles.z, Is.EqualTo(0).Within(Mathf.Epsilon));
            // check scale
            Assert.That(computed.transform.scale.x, Is.EqualTo(3.5).Within(Mathf.Epsilon));
            Assert.That(computed.transform.scale.y, Is.EqualTo(3.5).Within(Mathf.Epsilon));
            Assert.That(computed.transform.scale.z, Is.EqualTo(3.5).Within(Mathf.Epsilon));
        }

        // fourth step: compute should begin if we have two old enough snapshots
        //              => test with 3 snapshots to make sure the third one
        //                 isn't touched while t between [0,1]
        [Test]
        public void Compute_Step4_InterpolateWithThreeOldEnoughSnapshots()
        {
            // with remoteTime = 3.5 and delta of 0.5,
            // compute sets remoteTime = 4.
            // bufferTime = 2.
            // so the threshold is bufferTime-remoteTime = 2.
            // => everything has to be older than 2 (aka <= 2)
            // => first at '0' is old enough
            // => second at '1' is old enough
            // => third at '2' is _exactly_ old enough via <=
            Snapshot first = new Snapshot(0, new Vector3(1, 1, 1), Quaternion.Euler(new Vector3(0, 0, 0)), new Vector3(3, 3, 3));
            Snapshot second = new Snapshot(1, new Vector3(2, 2, 2), Quaternion.Euler(new Vector3(0, 60, 0)), new Vector3(4, 4, 4));
            Snapshot third = new Snapshot(2, new Vector3(2, 2, 2), Quaternion.Euler(new Vector3(0, 60, 0)), new Vector3(4, 4, 4));
            buffer.Add(first.timestamp, first);
            buffer.Add(second.timestamp, second);
            buffer.Add(third.timestamp, third);

            // compute with initialized remoteTime and buffer time of 2 seconds
            // and a delta time to be sure that we move along it no matter what.
            float bufferTime = 2;
            double deltaTime = 0.5;
            double remoteTime = 3.5;
            double interpolationTime = 0;
            bool result = SnapshotInterpolation.Compute(bufferTime, deltaTime, ref remoteTime, ref interpolationTime, buffer, out Snapshot computed);

            // should spit out the interpolated snapshot
            Assert.That(result, Is.True);
            // remote time should be moved along deltaTime
            Assert.That(remoteTime, Is.EqualTo(3.5 + 0.5));
            // interpolation started just now, from 0.
            // and deltaTime is 0.5, so we should be at 0.5 now.
            Assert.That(interpolationTime, Is.EqualTo(0.5));
            // buffer should be untouched, we are still interpolating between
            // the first two. third should still be there.
            Assert.That(buffer.Count, Is.EqualTo(3));
            // computed snapshot should be interpolated in the middle
            // check position
            Assert.That(computed.transform.position.x, Is.EqualTo(1.5).Within(Mathf.Epsilon));
            Assert.That(computed.transform.position.y, Is.EqualTo(1.5).Within(Mathf.Epsilon));
            Assert.That(computed.transform.position.z, Is.EqualTo(1.5).Within(Mathf.Epsilon));
            // check rotation (epsilon is not enough, it's slightly more off)
            Assert.That(computed.transform.rotation.eulerAngles.x, Is.EqualTo(0).Within(Mathf.Epsilon));
            Assert.That(computed.transform.rotation.eulerAngles.y, Is.EqualTo(30).Within(0.001));
            Assert.That(computed.transform.rotation.eulerAngles.z, Is.EqualTo(0).Within(Mathf.Epsilon));
            // check scale
            Assert.That(computed.transform.scale.x, Is.EqualTo(3.5).Within(Mathf.Epsilon));
            Assert.That(computed.transform.scale.y, Is.EqualTo(3.5).Within(Mathf.Epsilon));
            Assert.That(computed.transform.scale.z, Is.EqualTo(3.5).Within(Mathf.Epsilon));
        }

        // fifth step: interpolation time overshoots the end
        //             => test without additional snapshots first
        [Test]
        public void Compute_Step5_ExtrapolateWithoutMoreSnapshots()
        {
            // with remoteTime = 2.5 and delta of 0.5,
            // compute sets remoteTime = 3.
            // bufferTime = 2.
            // so the threshold is bufferTime-remoteTime = 1.
            // => everything has to be older than 1 (aka <= 1)
            // => first at '0' is old enough
            // => second at '1' is _exactly_ old enough via <=
            Snapshot first = new Snapshot(0, new Vector3(1, 1, 1), Quaternion.Euler(new Vector3(0, 0, 0)), new Vector3(3, 3, 3));
            Snapshot second = new Snapshot(1, new Vector3(2, 2, 2), Quaternion.Euler(new Vector3(0, 60, 0)), new Vector3(4, 4, 4));
            buffer.Add(first.timestamp, first);
            buffer.Add(second.timestamp, second);

            // compute with initialized remoteTime and buffer time of 2 seconds
            // and a delta time to be sure that we move along it no matter what.
            // -> interpolation time is already at '1' at the end.
            // -> compute will add 0.5 deltaTime
            // -> so we should overshoot aka extrapolate between first & second
            float bufferTime = 2;
            double deltaTime = 0.5;
            double remoteTime = 2.5;
            double interpolationTime = 1;
            bool result = SnapshotInterpolation.Compute(bufferTime, deltaTime, ref remoteTime, ref interpolationTime, buffer, out Snapshot computed);

            // should spit out the interpolated snapshot
            Assert.That(result, Is.True);
            // remote time should be moved along deltaTime
            Assert.That(remoteTime, Is.EqualTo(2.5 + 0.5));
            // interpolation started at the end = 1
            // and deltaTime is 0.5, so we should be at 1.5 now.
            Assert.That(interpolationTime, Is.EqualTo(1.5));
            // buffer should be untouched, we are still interpolating between the two
            Assert.That(buffer.Count, Is.EqualTo(2));
            // computed snapshot should be extrapolated by one and a half
            // check position
            Assert.That(computed.transform.position.x, Is.EqualTo(2.5).Within(Mathf.Epsilon));
            Assert.That(computed.transform.position.y, Is.EqualTo(2.5).Within(Mathf.Epsilon));
            Assert.That(computed.transform.position.z, Is.EqualTo(2.5).Within(Mathf.Epsilon));
            // check rotation
            Assert.That(computed.transform.rotation.eulerAngles.x, Is.EqualTo(0).Within(Mathf.Epsilon));
            Assert.That(computed.transform.rotation.eulerAngles.y, Is.EqualTo(90).Within(Mathf.Epsilon));
            Assert.That(computed.transform.rotation.eulerAngles.z, Is.EqualTo(0).Within(Mathf.Epsilon));
            // check scale
            Assert.That(computed.transform.scale.x, Is.EqualTo(4.5).Within(Mathf.Epsilon));
            Assert.That(computed.transform.scale.y, Is.EqualTo(4.5).Within(Mathf.Epsilon));
            Assert.That(computed.transform.scale.z, Is.EqualTo(4.5).Within(Mathf.Epsilon));
        }

        // fifth step: interpolation time overshoots the end
        //             => test with additional snapshots now
        [Test]
        public void Compute_Step5_ExtrapolateWithMoreSnapshots()
        {
            // with remoteTime = 2.5 and delta of 0.5,
            // compute sets remoteTime = 3.
            // bufferTime = 2.
            // so the threshold is bufferTime-remoteTime = 1.
            // => everything has to be older than 1 (aka <= 1)
            // => first at '0' is old enough
            // => second at '1' is _exactly_ old enough via <=
            Snapshot first = new Snapshot(0, new Vector3(1, 1, 1), Quaternion.Euler(new Vector3(0, 0, 0)), new Vector3(3, 3, 3));
            Snapshot second = new Snapshot(1, new Vector3(2, 2, 2), Quaternion.Euler(new Vector3(0, 60, 0)), new Vector3(4, 4, 4));
            // third snapshot should be a different direction so we can check
            // if it really interpolates between second and third, and not just
            // extrapolates between first and second.
            // => simply double the value difference
            Snapshot third = new Snapshot(2, new Vector3(4, 4, 4), Quaternion.Euler(new Vector3(0, 120, 0)), new Vector3(6, 6, 6));
            buffer.Add(first.timestamp, first);
            buffer.Add(second.timestamp, second);
            buffer.Add(third.timestamp, third);

            // compute with initialized remoteTime and buffer time of 2 seconds
            // and a delta time to be sure that we move along it no matter what.
            // -> interpolation time is already at '1' at the end.
            // -> compute will add 0.5 deltaTime
            // -> so we should overshoot aka extrapolate between first & second
            float bufferTime = 2;
            double deltaTime = 0.5;
            double remoteTime = 2.5;
            double interpolationTime = 1;
            bool result = SnapshotInterpolation.Compute(bufferTime, deltaTime, ref remoteTime, ref interpolationTime, buffer, out Snapshot computed);

            // should spit out the interpolated snapshot
            Assert.That(result, Is.True);
            // remote time should be moved along deltaTime
            Assert.That(remoteTime, Is.EqualTo(2.5 + 0.5));
            // interpolation started at the end = 1
            // and deltaTime is 0.5, so we were at 1.5 internally.
            // we have more snapshots, so we jump to the next and subtract '1'
            // 1 + 0.5 = 1.5 => -1 => 0.5
            Assert.That(interpolationTime, Is.EqualTo(0.5));
            // buffer's first entry should have been removed
            Assert.That(buffer.Count, Is.EqualTo(2));
            // computed snapshot should be half way between second and third
            Assert.That(computed.transform.position.x, Is.EqualTo(3).Within(Mathf.Epsilon));
            Assert.That(computed.transform.position.y, Is.EqualTo(3).Within(Mathf.Epsilon));
            Assert.That(computed.transform.position.z, Is.EqualTo(3).Within(Mathf.Epsilon));
            // check rotation (epsilon is a bit too small)
            Assert.That(computed.transform.rotation.eulerAngles.x, Is.EqualTo(0).Within(Mathf.Epsilon));
            Assert.That(computed.transform.rotation.eulerAngles.y, Is.EqualTo(90).Within(0.001));
            Assert.That(computed.transform.rotation.eulerAngles.z, Is.EqualTo(0).Within(Mathf.Epsilon));
            // check scale
            Assert.That(computed.transform.scale.x, Is.EqualTo(5).Within(Mathf.Epsilon));
            Assert.That(computed.transform.scale.y, Is.EqualTo(5).Within(Mathf.Epsilon));
            Assert.That(computed.transform.scale.z, Is.EqualTo(5).Within(Mathf.Epsilon));
        }
    }
}
