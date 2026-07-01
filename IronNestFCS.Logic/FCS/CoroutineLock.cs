using System.Collections;

namespace IronNestFCS.Logic.FCS;

/// <summary>
/// 协程级互斥锁。MelonCoroutines 全部在 Unity 主线程上协作式调度，没有真正并发，
/// 因此一个 bool 足以实现互斥——不需要任何并发原语（lock/Interlocked/SemaphoreSlim）。
///
/// 用法（务必配 try/finally，保证协程被 Stop / yield break 时也能释放）：
/// <code>
/// yield return deskLock.Acquire();
/// try { /* 临界区，可含 yield return */ }
/// finally { deskLock.Release(); }
/// </code>
/// 迭代器被 MelonCoroutines.Stop 停掉时会 Dispose，finally 块照常执行 → 锁不会泄漏。
/// </summary>
public sealed class CoroutineLock {
    private bool _held;

    /// <summary>等待直到拿到锁。默认 30s 超时防死锁。</summary>
    public IEnumerator Acquire(float timeout = 30f) {
        float waited = 0f;
        while (_held && waited < timeout) {
            yield return null;
            waited += UnityEngine.Time.deltaTime;
        }
        _held = true;
    }

    public void Release() {
        _held = false;
    }

    /// <summary>重绑定（热重载）时强制复位，防止上一轮异常残留导致死锁。</summary>
    public void Reset() {
        _held = false;
    }
}
