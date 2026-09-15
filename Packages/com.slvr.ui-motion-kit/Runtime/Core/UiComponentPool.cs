using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace SLVR.UIMotion
{
    public interface IUiPoolable
    {
        void OnRentFromPool();
        void OnReturnToPool();
    }

    public readonly struct UiPoolMetricsSnapshot
    {
        public UiPoolMetricsSnapshot(int pools, int active, int inactive)
        {
            Pools = pools;
            Active = active;
            Inactive = inactive;
        }
        public int Pools { get; }
        public int Active { get; }
        public int Inactive { get; }
    }

    public static class UiPoolMetrics
    {
        private static int pools;
        private static int active;
        private static int inactive;
        public static UiPoolMetricsSnapshot Capture() => new UiPoolMetricsSnapshot(
            Volatile.Read(ref pools), Volatile.Read(ref active), Volatile.Read(ref inactive));
        internal static void Pool(int delta) => Interlocked.Add(ref pools, delta);
        internal static void Active(int delta) => Interlocked.Add(ref active, delta);
        internal static void Inactive(int delta) => Interlocked.Add(ref inactive, delta);
    }

    /// <summary>Main-thread Unity component pool with deterministic active-instance cleanup.</summary>
    public sealed class UiComponentPool<T> : IDisposable where T : Component
    {
        private readonly Func<T> factory;
        private readonly Transform inactiveRoot;
        private readonly int maximumRetained;
        private readonly Stack<T> inactive;
        private readonly HashSet<T> active;
        private readonly List<T> activeBuffer;
        private bool disposed;

        public UiComponentPool(Func<T> factory, Transform inactiveRoot = null, int preload = 0, int maximumRetained = 64)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.inactiveRoot = inactiveRoot;
            this.maximumRetained = Mathf.Max(0, maximumRetained);
            inactive = new Stack<T>(Mathf.Max(0, preload));
            active = new HashSet<T>();
            activeBuffer = new List<T>(Mathf.Max(4, preload));
            UiPoolMetrics.Pool(1);

            for (int i = 0; i < preload; i++)
            {
                T instance = Create();
                StoreInactive(instance);
            }
        }

        public int ActiveCount => active.Count;
        public int InactiveCount => inactive.Count;
        public int TotalCount => active.Count + inactive.Count;

        public T Rent(Transform parent = null)
        {
            if (disposed) throw new ObjectDisposedException(nameof(UiComponentPool<T>));
            T instance = null;
            while (inactive.Count > 0 && instance == null)
            {
                instance = inactive.Pop();
                UiPoolMetrics.Inactive(-1);
            }

            if (instance == null) instance = Create();
            if (parent != null) instance.transform.SetParent(parent, false);
            active.Add(instance);
            UiPoolMetrics.Active(1);
            instance.gameObject.SetActive(true);
            if (instance is IUiPoolable poolable) poolable.OnRentFromPool();
            return instance;
        }

        public bool Return(T instance)
        {
            if (ReferenceEquals(instance, null) || !active.Remove(instance)) return false;
            UiPoolMetrics.Active(-1);
            if (instance == null) return true;
            if (instance is IUiPoolable poolable) poolable.OnReturnToPool();

            if (!disposed && inactive.Count < maximumRetained)
            {
                StoreInactive(instance);
            }
            else
            {
                DestroyInstance(instance);
            }

            return true;
        }

        public void ReturnAll()
        {
            activeBuffer.Clear();
            foreach (T instance in active) activeBuffer.Add(instance);
            for (int i = 0; i < activeBuffer.Count; i++) Return(activeBuffer[i]);
            activeBuffer.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            ReturnAll();
            disposed = true;
            UiPoolMetrics.Pool(-1);
            while (inactive.Count > 0)
            {
                UiPoolMetrics.Inactive(-1);
                DestroyInstance(inactive.Pop());
            }
        }

        private T Create()
        {
            T instance = factory();
            if (instance == null) throw new InvalidOperationException("UI component pool factory returned null.");
            return instance;
        }

        private void StoreInactive(T instance)
        {
            if (instance == null) return;
            if (inactiveRoot != null) instance.transform.SetParent(inactiveRoot, false);
            instance.gameObject.SetActive(false);
            inactive.Push(instance);
            UiPoolMetrics.Inactive(1);
        }

        private static void DestroyInstance(T instance)
        {
            if (instance == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(instance.gameObject);
            else UnityEngine.Object.DestroyImmediate(instance.gameObject);
        }
    }
}
