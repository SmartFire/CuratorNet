﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Org.Apache.Java.Types.Concurrent.Futures;

namespace Org.Apache.Java.Types.Concurrent.Futures
{
    //    public class FutureTask<T> : IFuture<T>
    //    {
    //        protected readonly Task<T> Task;
    //
    //        public FutureTask(Task<T> task)
    //        {
    //            if (task == null)
    //            {
    //                throw new ArgumentNullException(nameof(task));
    //            }
    //            Task = task;
    //        }
    //
    //        public virtual void run()
    //        {
    //            if (Task.Status != TaskStatus.Created)
    //            {
    //                return;
    //            }
    //            Task.Start();
    //        }
    //
    //        public virtual bool cancel()
    //        {
    //            return false;
    //        }
    //
    //        public virtual T get()
    //        {
    //            Task.Wait();
    //            return Task.Result;
    //        }
    //
    //        public virtual T get(int timeoutMs)
    //        {
    //            Task.Wait(timeoutMs);
    //            return Task.Result;
    //        }
    //
    //        public virtual bool isCancelled()
    //        {
    //            return Task.IsCanceled;
    //        }
    //
    //        public virtual bool isDone()
    //        {
    //            return Task.IsCompleted;
    //        }
    //    }
















    public class FutureTask<T> : IRunnableFuture<T> {

        /**
         * The run state of this task, initially NEW.  The run state
         * transitions to a terminal state only in methods set,
         * setException, and cancel.  During completion, state may take on
         * transient values of COMPLETING (while outcome is being set) or
         * INTERRUPTING (only while interrupting the runner to satisfy a
         * cancel(true)). Transitions from these intermediate to final
         * states use cheaper ordered/lazy writes because values are unique
         * and cannot be further modified.
         *
         * Possible state transitions:
         * NEW -> COMPLETING -> NORMAL
         * NEW -> COMPLETING -> EXCEPTIONAL
         * NEW -> CANCELLED
         * NEW -> INTERRUPTING -> INTERRUPTED
         */
        private volatile int state;
        private const int NEW = 0;
        private const int COMPLETING = 1;
        private const int NORMAL = 2;
        private const int EXCEPTIONAL = 3;
        private const int CANCELLED = 4;
        private const int INTERRUPTING = 5;
        private const int INTERRUPTED = 6;

        /** The underlying callable; nulled out after running */
        private ICallable<T> callable;
        /** The result to return or exception to throw from get() */
        private Object outcome; // non-volatile, protected by state reads/writes
        /** The thread running the callable; CASed during run() */
        private volatile Thread runner;
        /** Treiber stack of waiting threads */
        private volatile WaitNode waiters;

        /**
         * Returns result or throws exception for completed task.
         *
         * @param s completed state value
         */
        private T report(int s)
        {
            Object x = outcome;
            if (s == NORMAL)
                return (T)x;
            if (s >= CANCELLED)
                throw new TaskCanceledException();
            throw new InvalidOperationException((Throwable)x);
        }

        /**
         * Creates a {@code FutureTask} that will, upon running, execute the
         * given {@code Callable}.
         *
         * @param  callable the callable task
         * @throws NullPointerException if the callable is null
         */
        public FutureTask(ICallable<T> callable)
        {
            if (callable == null)
            {
                throw new ArgumentNullException(nameof(callable));
            }
            this.callable = callable;
            this.state = NEW;       // ensure visibility of callable
        }

        /**
         * Creates a {@code FutureTask} that will, upon running, execute the
         * given {@code Runnable}, and arrange that {@code get} will return the
         * given result on successful completion.
         *
         * @param runnable the runnable task
         * @param result the result to return on successful completion. If
         * you don't need a particular result, consider using
         * constructions of the form:
         * {@code Future<?> f = new FutureTask<Void>(runnable, null)}
         * @throws NullPointerException if the runnable is null
         */
        public FutureTask(IRunnable runnable)
        {
            this.callable = new RunnableCallable<T>(runnable);
            this.state = NEW;       // ensure visibility of callable
        }

        public bool isCancelled()
        {
            return state >= CANCELLED;
        }

        public bool isDone()
        {
            return state != NEW;
        }

        public bool cancel(bool mayInterruptIfRunning)
        {
            int newState = mayInterruptIfRunning 
                            ? INTERRUPTING 
                            : CANCELLED;
            if (!(state == NEW &&
                    Interlocked.CompareExchange(ref state, newState, NEW) == NEW))
            {
                return false;
            }
            try
            {    // in case call to interrupt throws exception
                if (mayInterruptIfRunning)
                {
                    try
                    {
                        Thread t = runner;
                        if (t != null)
                            t.interrupt();
                    }
                    finally
                    { // final state
                        UNSAFE.putOrderedInt(this, stateOffset, INTERRUPTED);
                    }
                }
            }
            finally
            {
                finishCompletion();
            }
            return true;
        }

        /**
         * @throws CancellationException {@inheritDoc}
         */
        public T get()
        {
            int s = state;
            if (s <= COMPLETING)
                s = awaitDone(false, 0L);
            return report(s);
        }

        /**
         * @throws CancellationException {@inheritDoc}
         */
        public T get(long timeout, TimeUnit unit)
        {
            if (unit == null)
            {
                throw new ArgumentNullException(nameof(unit));
            }
            int s = state;
            if (s <= COMPLETING &&
                (s = awaitDone(true, unit.toNanos(timeout))) <= COMPLETING)
            {
                throw new TimeoutException();
            }
            return report(s);
        }

        /**
         * Protected method invoked when this task transitions to state
         * {@code isDone} (whether normally or via cancellation). The
         * default implementation does nothing.  Subclasses may override
         * this method to invoke completion callbacks or perform
         * bookkeeping. Note that you can query status inside the
         * implementation of this method to determine whether this task
         * has been cancelled.
         */
        protected virtual void done() { }

        /**
         * Sets the result of this future to the given value unless
         * this future has already been set or has been cancelled.
         *
         * <p>This method is invoked internally by the {@link #run} method
         * upon successful completion of the computation.
         *
         * @param v the value
         */
        protected void set(T v)
        {
            if (Interlocked.CompareExchange(ref state, COMPLETING, NEW) == NEW)
            {
                outcome = v;
                Interlocked.Exchange(ref state, NORMAL); // final state
                finishCompletion();
            }
        }

        /**
         * Causes this future to report an {@link ExecutionException}
         * with the given throwable as its cause, unless this future has
         * already been set or has been cancelled.
         *
         * <p>This method is invoked internally by the {@link #run} method
         * upon failure of the computation.
         *
         * @param t the cause of failure
         */
        protected void setException(Exception t)
        {
            if (Interlocked.CompareExchange(ref state, COMPLETING, NEW) == NEW)
            {
                outcome = t;
                Interlocked.Exchange(ref state, EXCEPTIONAL); // final state
                finishCompletion();
            }
        }

        public void run()
        {
            if (state != NEW 
                    || Interlocked.CompareExchange(ref runner, Thread.CurrentThread,null) != null)
            {
                return;
            }
            try
            {
                ICallable<T> c = callable;
                if (c != null && state == NEW)
                {
                    T result;
                    bool ran;
                    try
                    {
                        result = c.call();
                        ran = true;
                    }
                    catch (Exception ex)
                    {
                        result = default(T);
                        ran = false;
                        setException(ex);
                    }
                    if (ran)
                        set(result);
                }
            }
            finally
            {
                // runner must be non-null until state is settled to
                // prevent concurrent calls to run()
                runner = null;
                // state must be re-read after nulling runner to prevent
                // leaked interrupts
                int s = state;
                if (s >= INTERRUPTING)
                    handlePossibleCancellationInterrupt(s);
            }
        }

        /**
         * Executes the computation without setting its result, and then
         * resets this future to initial state, failing to do so if the
         * computation encounters an exception or is cancelled.  This is
         * designed for use with tasks that intrinsically execute more
         * than once.
         *
         * @return {@code true} if successfully run and reset
         */
        protected bool runAndReset()
        {
            if (state != NEW ||
                Interlocked.CompareExchange(ref runner, Thread.currentThread(),null) != null)
            {
                return false;
            }
            bool ran = false;
            int s = state;
            try
            {
                ICallable<T> c = callable;
                if (c != null && s == NEW)
                {
                    try
                    {
                        c.call(); // don't set result
                        ran = true;
                    }
                    catch (Exception ex)
                    {
                        setException(ex);
                    }
                }
            }
            finally
            {
                // runner must be non-null until state is settled to
                // prevent concurrent calls to run()
                runner = null;
                // state must be re-read after nulling runner to prevent
                // leaked interrupts
                s = state;
                if (s >= INTERRUPTING)
                    handlePossibleCancellationInterrupt(s);
            }
            return ran && s == NEW;
        }

        /**
         * Ensures that any interrupt from a possible cancel(true) is only
         * delivered to a task while in run or runAndReset.
         */
        private void handlePossibleCancellationInterrupt(int s)
        {
            // It is possible for our interrupter to stall before getting a
            // chance to interrupt us.  Let's spin-wait patiently.
            if (s == INTERRUPTING)
            {
                while (state == INTERRUPTING)
                {
                    Thread.Yield(); // wait out pending interrupt
                }
            }

            // assert state == INTERRUPTED;

            // We want to clear any interrupt we may have received from
            // cancel(true).  However, it is permissible to use interrupts
            // as an independent mechanism for a task to communicate with
            // its caller, and there is no way to clear only the
            // cancellation interrupt.
            //
            // Thread.interrupted();
        }

        /**
         * Simple linked list nodes to record waiting threads in a Treiber
         * stack.  See other classes such as Phaser and SynchronousQueue
         * for more detailed explanation.
         */
        static final class WaitNode
        {
            volatile Thread thread;
            volatile WaitNode next;
            WaitNode() { thread = Thread.currentThread(); }
        }

        /**
         * Removes and signals all waiting threads, invokes done(), and
         * nulls out callable.
         */
        private void finishCompletion()
        {
            // assert state > COMPLETING;
            for (WaitNode q; (q = waiters) != null;)
            {
                if (Interlocked.CompareExchange(ref waiters, null, q) == q)
                {
                    for (;;)
                    {
                        Thread t = q.thread;
                        if (t != null)
                        {
                            q.thread = null;
                            LockSupport.unpark(t);
                        }
                        WaitNode next = q.next;
                        if (next == null)
                            break;
                        q.next = null; // unlink to help gc
                        q = next;
                    }
                    break;
                }
            }

            done();

            callable = null;        // to reduce footprint
        }

        /**
         * Awaits completion or aborts on interrupt or timeout.
         *
         * @param timed true if use timed waits
         * @param nanos time to wait, if timed
         * @return state upon completion
         */
        private int awaitDone(boolean timed, long nanos)
                throws InterruptedException
        {
            final long deadline = timed ? System.nanoTime() + nanos : 0L;
            WaitNode q = null;
            boolean queued = false;
                for (;;) {
                if (Thread.interrupted())
                {
                    removeWaiter(q);
                    throw new InterruptedException();
                }

                int s = state;
                if (s > COMPLETING)
                {
                    if (q != null)
                        q.thread = null;
                    return s;
                }
                else if (s == COMPLETING) // cannot time out yet
                    Thread.yield();
                else if (q == null)
                    q = new WaitNode();
                else if (!queued)
                    queued = UNSAFE.compareAndSwapObject(this, waitersOffset,
                                                         q.next = waiters, q);
                else if (timed)
                {
                    nanos = deadline - System.nanoTime();
                    if (nanos <= 0L)
                    {
                        removeWaiter(q);
                        return state;
                    }
                    LockSupport.parkNanos(this, nanos);
                }
                else
                    LockSupport.park(this);
            }
        }

        /**
         * Tries to unlink a timed-out or interrupted wait node to avoid
         * accumulating garbage.  Internal nodes are simply unspliced
         * without CAS since it is harmless if they are traversed anyway
         * by releasers.  To avoid effects of unsplicing from already
         * removed nodes, the list is retraversed in case of an apparent
         * race.  This is slow when there are a lot of nodes, but we don't
         * expect lists to be long enough to outweigh higher-overhead
         * schemes.
         */
        private void removeWaiter(WaitNode node)
        {
            if (node != null)
            {
                node.thread = null;
                retry:
                for (;;)
                {          // restart on removeWaiter race
                    for (WaitNode pred = null, q = waiters, s; q != null; q = s)
                    {
                        s = q.next;
                        if (q.thread != null)
                            pred = q;
                        else if (pred != null)
                        {
                            pred.next = s;
                            if (pred.thread == null) // check for race
                                continue retry;
                        }
                        else if (!UNSAFE.compareAndSwapObject(this, waitersOffset,
                                                              q, s))
                            continue retry;
                    }
                    break;
                }
            }
        }

        // Unsafe mechanics
        private static final sun.misc.Unsafe UNSAFE;
        private static final long stateOffset;
        private static final long runnerOffset;
        private static final long waitersOffset;
        static {
                try {
                    UNSAFE = sun.misc.Unsafe.getUnsafe();
                    Class<?> k = FutureTask<>.class;
        stateOffset = UNSAFE.objectFieldOffset
                        (k.getDeclaredField("state"));
                    runnerOffset = UNSAFE.objectFieldOffset
                        (k.getDeclaredField("runner"));
                    waitersOffset = UNSAFE.objectFieldOffset
                        (k.getDeclaredField("waiters"));
                } catch (Exception e) {
                    throw new Error(e);
                }
            }

        }
}
