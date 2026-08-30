using System;
using System.Runtime.InteropServices;
using System.Threading;
using Il2CppInterop.Common;
using Microsoft.Extensions.Logging;

namespace Il2CppInterop.Runtime.Injection
{
    internal abstract class Hook<T> where T : Delegate
    {
        private bool _isApplied;
        private T _detour;
        private T _method;
        private T _original;
        private int _exceptionReported;

        public T Original => _original;

        public abstract string TargetMethodName { get; }
        public abstract T GetDetour();
        public abstract IntPtr FindTargetMethod();

        protected void ReportManagedException(Exception exception)
        {
            if (Interlocked.Exchange(ref _exceptionReported, 1) != 0)
                return;

            try
            {
                LogManagedException(exception);
            }
            catch
            {
                // Logging must never let an exception escape a reverse P/Invoke hook.
            }
        }

        protected virtual void LogManagedException(Exception exception) =>
            Logger.Instance.LogError(
                exception,
                "Managed exception in native hook {TargetMethodName}; falling back to the original IL2CPP method. Further exceptions from this hook will be suppressed.",
                TargetMethodName);

        public virtual void TargetMethodNotFound()
        {
            throw new Exception($"Required target method {TargetMethodName} not found");
        }

        public void ApplyHook()
        {
            if (_isApplied) return;

            var methodPtr = FindTargetMethod();

            if (methodPtr == IntPtr.Zero)
            {
                TargetMethodNotFound();
                return;
            }

            Logger.Instance.LogTrace("{MethodName} found: 0x{MethodPtr}", TargetMethodName, methodPtr.ToInt64().ToString("X2"));

            _detour = GetDetour();
            Detour.Apply(methodPtr, _detour, out _original);
            _method = Marshal.GetDelegateForFunctionPointer<T>(methodPtr);
            _isApplied = true;
        }
    }
}
