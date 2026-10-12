using System.Collections.Generic;
using Incheol.Modules;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Incheol.Tests
{
    // EditMode에서 Object.Destroy는 실제로 파괴하지 않고 오류 로그만 남긴다. 그래서 "파괴 직전에 일어나는 일"(부모 분리, 추적 정보 유지,
    // 사전 정리)까지만 검증하고, 파괴를 부르는 테스트는 그 로그를 무시한다. 만든 오브젝트는 TearDown에서 DestroyImmediate로 정리한다.
    public class ObjectPoolStorageTests
    {
        private class OwnerStub : MonoBehaviour { }

        private readonly List<GameObject> created = new List<GameObject>();
        private OwnerStub owner;
        private ObjectPoolStorage storage;

        [SetUp]
        public void CreateStorage()
        {
            owner = Make("Owner").AddComponent<OwnerStub>();
            storage = new ObjectPoolStorage(owner);
        }

        [TearDown]
        public void Cleanup()
        {
            LogAssert.ignoreFailingMessages = false;

            foreach (GameObject go in created)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            created.Clear();
        }

        private GameObject Make(string name)
        {
            var go = new GameObject(name);
            created.Add(go);
            return go;
        }

        #region HasPool / EnsurePool
        [Test]
        public void HasPool_FalseForNullEmptyOrUnknownKey()
        {
            Assert.IsFalse(storage.HasPool(null));
            Assert.IsFalse(storage.HasPool(""));
            Assert.IsFalse(storage.HasPool("unknown"));
        }

        [Test]
        public void EnsurePool_RegistersKey()
        {
            storage.EnsurePool("bullet");

            Assert.IsTrue(storage.HasPool("bullet"));
        }
        #endregion

        #region Enqueue / Dequeue
        [Test]
        public void Dequeue_ReturnsEnqueuedInstancesInOrder()
        {
            GameObject first = Make("first");
            GameObject second = Make("second");
            storage.Enqueue("k", first);
            storage.Enqueue("k", second);

            Assert.AreSame(first, storage.Dequeue("k"));
            Assert.AreSame(second, storage.Dequeue("k"));
        }

        [Test]
        public void Dequeue_EmptyPool_ReturnsNull()
        {
            Assert.IsNull(storage.Dequeue("k"));
        }

        [Test]
        public void Dequeue_SkipsAlreadyDestroyedInstances()
        {
            GameObject destroyed = Make("destroyed");
            GameObject alive = Make("alive");
            storage.Enqueue("k", destroyed);
            storage.Enqueue("k", alive);
            Object.DestroyImmediate(destroyed);

            Assert.AreSame(alive, storage.Dequeue("k"));
        }
        #endregion

        #region Capacity
        [Test]
        public void TryEnqueueReleased_WithoutCapacity_AlwaysAccepts()
        {
            for (int i = 0; i < 20; i++)
            {
                Assert.IsTrue(storage.TryEnqueueReleased("k", Make($"go{i}")));
            }
        }

        [Test]
        public void TryEnqueueReleased_RejectsWhenFull()
        {
            storage.SetCapacity("k", 2);

            Assert.IsTrue(storage.TryEnqueueReleased("k", Make("a")));
            Assert.IsTrue(storage.TryEnqueueReleased("k", Make("b")));
            Assert.IsFalse(storage.TryEnqueueReleased("k", Make("c")));
        }

        [Test]
        public void TryEnqueueReleased_AcceptsAgainAfterDequeue()
        {
            storage.SetCapacity("k", 1);
            storage.TryEnqueueReleased("k", Make("a"));

            storage.Dequeue("k");

            Assert.IsTrue(storage.TryEnqueueReleased("k", Make("b")));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void SetCapacity_ZeroOrNegative_MeansUnlimited(int maxSize)
        {
            storage.SetCapacity("k", 1);
            storage.TryEnqueueReleased("k", Make("a"));

            storage.SetCapacity("k", maxSize);

            Assert.IsTrue(storage.TryEnqueueReleased("k", Make("b")));
        }

        [Test]
        public void Capacity_IsPerKey()
        {
            storage.SetCapacity("limited", 1);
            storage.TryEnqueueReleased("limited", Make("a"));

            Assert.IsFalse(storage.TryEnqueueReleased("limited", Make("b")));
            Assert.IsTrue(storage.TryEnqueueReleased("other", Make("c")));
        }

        [Test]
        public void Enqueue_IgnoresCapacity()
        {
            storage.SetCapacity("k", 1);

            storage.Enqueue("k", Make("a"));
            storage.Enqueue("k", Make("b"));

            Assert.IsFalse(storage.TryEnqueueReleased("k", Make("c")));
            Assert.IsNotNull(storage.Dequeue("k"));
            Assert.IsNotNull(storage.Dequeue("k"));
        }
        #endregion

        #region Track / GetTrackedKeys
        [Test]
        public void TryUntrack_ReturnsKeyOnce()
        {
            GameObject go = Make("borrowed");
            storage.Track(go, "k");

            Assert.IsTrue(storage.TryUntrack(go, out string key));
            Assert.AreEqual("k", key);
            Assert.IsFalse(storage.TryUntrack(go, out _));
        }

        [Test]
        public void TryUntrack_UntrackedInstance_ReturnsFalse()
        {
            Assert.IsFalse(storage.TryUntrack(Make("stranger"), out string key));
            Assert.IsNull(key);
        }

        [Test]
        public void Track_ThenUntrack_ThenTrackAgain_Works()
        {
            GameObject go = Make("borrowed");

            storage.Track(go, "k");
            storage.TryUntrack(go, out _);
            storage.Track(go, "k2");

            Assert.IsTrue(storage.TryUntrack(go, out string key));
            Assert.AreEqual("k2", key);
        }

        [Test]
        public void GetTrackedKeys_IncludesPooledAndBorrowedKeys()
        {
            storage.EnsurePool("pooled");
            storage.Track(Make("borrowed"), "borrowedOnly");

            HashSet<string> keys = storage.GetTrackedKeys();

            CollectionAssert.AreEquivalent(new[] { "pooled", "borrowedOnly" }, keys);
        }

        [Test]
        public void GetTrackedKeys_SameKeyInBoth_AppearsOnce()
        {
            storage.EnsurePool("k");
            storage.Track(Make("borrowed"), "k");

            Assert.AreEqual(1, storage.GetTrackedKeys().Count);
        }
        #endregion

        #region GetOrCreateRoot
        [Test]
        public void GetOrCreateRoot_BuildsHierarchyUnderOwner()
        {
            Transform root = storage.GetOrCreateRoot("bullet");

            Assert.AreEqual("Pool_bullet", root.name);
            Assert.AreEqual(ObjectPoolStorage.PoolRootName, root.parent.name);
            Assert.AreSame(owner.transform, root.parent.parent);
        }

        [Test]
        public void GetOrCreateRoot_SameKey_ReturnsSameTransform()
        {
            Assert.AreSame(storage.GetOrCreateRoot("k"), storage.GetOrCreateRoot("k"));
        }

        [Test]
        public void GetOrCreateRoot_DifferentKeys_ShareOnePoolRoot()
        {
            Transform a = storage.GetOrCreateRoot("a");
            Transform b = storage.GetOrCreateRoot("b");

            Assert.AreNotSame(a, b);
            Assert.AreSame(a.parent, b.parent);
        }

        [Test]
        public void GetOrCreateRoot_OwnerDestroyed_ReturnsNull()
        {
            Object.DestroyImmediate(owner.gameObject);

            Assert.IsNull(storage.GetOrCreateRoot("k"));
        }
        #endregion

        #region DestroyPool / DestroyAll
        [Test]
        public void DestroyPool_KeepActive_DetachesBorrowedInstanceFromRoot_AndKeepsTracking()
        {
            LogAssert.ignoreFailingMessages = true; // EditMode의 Object.Destroy 오류 로그
            storage.EnsurePool("k");
            Transform root = storage.GetOrCreateRoot("k");
            GameObject borrowed = Make("borrowed");
            borrowed.transform.SetParent(root, false);
            storage.Track(borrowed, "k");

            storage.DestroyPool("k", destroyActive: false);

            Assert.IsNull(borrowed.transform.parent, "루트와 함께 파괴되지 않도록 분리돼야 한다");
            Assert.IsFalse(storage.HasPool("k"));
            Assert.IsTrue(storage.TryUntrack(borrowed, out string key), "추적은 유지돼 이후 Release로 반환할 수 있어야 한다");
            Assert.AreEqual("k", key);
        }

        [Test]
        public void DestroyPool_DestroyActive_StopsTrackingBorrowedInstance()
        {
            LogAssert.ignoreFailingMessages = true;
            GameObject borrowed = Make("borrowed");
            storage.Track(borrowed, "k");

            storage.DestroyPool("k", destroyActive: true);

            Assert.IsFalse(storage.TryUntrack(borrowed, out _));
        }

        [Test]
        public void DestroyPool_OtherKeys_AreUntouched()
        {
            LogAssert.ignoreFailingMessages = true;
            storage.EnsurePool("a");
            storage.EnsurePool("b");
            GameObject other = Make("other");
            storage.Track(other, "b");

            storage.DestroyPool("a", destroyActive: true);

            Assert.IsFalse(storage.HasPool("a"));
            Assert.IsTrue(storage.HasPool("b"));
            Assert.IsTrue(storage.TryUntrack(other, out _));
        }

        [Test]
        public void DestroyAll_KeepActive_DetachesBorrowedAndKeepsTracking()
        {
            LogAssert.ignoreFailingMessages = true;
            storage.EnsurePool("k");
            Transform root = storage.GetOrCreateRoot("k");
            GameObject borrowed = Make("borrowed");
            borrowed.transform.SetParent(root, false);
            storage.Track(borrowed, "k");

            storage.DestroyAll(destroyActive: false, captureKeys: false);

            Assert.IsNull(borrowed.transform.parent);
            Assert.IsFalse(storage.HasPool("k"));
            Assert.IsTrue(storage.TryUntrack(borrowed, out _));
        }

        [Test]
        public void DestroyAll_DestroyActive_ClearsTracking()
        {
            LogAssert.ignoreFailingMessages = true;
            GameObject borrowed = Make("borrowed");
            storage.Track(borrowed, "k");

            storage.DestroyAll(destroyActive: true, captureKeys: false);

            Assert.IsFalse(storage.TryUntrack(borrowed, out _));
        }

        [Test]
        public void DestroyAll_CaptureKeys_ReturnsEveryKeyThatHadARoot()
        {
            LogAssert.ignoreFailingMessages = true;
            storage.GetOrCreateRoot("a");
            storage.GetOrCreateRoot("b");

            List<string> keys = storage.DestroyAll(destroyActive: false, captureKeys: true);

            CollectionAssert.AreEquivalent(new[] { "a", "b" }, keys);
        }

        [Test]
        public void DestroyAll_WithoutCaptureKeys_ReturnsNull()
        {
            LogAssert.ignoreFailingMessages = true;
            storage.GetOrCreateRoot("a");

            Assert.IsNull(storage.DestroyAll(destroyActive: false, captureKeys: false));
        }
        #endregion
    }
}
