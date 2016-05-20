﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;

namespace Org.Apache.CuratorNet.Client.Utils
{
    /**
     * Decoration on an ExecutorService that tracks created futures and provides
     * a method to close futures created via this class
     */
    public class CloseableExecutorService : IDisposable
    {
        private readonly Logger log = LogManager.GetCurrentClassLogger();
        private readonly Set<Future<?>> futures = Sets.newSetFromMap(Maps.< Future <?>, Boolean > newConcurrentMap());
        private readonly ExecutorService executorService;
        private readonly boolean shutdownOnClose;
        protected final AtomicBoolean isOpen = new AtomicBoolean(true);

        protected class InternalScheduledFutureTask implements Future<Void>
        {
            private final ScheduledFuture<?> scheduledFuture;

        public InternalScheduledFutureTask(ScheduledFuture<?> scheduledFuture)
        {
            this.scheduledFuture = scheduledFuture;
            futures.add(scheduledFuture);
        }

        @Override
            public boolean cancel(boolean mayInterruptIfRunning)
        {
            futures.remove(scheduledFuture);
            return scheduledFuture.cancel(mayInterruptIfRunning);
        }

        @Override
            public boolean isCancelled()
        {
            return scheduledFuture.isCancelled();
        }

        @Override
            public boolean isDone()
        {
            return scheduledFuture.isDone();
        }

        @Override
            public Void get() throws InterruptedException, ExecutionException
            {
                return null;
            }

    @Override
            public Void get(long timeout, TimeUnit unit) throws InterruptedException, ExecutionException, TimeoutException
            {
                return null;
            }
        }

        protected class InternalFutureTask<T> extends FutureTask<T>
        {
            private final RunnableFuture<T> task;

            InternalFutureTask(RunnableFuture<T> task)
    {
        super(task, null);
        this.task = task;
        futures.add(task);
    }

    protected void done()
    {
        futures.remove(task);
    }
        }

        /**
         * @param executorService the service to decorate
         */
        public CloseableExecutorService(ExecutorService executorService)
    {
        this(executorService, false);
    }

    /**
     * @param executorService the service to decorate
     * @param shutdownOnClose if true, shutdown the executor service when this is closed
     */
    public CloseableExecutorService(ExecutorService executorService, boolean shutdownOnClose)
    {
        this.executorService = Preconditions.checkNotNull(executorService, "executorService cannot be null");
        this.shutdownOnClose = shutdownOnClose;
    }

    /**
     * Returns <tt>true</tt> if this executor has been shut down.
     *
     * @return <tt>true</tt> if this executor has been shut down
     */
    public boolean isShutdown()
    {
        return !isOpen.get();
    }

    @VisibleForTesting
        int size()
    {
        return futures.size();
    }

    /**
     * Closes any tasks currently in progress
     */
    @Override
        public void close()
    {
        isOpen.set(false);
        Iterator < Future <?>> iterator = futures.iterator();
        while (iterator.hasNext())
        {
            Future <?> future = iterator.next();
            iterator.remove();
            if (!future.isDone() && !future.isCancelled() && !future.cancel(true))
            {
                log.warn("Could not cancel " + future);
            }
        }
        if (shutdownOnClose)
        {
            executorService.shutdownNow();
        }
    }

    /**
     * Submits a value-returning task for execution and returns a Future
     * representing the pending results of the task.  Upon completion,
     * this task may be taken or polled.
     *
     * @param task the task to submit
     * @return a future to watch the task
     */
    public<V> Future<V> submit(Callable<V> task)
    {
        Preconditions.checkState(isOpen.get(), "CloseableExecutorService is closed");

        InternalFutureTask<V> futureTask = new InternalFutureTask<V>(new FutureTask<V>(task));
        executorService.execute(futureTask);
        return futureTask;
    }

    /**
     * Submits a Runnable task for execution and returns a Future
     * representing that task.  Upon completion, this task may be
     * taken or polled.
     *
     * @param task the task to submit
     * @return a future to watch the task
     */
    public Future<?> submit(Runnable task)
    {
        Preconditions.checkState(isOpen.get(), "CloseableExecutorService is closed");

        InternalFutureTask<Void> futureTask = new InternalFutureTask<Void>(new FutureTask<Void>(task, null));
        executorService.execute(futureTask);
        return futureTask;
    }
    }

}
