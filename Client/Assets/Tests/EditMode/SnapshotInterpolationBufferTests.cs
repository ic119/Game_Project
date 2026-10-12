using Incheol.Utils;
using NUnit.Framework;
using UnityEngine;

namespace Incheol.Tests
{
    public class SnapshotInterpolationBufferTests
    {
        private const float Tolerance = 0.001f;

        private SnapshotInterpolationBuffer buffer;

        [SetUp]
        public void CreateBuffer()
        {
            buffer = new SnapshotInterpolationBuffer();
        }

        private Vector3 SamplePosition(double renderTimeMs)
        {
            buffer.Sample(renderTimeMs, out Vector3 position, out _);
            return position;
        }

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, Tolerance);
            Assert.AreEqual(expected.y, actual.y, Tolerance);
            Assert.AreEqual(expected.z, actual.z, Tolerance);
        }

        [Test]
        public void NoSamples_ReturnsRestPosition()
        {
            buffer.Reset(new Vector3(1, 2, 3), 45f);

            buffer.Sample(1000, out Vector3 position, out float rotationY);

            AssertVector(new Vector3(1, 2, 3), position);
            Assert.AreEqual(45f, rotationY, Tolerance);
        }

        [Test]
        public void TwoSamples_InterpolatesLinearlyBetweenThem()
        {
            buffer.Reset(Vector3.zero, 0f);
            buffer.Add(1000, new Vector3(0, 0, 0), 0f);
            buffer.Add(1100, new Vector3(10, 0, 0), 0f);

            AssertVector(new Vector3(5, 0, 0), SamplePosition(1050));
            AssertVector(new Vector3(2.5f, 0, 0), SamplePosition(1025));
        }

        [Test]
        public void RenderTimeAfterNewest_StaysAtNewest_WithoutExtrapolating()
        {
            buffer.Reset(Vector3.zero, 0f);
            buffer.Add(1000, new Vector3(10, 0, 0), 0f);

            AssertVector(new Vector3(10, 0, 0), SamplePosition(5000));
        }

        [Test]
        public void RenderTimeBeforeOldest_ClampsToOldest()
        {
            buffer.Reset(new Vector3(-4, 0, 0), 0f);
            buffer.Add(1000, new Vector3(10, 0, 0), 0f);

            // 첫 샘플은 Reset 위치에서 출발하는 시작점(1000 - 50ms)이다.
            AssertVector(new Vector3(-4, 0, 0), SamplePosition(0));
        }

        [Test]
        public void FirstSample_StartsFromRestPosition()
        {
            buffer.Reset(new Vector3(0, 0, 0), 0f);
            buffer.Add(1000, new Vector3(10, 0, 0), 0f);

            // 시작점(950ms, rest)과 첫 샘플(1000ms)의 한가운데.
            AssertVector(new Vector3(5, 0, 0), SamplePosition(975));
        }

        [Test]
        public void IdleGap_InsertsStationarySampleJustBeforeNewOne()
        {
            buffer.Reset(Vector3.zero, 0f);
            buffer.Add(1000, new Vector3(0, 0, 0), 0f);
            buffer.Add(1200, new Vector3(10, 0, 0), 0f); // 200ms > 150ms: 그 사이 멈춰 있었다

            // 멈춘 구간(1000~1150)에서는 미끄러지지 않는다.
            AssertVector(Vector3.zero, SamplePosition(1100));
            // 새 샘플 직전 50ms(1150~1200)에만 움직인다.
            AssertVector(new Vector3(5, 0, 0), SamplePosition(1175));
        }

        [Test]
        public void SmallGap_DoesNotInsertStationarySample()
        {
            buffer.Reset(Vector3.zero, 0f);
            buffer.Add(1000, new Vector3(0, 0, 0), 0f);
            buffer.Add(1100, new Vector3(10, 0, 0), 0f); // 100ms <= 150ms

            AssertVector(new Vector3(5, 0, 0), SamplePosition(1050));
        }

        [Test]
        public void OutOfOrderOrDuplicateSample_IsIgnored()
        {
            buffer.Reset(Vector3.zero, 0f);
            buffer.Add(1000, new Vector3(0, 0, 0), 0f);
            buffer.Add(1100, new Vector3(10, 0, 0), 0f);

            buffer.Add(1100, new Vector3(99, 0, 0), 0f); // 중복
            buffer.Add(1050, new Vector3(99, 0, 0), 0f); // 역순

            AssertVector(new Vector3(10, 0, 0), SamplePosition(1100));
            AssertVector(new Vector3(5, 0, 0), SamplePosition(1050));
        }

        [Test]
        public void MoreThanMaxSamples_DropsOldest()
        {
            buffer.Reset(Vector3.zero, 0f);
            for (int i = 0; i < 40; i++)
            {
                buffer.Add(1000 + i * 50, new Vector3(i, 0, 0), 0f);
            }

            // 40개 + 시작점 1개 = 41개 중 마지막 32개(i = 8..39)만 남는다. 오래된 쪽은 가장 오래 남은 샘플로 고정된다.
            AssertVector(new Vector3(8, 0, 0), SamplePosition(0));
            AssertVector(new Vector3(39, 0, 0), SamplePosition(100000));
        }

        [Test]
        public void Rotation_InterpolatesAlongShortestArc()
        {
            buffer.Reset(Vector3.zero, 350f);
            buffer.Add(1000, Vector3.zero, 350f);
            buffer.Add(1100, Vector3.zero, 10f);

            buffer.Sample(1050, out _, out float rotationY);

            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, rotationY), Tolerance);
        }

        [Test]
        public void Reset_DiscardsQueuedSamples()
        {
            buffer.Reset(Vector3.zero, 0f);
            buffer.Add(1000, new Vector3(10, 0, 0), 0f);

            buffer.Reset(new Vector3(7, 0, 0), 0f);

            AssertVector(new Vector3(7, 0, 0), SamplePosition(1000));
        }
    }
}
