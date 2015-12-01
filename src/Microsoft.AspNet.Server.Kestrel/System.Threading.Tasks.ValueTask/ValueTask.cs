// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;

// See: https://github.com/dotnet/corefx/issues/4708

namespace System.Threading.Tasks
{
    /// <summary>Value type discriminated union for a TResult and a <see cref="Task{TResult}"/>.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    [DebuggerDisplay("Status = {DebuggerStatus}, Result = {DebuggerResult}")]
    public struct ValueTask<TResult> : IEquatable<ValueTask<TResult>>
    {
        /// <summary>The task. this will be non-null iff the operation didn't complete successfully synchronously.</summary>
        private readonly Task<TResult> _task;
        /// <summary>The result to be used if the operation completed successfully synchronously.</summary>
        private readonly TResult _result;

        /// <summary>Initialize the TaskValue with the result of the successful operation.</summary>
        /// <param name="result">The result.</param>
        public ValueTask(TResult result)
        {
            _result = result;
            _task = null;
        }

        /// <summary>
        /// Initialize the TaskValue with a <see cref="Task{TResult}"/> that represents 
        /// the non-successful or incomplete operation.
        /// </summary>
        /// <param name="task"></param>
        public ValueTask(Task<TResult> task)
        {
            Debug.Assert(task != null);
            _result = default(TResult);
            _task = task;
        }

        /// <summary>Implicit operator to wrap a TaskValue around a task.</summary>
        public static implicit operator ValueTask<TResult>(Task<TResult> task)
        {
            return new ValueTask<TResult>(task);
        }

        /// <summary>Implicit operator to wrap a TaskValue around a result.</summary>
        public static implicit operator ValueTask<TResult>(TResult result)
        {
            return new ValueTask<TResult>(result);
        }

        /// <summary>Returns the hash code for this instance.</summary>
        public override int GetHashCode()
        {
            return
                _task != null ? _task.GetHashCode() :
                _result != null ? _result.GetHashCode() :
                0;
        }

        /// <summary>Returns a value indicating whether this value is equal to a specified <see cref="object"/>.</summary>
        public override bool Equals(object obj)
        {
            return
                obj is ValueTask<TResult> &&
                Equals((ValueTask<TResult>)obj);
        }

        /// <summary>Returns a value indicating whether this value is equal to a specified <see cref="ValueTask{TResult}"/> value.</summary>
        public bool Equals(ValueTask<TResult> other)
        {
            if (_task == null && other._task == null)
            {
                if (_result == null)
                {
                    return other._result == null;
                }
                else
                {
                    return other._result != null && _result.Equals(other._result);
                }
            }

            return _task == other._task;
        }

        /// <summary>Returns a value indicating whether two <see cref="ValueTask{TResult}"/> values are equal.</summary>
        public static bool operator ==(ValueTask<TResult> left, ValueTask<TResult> right)
        {
            return left.Equals(right);
        }

        /// <summary>Returns a value indicating whether two <see cref="ValueTask{TResult}"/> values are not equal.</summary>
        public static bool operator !=(ValueTask<TResult> left, ValueTask<TResult> right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Gets a <see cref="Task{TResult}"/> object to represent this TaskValue.  It will
        /// either return the wrapped task object if one exists, or it'll manufacture a new
        /// task object to represent the result.
        /// </summary>
        public Task<TResult> AsTask()
        {
            return _task ?? Task.FromResult(_result);
        }

        /// <summary>Gets whether the TaskValue represents a successfully completed operation.</summary>
        public bool IsRanToCompletion
        {
            get { return _task == null || _task.Status == TaskStatus.RanToCompletion; }
        }

        /// <summary>Gets the result.</summary>
        public TResult Result
        {
            get { return _task == null ? _result : _task.GetAwaiter().GetResult(); }
        }

        /// <summary>Gets an awaiter for this value.</summary>
        public ValueTaskAwaiter GetAwaiter()
        {
            return new ValueTaskAwaiter(this, continueOnCapturedContext: true);
        }

        /// <summary>Configures an awaiter for this value.</summary>
        /// <param name="continueOnCapturedContext">true to attempt to marshal the continuation back to the captured context; otherwise, false.</param>
        public ValueTaskAwaiter ConfigureAwait(bool continueOnCapturedContext)
        {
            return new ValueTaskAwaiter(this, continueOnCapturedContext: continueOnCapturedContext);
        }

        /// <summary>Gets a TaskStatus for the debugger to display.</summary>
        private TaskStatus DebuggerStatus
        {
            get { return _task == null ? TaskStatus.RanToCompletion : _task.Status; }
        }

        /// <summary>Gets a result string for the debugger to display.</summary>
        private string DebuggerResult
        {
            get
            {
                return
                    _task == null ? _result.ToString() :
                    _task.Status == TaskStatus.RanToCompletion ? _task.Result.ToString() :
                    "Channels.Properties.Resources.Debugger_TaskResultNotAvailable";
            }
        }

        /// <summary>Provides an awaiter for a TaskValue.</summary>
        public struct ValueTaskAwaiter : ICriticalNotifyCompletion
        {
            /// <summary>The value being awaited.</summary>
            private readonly ValueTask<TResult> _value;
            /// <summary>The value to pass to ConfigureAwait.</summary>
            private readonly bool _continueOnCapturedContext;

            /// <summary>Initializes the awaiter.</summary>
            /// <param name="value">The value to be awaited.</param>
            /// <param name="continueOnCapturedContext">The value to pass to ConfigureAwait.</param>
            public ValueTaskAwaiter(ValueTask<TResult> value, bool continueOnCapturedContext)
            {
                _value = value;
                _continueOnCapturedContext = continueOnCapturedContext;
            }

            /// <summary>Returns this awaiter.</summary>
            public ValueTaskAwaiter GetAwaiter() { return this; }

            /// <summary>Gets whether the TaskValue has completed.</summary>
            public bool IsCompleted
            {
                get { return _value._task == null || _value._task.IsCompleted; }
            }

            /// <summary>Gets the result of the TaskValue.</summary>
            public TResult GetResult()
            {
                return _value._task == null ?
                    _value._result :
                    _value._task.GetAwaiter().GetResult();
            }

            /// <summary>Schedules the continuation action for this TaskValue.</summary>
            public void OnCompleted(Action continuation)
            {
                _value.AsTask().ConfigureAwait(_continueOnCapturedContext).GetAwaiter().OnCompleted(continuation);
            }

            /// <summary>Schedules the continuation action for this TaskValue.</summary>
            public void UnsafeOnCompleted(Action continuation)
            {
                _value.AsTask().ConfigureAwait(_continueOnCapturedContext).GetAwaiter().UnsafeOnCompleted(continuation);
            }
        }

    }
}